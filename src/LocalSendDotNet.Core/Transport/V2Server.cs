using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using LocalSendDotNet.Protocol.V2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    private readonly ConcurrentDictionary<IPAddress, int> _pinAttempts = new();
    private WebApplication? _application;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(V2Server).Assembly.FullName,
            EnvironmentName = Environments.Production
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.ListenAnyIP(options.Port, listen =>
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
        }));

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
        var payload = await ReadJsonAsync<DeviceInfoDto>(context).ConfigureAwait(false);
        if (payload is null)
            return;
        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var certificateFingerprint = certificate is null ? null : DeviceIdentityStore.Fingerprint(certificate);
        if (options.EnableHttps && (certificate is null || !DeviceIdentityStore.ValidatePeerCertificate(certificate, payload.Fingerprint)))
        {
            logger.LogWarning("Ignoring spoofed register fingerprint from {Address}", context.Connection.RemoteIpAddress);
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
        if (!CheckPin(context, remote))
            return;

        var payload = await ReadJsonAsync<PrepareUploadRequestDto>(context).ConfigureAwait(false);
        if (payload is null)
            return;
        if (payload.Files.Count == 0)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "No files provided").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Any(static pair => pair.Value.Size < 0 || string.IsNullOrWhiteSpace(pair.Value.FileName) || !StringComparer.Ordinal.Equals(pair.Key, pair.Value.Id)))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid file metadata").ConfigureAwait(false);
            return;
        }

        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var certFingerprint = certificate is null ? null : DeviceIdentityStore.Fingerprint(certificate);
        if (options.EnableHttps && (certificate is null || !DeviceIdentityStore.ValidatePeerCertificate(certificate, payload.Info.Fingerprint)))
        {
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }

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
        var attempts = _pinAttempts.GetOrAdd(remote, 0);
        if (attempts >= 3)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return false;
        }
        var supplied = context.Request.Query["pin"].ToString();
        if (StringComparer.Ordinal.Equals(supplied, options.ReceivePin))
        {
            _pinAttempts.TryRemove(remote, out _);
            return true;
        }
        if (supplied.Length > 0)
            _pinAttempts[remote] = attempts + 1;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            return await context.Request.ReadFromJsonAsync<T>(V2Json.Options, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or Microsoft.AspNetCore.Http.BadHttpRequestException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid JSON request").ConfigureAwait(false);
            return default;
        }
    }

    private static Task WriteErrorAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int)status;
        return context.Response.WriteAsJsonAsync(new ErrorResponseDto { Message = message }, V2Json.Options, context.RequestAborted);
    }

    private static IPAddress RemoteAddress(HttpContext context) => context.Connection.RemoteIpAddress ?? IPAddress.None;

    public async ValueTask DisposeAsync()
    {
        if (_application is null)
            return;
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
        _application = null;
    }
}
