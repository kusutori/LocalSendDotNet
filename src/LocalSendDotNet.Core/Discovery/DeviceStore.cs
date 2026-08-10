using System.Collections.Concurrent;

namespace LocalSendDotNet;

internal sealed class DeviceStore(BroadcastHub<DeviceChange> changes)
{
    private readonly ConcurrentDictionary<string, LocalSendDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _persistent = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LocalSendDevice> Snapshot() => _devices.Values.OrderBy(static d => d.Alias, StringComparer.OrdinalIgnoreCase).ToArray();

    public LocalSendDevice Upsert(LocalSendDevice candidate, bool persistent = false)
    {
        if (persistent)
            _persistent[candidate.Fingerprint] = 0;
        while (true)
        {
            if (!_devices.TryGetValue(candidate.Fingerprint, out var existing))
            {
                if (!_devices.TryAdd(candidate.Fingerprint, candidate))
                    continue;
                changes.Publish(new DeviceChange(DeviceChangeKind.Added, candidate));
                return candidate;
            }
            var updated = existing with
            {
                Alias = candidate.Alias,
                ProtocolVersion = candidate.ProtocolVersion,
                DeviceModel = candidate.DeviceModel,
                DeviceType = candidate.DeviceType,
                SupportsDownload = candidate.SupportsDownload,
                Endpoints = existing.Endpoints.Concat(candidate.Endpoints).Distinct().ToArray(),
                LastSeen = candidate.LastSeen
            };
            if (!_devices.TryUpdate(candidate.Fingerprint, updated, existing))
                continue;
            changes.Publish(new DeviceChange(DeviceChangeKind.Updated, updated));
            return updated;
        }
    }

    public bool Remove(string fingerprint)
    {
        _persistent.TryRemove(fingerprint, out _);
        if (!_devices.TryRemove(fingerprint, out var removed))
            return false;
        changes.Publish(new DeviceChange(DeviceChangeKind.Removed, removed));
        return true;
    }

    public int RemoveExpired(DateTimeOffset cutoff)
    {
        var removed = 0;
        foreach (var pair in _devices)
        {
            if (_persistent.ContainsKey(pair.Key) || pair.Value.LastSeen >= cutoff || !_devices.TryRemove(pair.Key, out var device))
                continue;
            changes.Publish(new DeviceChange(DeviceChangeKind.Removed, device));
            removed++;
        }
        return removed;
    }
}
