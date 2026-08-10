using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using LocalSendDotNet.Protocol.V2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalSendDotNet;

internal sealed record PrepareOutcome(HttpStatusCode StatusCode, PrepareUploadResponseDto? Response = null, string? Message = null);

internal sealed class V2Server(
    LocalSendOptions options,
    DeviceIdentity identity,
    Func<DeviceInfoDto> localInfo,
    Func<DeviceInfoDto, IPAddress, string?, Task> onRegister,
    Func<PrepareUploadRequestDto, IPAddress, string?, CancellationToken, Task<PrepareOutcome>> onPrepare,
    Func<string, string, string, IPAddress, Stream, long?, CancellationToken, Task<HttpStatusCode>> onUpload,
    Func<string, IPAddress, CancellationToken, Task<bool>> onCancel,
    ILogger logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<IPAddress, (int Count, DateTimeOffset LockedUntil)> _pinAttempts = new();
    private WebApplication? _application;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(V2Server).Assembly.FullName,
            EnvironmentName = Environments.Production
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = null;
            server.ListenAnyIP(options.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                if (options.EnableHttps)
                {
                    listen.UseHttps(https =>
                    {
                        https.ServerCertificate = identity.Certificate;
                        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                        https.ClientCertificateValidation = static (_, _, _) => true;
                    });
                }
            });
        });

        var app = builder.Build();
        app.MapPost(V2Constants.BasePath + "/register", RegisterAsync);
        app.MapGet(V2Constants.BasePath + "/info", InfoAsync);
        app.MapPost(V2Constants.BasePath + "/prepare-upload", PrepareUploadAsync);
        app.MapPost(V2Constants.BasePath + "/upload", UploadAsync);
        app.MapPost(V2Constants.BasePath + "/cancel", CancelAsync);
        _application = app;
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RegisterAsync(HttpContext context)
    {
        if (!ConfigureRequestLimit(context, 64 * 1024))
            return;
        var payload = await ReadJsonAsync<DeviceInfoDto>(context).ConfigureAwait(false);
        if (payload is null)
            return;
        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var certificateFingerprint = certificate is null ? null : DeviceIdentityStore.Fingerprint(certificate);
        if (options.EnableHttps && (certificate is null || !DeviceIdentityStore.ValidatePeerCertificate(certificate, payload.Fingerprint)))
        {
            logger.LogWarning("Ignoring spoofed register fingerprint from {Address}", context.Connection.RemoteIpAddress);
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }
        else
        {
            await onRegister(payload, RemoteAddress(context), certificateFingerprint).ConfigureAwait(false);
        }

        var info = localInfo();
        await context.Response.WriteAsJsonAsync(new RegisterResponseDto
        {
            Alias = info.Alias,
            Version = info.Version,
            DeviceModel = info.DeviceModel,
            DeviceType = info.DeviceType,
            Fingerprint = info.Fingerprint,
            Download = false
        }, V2Json.Options, context.RequestAborted).ConfigureAwait(false);
    }

    private Task InfoAsync(HttpContext context)
    {
        var info = localInfo();
        return context.Response.WriteAsJsonAsync(new RegisterResponseDto
        {
            Alias = info.Alias,
            Version = info.Version,
            DeviceModel = info.DeviceModel,
            DeviceType = info.DeviceType,
            Fingerprint = info.Fingerprint,
            Download = false
        }, V2Json.Options, context.RequestAborted);
    }

    private async Task PrepareUploadAsync(HttpContext context)
    {
        var remote = RemoteAddress(context);
        if (!ConfigureRequestLimit(context, options.MaxPrepareRequestBytes))
            return;
        var payload = await ReadJsonAsync<PrepareUploadRequestDto>(context).ConfigureAwait(false);
        if (payload is null)
            return;
        if (payload.Files.Count == 0)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "No files provided").ConfigureAwait(false);
            return;
        }
        if (payload.Info.Port is < 1 or > ushort.MaxValue || string.IsNullOrWhiteSpace(payload.Info.Alias) ||
            !IsFingerprint(payload.Info.Fingerprint) ||
            (!StringComparer.OrdinalIgnoreCase.Equals(payload.Info.Protocol, "http") && !StringComparer.OrdinalIgnoreCase.Equals(payload.Info.Protocol, "https")))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid sender metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Any(static pair => pair.Value.Size < 0 || string.IsNullOrWhiteSpace(pair.Value.FileName) ||
            string.IsNullOrWhiteSpace(pair.Value.FileType) || !StringComparer.Ordinal.Equals(pair.Key, pair.Value.Id)))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid file metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Count > options.MaxIncomingItemsPerTransfer || ExceedsTransferLimit(payload.Files.Values, options.MaxIncomingTransferBytes))
        {
            await WriteErrorAsync(context, HttpStatusCode.RequestEntityTooLarge, "Incoming transfer exceeds configured limits").ConfigureAwait(false);
            return;
        }

        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var certFingerprint = certificate is null ? null : DeviceIdentityStore.Fingerprint(certificate);
        if (options.EnableHttps && (certificate is null || !DeviceIdentityStore.ValidatePeerCertificate(certificate, payload.Info.Fingerprint)))
        {
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }
        if (!CheckPin(context, remote))
            return;

        var outcome = await onPrepare(payload, remote, certFingerprint, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = (int)outcome.StatusCode;
        if (outcome.Response is not null)
            await context.Response.WriteAsJsonAsync(outcome.Response, V2Json.Options, context.RequestAborted).ConfigureAwait(false);
        else if (outcome.Message is not null)
            await context.Response.WriteAsJsonAsync(new ErrorResponseDto { Message = outcome.Message }, V2Json.Options, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task UploadAsync(HttpContext context)
    {
        var query = context.Request.Query;
        if (!query.TryGetValue("sessionId", out var sessionId) || !query.TryGetValue("fileId", out var fileId) || !query.TryGetValue("token", out var token))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Missing upload query parameters").ConfigureAwait(false);
            return;
        }
        var status = await onUpload(sessionId.ToString(), fileId.ToString(), token.ToString(), RemoteAddress(context), context.Request.Body, context.Request.ContentLength, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = (int)status;
    }

    private async Task CancelAsync(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("sessionId", out var sessionId))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Missing sessionId").ConfigureAwait(false);
            return;
        }
        context.Response.StatusCode = await onCancel(sessionId.ToString(), RemoteAddress(context), context.RequestAborted).ConfigureAwait(false)
            ? StatusCodes.Status200OK
            : StatusCodes.Status404NotFound;
    }

    private bool CheckPin(HttpContext context, IPAddress remote)
    {
        if (options.ReceivePin is null)
            return true;
        var now = DateTimeOffset.UtcNow;
        var attempts = _pinAttempts.GetOrAdd(remote, static _ => (0, DateTimeOffset.MinValue));
        if (attempts.Count >= 3 && attempts.LockedUntil > now)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return false;
        }
        if (attempts.Count >= 3)
            attempts = (0, DateTimeOffset.MinValue);
        var supplied = context.Request.Query["pin"].ToString();
        if (StringComparer.Ordinal.Equals(supplied, options.ReceivePin))
        {
            _pinAttempts.TryRemove(remote, out _);
            return true;
        }
        if (supplied.Length > 0)
        {
            var count = attempts.Count + 1;
            _pinAttempts[remote] = (count, count >= 3 ? now + options.PinLockoutDuration : DateTimeOffset.MinValue);
        }
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

    private static bool ExceedsTransferLimit(IEnumerable<FileDto> files, long limit)
    {
        long total = 0;
        foreach (var file in files)
        {
            if (file.Size > limit - total)
                return true;
            total += file.Size;
        }
        return false;
    }

    private static bool IsFingerprint(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            return await context.Request.ReadFromJsonAsync<T>(V2Json.Options, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await WriteErrorAsync(context, HttpStatusCode.RequestEntityTooLarge, "JSON request exceeds the configured limit").ConfigureAwait(false);
            return default;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or Microsoft.AspNetCore.Http.BadHttpRequestException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid JSON request").ConfigureAwait(false);
            return default;
        }
    }

    private static bool ConfigureRequestLimit(HttpContext context, long limit)
    {
        if (context.Request.ContentLength > limit)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return false;
        }
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = limit;
        return true;
    }

    private static Task WriteErrorAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int)status;
        return context.Response.WriteAsJsonAsync(new ErrorResponseDto { Message = message }, V2Json.Options, context.RequestAborted);
    }

    private static IPAddress RemoteAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress ?? IPAddress.None;
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is null)
            return;
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
        _application = null;
    }
}
