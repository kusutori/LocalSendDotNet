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

## Roadmap boundaries

The UI-ready v2 send/receive core is implemented. Browser sharing, IPv6 discovery and subnet scanning are later v2 extensions rather than UI blockers. Protocol v1 is legacy-only, transfer history belongs to the host application, and WebRTC work waits for a stable official v3 design.

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

Windows Native AOT publishing and optional MSIX packaging are documented in
[docs/packaging.md](docs/packaging.md).

The tag-gated nuget.org release process for `LocalSendDotNet.Core` is documented
in [docs/nuget-publishing.md](docs/nuget-publishing.md).

`samples/LocalSendDotNet.Sample` references the generated NuGet package rather than the source project. It is built in CI after packing to verify the consumer experience.

Official-client evidence and the remaining manual scenarios are tracked in [docs/interop-matrix.md](docs/interop-matrix.md).

The library requires the .NET 10 and ASP.NET Core 10 shared frameworks for framework-dependent deployments. Self-contained applications include these dependencies automatically.

## License

LocalSendDotNet is licensed under [Apache-2.0](LICENSE). Attribution and the
relationship to the separate official LocalSend project are documented in
[NOTICE](NOTICE).
