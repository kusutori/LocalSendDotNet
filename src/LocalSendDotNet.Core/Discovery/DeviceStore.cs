using System.Collections.Concurrent;

namespace LocalSendDotNet;

internal sealed class DeviceStore(BroadcastHub<DeviceChange> changes)
{
    private readonly ConcurrentDictionary<string, LocalSendDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LocalSendDevice> Snapshot() => _devices.Values.OrderBy(static d => d.Alias, StringComparer.OrdinalIgnoreCase).ToArray();

    public LocalSendDevice Upsert(LocalSendDevice candidate)
    {
        var added = false;
        var result = _devices.AddOrUpdate(candidate.Fingerprint,
            _ => { added = true; return candidate; },
            (_, existing) => existing with
            {
                Alias = candidate.Alias,
                ProtocolVersion = candidate.ProtocolVersion,
                DeviceModel = candidate.DeviceModel,
                DeviceType = candidate.DeviceType,
                SupportsDownload = candidate.SupportsDownload,
                Endpoints = existing.Endpoints.Concat(candidate.Endpoints).Distinct().ToArray(),
                LastSeen = candidate.LastSeen
            });
        changes.Publish(new DeviceChange(added ? DeviceChangeKind.Added : DeviceChangeKind.Updated, result));
        return result;
    }
}
