namespace LocalSendDotNet;

/// <summary>Base exception for LocalSend-specific failures.</summary>
public class LocalSendException : Exception
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public LocalSendException(string message) : base(message) { }
    /// <summary>Creates an exception with a diagnostic message and underlying cause.</summary>
    public LocalSendException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Indicates that the peer requires a PIN or rejected the supplied PIN.</summary>
public sealed class PinRequiredException : LocalSendException
{
    /// <summary>Creates a PIN response exception.</summary>
    /// <param name="invalidPin">Whether a supplied PIN was rejected.</param>
    public PinRequiredException(bool invalidPin) : base(invalidPin ? "The remote device rejected the PIN." : "The remote device requires a PIN.") => InvalidPin = invalidPin;
    /// <summary>Gets whether the caller supplied an incorrect PIN.</summary>
    public bool InvalidPin { get; }
}

/// <summary>Indicates that the peer temporarily rate-limited PIN attempts.</summary>
public sealed class PinRateLimitedException : LocalSendException
{
    /// <summary>Creates a PIN rate-limit exception.</summary>
    public PinRateLimitedException() : base("The remote device has rate-limited PIN attempts.") { }
}

/// <summary>Indicates that a peer certificate or advertised fingerprint failed validation.</summary>
public sealed class PeerIdentityException : LocalSendException
{
    /// <summary>Creates a peer identity exception.</summary>
    public PeerIdentityException(string message) : base(message) { }
    /// <summary>Creates a peer identity exception with its underlying cause.</summary>
    public PeerIdentityException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Indicates that a receiver has no free transfer capacity.</summary>
public sealed class PeerBusyException : LocalSendException
{
    /// <summary>Creates a peer busy exception.</summary>
    public PeerBusyException() : base("The remote device is handling the maximum number of transfers.") { }
}

/// <summary>Indicates that a receiver declined an outgoing offer.</summary>
public sealed class TransferDeclinedException : LocalSendException
{
    /// <summary>Creates a transfer declined exception.</summary>
    public TransferDeclinedException() : base("The remote device declined the transfer.") { }
}

/// <summary>Indicates that the persistent local certificate identity is incomplete or corrupt.</summary>
public sealed class IdentityLoadException : LocalSendException
{
    /// <summary>Creates an identity load exception.</summary>
    public IdentityLoadException(string message) : base(message) { }
    /// <summary>Creates an identity load exception with its underlying cause.</summary>
    public IdentityLoadException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Indicates that the configured TCP listening port is unavailable.</summary>
public sealed class PortUnavailableException : LocalSendException
{
    /// <summary>Creates a port availability exception.</summary>
    /// <param name="port">The unavailable TCP port.</param>
    /// <param name="innerException">The underlying bind failure.</param>
    public PortUnavailableException(int port, Exception innerException)
        : base($"TCP port {port} is unavailable. Choose another LocalSendOptions.Port or stop the process using it.", innerException) => Port = port;

    /// <summary>Gets the unavailable port.</summary>
    public int Port { get; }
}

/// <summary>Indicates that IPv4 multicast discovery could not bind or join an interface.</summary>
public sealed class DiscoveryUnavailableException : LocalSendException
{
    /// <summary>Creates a discovery availability exception.</summary>
    public DiscoveryUnavailableException(string message) : base(message) { }
    /// <summary>Creates a discovery availability exception with its underlying cause.</summary>
    public DiscoveryUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
