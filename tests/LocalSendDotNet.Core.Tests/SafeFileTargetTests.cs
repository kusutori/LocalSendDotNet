namespace LocalSendDotNet.Core.Tests;

public sealed class SafeFileTargetTests
{
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/../../secret.txt")]
    [InlineData("C:\\secret.txt")]
    [InlineData("/etc/passwd")]
    public void RejectsUnsafePaths(string value)
    {
        var root = TestDirectory.Create();
        try { Assert.Throws<LocalSendException>(() => SafeFileTarget.ResolveUnique(root, value)); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CreatesUniqueNameForCollision()
    {
        var root = TestDirectory.Create();
        try
        {
            File.WriteAllText(Path.Combine(root, "hello.txt"), "existing");
            Assert.EndsWith("hello (1).txt", SafeFileTarget.ResolveUnique(root, "hello.txt"), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReservesUniqueNamesBeforeFilesExist()
    {
        var root = TestDirectory.Create();
        try
        {
            var reserved = new HashSet<string>();
            var first = SafeFileTarget.ResolveUnique(root, "hello.txt", reserved);
            var second = SafeFileTarget.ResolveUnique(root, "hello.txt", reserved);
            Assert.NotEqual(first, second);
            Assert.EndsWith("hello (1).txt", second, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
