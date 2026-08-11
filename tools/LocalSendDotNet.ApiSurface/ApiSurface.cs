using System.Reflection;

namespace LocalSendDotNet.ApiSurface;

internal static class PublicApiSurface
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static string Create(Assembly assembly)
    {
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes().OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            lines.Add(TypeHeader(type));
            const BindingFlags declaredPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(declaredPublic).Where(static field => field.Name != "value__").OrderBy(static field => field.Name, StringComparer.Ordinal))
                lines.Add($"  field {FormatType(field.FieldType, Nullability.Create(field))} {field.Name}{FormatConstant(field)}");
            foreach (var constructor in type.GetConstructors(declaredPublic).OrderBy(Signature, StringComparer.Ordinal))
                lines.Add("  ctor " + Signature(constructor));
            foreach (var property in type.GetProperties(declaredPublic).OrderBy(static property => property.Name, StringComparer.Ordinal))
                lines.Add($"  property {FormatType(property.PropertyType, Nullability.Create(property))} {property.Name}{Required(property)} {{ {Accessors(property)} }}");
            foreach (var method in type.GetMethods(declaredPublic).Where(static method => !method.IsSpecialName && method.Name is not ("<Clone>$" or "Equals" or "GetHashCode" or "ToString")).OrderBy(Signature, StringComparer.Ordinal))
                lines.Add($"  method {FormatType(method.ReturnType, Nullability.Create(method.ReturnParameter))} {Signature(method)}");
        }
        return string.Join('\n', lines);
    }

    private static string TypeHeader(Type type)
    {
        var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
        var modifiers = type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract && !type.IsInterface ? "abstract " : type.IsSealed && !type.IsValueType && !type.IsEnum ? "sealed " : string.Empty;
        var contracts = new List<Type>();
        if (type.BaseType is not null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType) && type.BaseType != typeof(Enum))
            contracts.Add(type.BaseType);
        contracts.AddRange(type.GetInterfaces().OrderBy(static contract => contract.FullName, StringComparer.Ordinal));
        var suffix = contracts.Count == 0 ? string.Empty : " : " + string.Join(", ", contracts.Select(static contract => FormatType(contract)));
        return $"type {modifiers}{kind} {FormatType(type)}{suffix}";
    }

    private static string Signature(MethodBase method) => $"{method.Name}({string.Join(", ", method.GetParameters().Select(FormatParameter))})";

    private static string FormatParameter(ParameterInfo parameter)
    {
        var modifier = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
        var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var optional = parameter.HasDefaultValue ? " = " + (parameter.DefaultValue is null && type.IsValueType ? "default" : FormatValue(parameter.DefaultValue)) : string.Empty;
        return $"{modifier}{FormatType(type, Nullability.Create(parameter))} {parameter.Name}{optional}";
    }

    private static string Accessors(PropertyInfo property)
    {
        var parts = new List<string>();
        if (property.GetMethod?.IsPublic == true) parts.Add("get;");
        if (property.SetMethod?.IsPublic == true)
        {
            var init = property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
            parts.Add(init ? "init;" : "set;");
        }
        return string.Join(' ', parts);
    }

    private static string FormatConstant(FieldInfo field) => field.IsLiteral ? " = " + FormatValue(field.GetRawConstantValue()) : field.IsStatic ? " [static]" : string.Empty;

    private static string Required(PropertyInfo property) => property.CustomAttributes.Any(static attribute =>
        attribute.AttributeType == typeof(System.Runtime.CompilerServices.RequiredMemberAttribute)) ? " [required]" : string.Empty;

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool flag => flag ? "true" : "false",
        Enum item => Convert.ToInt64(item, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
    };

    private static string FormatType(Type type, NullabilityInfo? nullability = null)
    {
        if (type.IsArray) return FormatType(type.GetElementType()!, nullability?.ElementType) + "[]" + NullableSuffix(type, nullability);
        if (type.IsGenericParameter) return type.Name;
        if (!type.IsGenericType) return Alias(type) + NullableSuffix(type, nullability);
        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(Nullable<>)) return FormatType(type.GetGenericArguments()[0], nullability?.GenericTypeArguments.FirstOrDefault()) + "?";
        var fullName = definition.FullName!;
        var name = fullName[..fullName.IndexOf('`')].Replace('+', '.');
        var arguments = type.GetGenericArguments();
        var nullableArguments = nullability?.GenericTypeArguments;
        var formatted = arguments.Select((argument, index) => FormatType(argument,
            nullableArguments is not null && index < nullableArguments.Length ? nullableArguments[index] : null));
        return $"{name}<{string.Join(", ", formatted)}>" + NullableSuffix(type, nullability);
    }

    private static string NullableSuffix(Type type, NullabilityInfo? nullability) =>
        !type.IsValueType && nullability?.ReadState == NullabilityState.Nullable ? "?" : string.Empty;

    private static string Alias(Type type) => type == typeof(void) ? "void" : type == typeof(bool) ? "bool" : type == typeof(byte) ? "byte" :
        type == typeof(int) ? "int" : type == typeof(long) ? "long" : type == typeof(string) ? "string" : type == typeof(object) ? "object" :
        (type.FullName ?? type.Name).Replace('+', '.');
}
