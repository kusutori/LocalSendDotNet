# LocalSendDotNet

An experimental, UI-independent .NET 10 implementation of the LocalSend v2.2 protocol.

## Current capabilities

- IPv4 multicast discovery and two-way registration
- HTTP or HTTPS with persisted RSA identity, mutual TLS and certificate fingerprint pinning
- File and text send/receive, partial acceptance, progress and cancellation
- Optional receiver PIN protection
- Safe streaming writes with path traversal protection and atomic publication
- Session timeouts, bounded concurrency, SHA-256 verification and explicit transfer cancellation
- Node lifecycle events, device expiry, trusted manual endpoints and stream-backed send items
- Folder enumeration with relative paths and common MIME type inference
- Diagnostic CLI for discovery and interoperability testing

Browser sharing, WebRTC, protocol v1, IPv6 discovery, subnet scanning, history and UI are intentionally out of scope for the first preview.

## Build

```powershell
dotnet restore LocalSendDotNet.slnx
dotnet build LocalSendDotNet.slnx
dotnet test LocalSendDotNet.slnx
```

## CLI

```powershell
dotnet run --project src/LocalSendDotNet.Cli -- discover
dotnet run --project src/LocalSendDotNet.Cli -- listen --auto-accept
dotnet run --project src/LocalSendDotNet.Cli -- send --target "Device Alias" path/to/file
dotnet run --project src/LocalSendDotNet.Cli -- send-dir --target "Device Alias" --sha256 path/to/folder
dotnet run --project src/LocalSendDotNet.Cli -- send-text --target "Device Alias" "hello"
```

## Core API

```csharp
await using var node = new LocalSendNode(new LocalSendOptions
{
    Alias = "My app",
    DataDirectory = appDataDirectory,
    DownloadDirectory = downloadsDirectory
});

await node.StartAsync(cancellationToken);
await foreach (var request in node.WatchIncomingTransfersAsync(cancellationToken))
{
    _ = node.AcceptAsync(request.RequestId, progress: progress, cancellationToken: cancellationToken);
}
```

See [docs/core-api.md](docs/core-api.md) for lifecycle, discovery, send/receive, cancellation and manual-address guidance.

The library requires the .NET 10 and ASP.NET Core 10 shared frameworks for framework-dependent deployments. Self-contained applications include these dependencies automatically.

## License

Apache-2.0. LocalSend is a separate Apache-2.0 project; this repository is an independent protocol implementation.
