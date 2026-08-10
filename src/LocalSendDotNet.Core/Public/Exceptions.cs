namespace LocalSendDotNet;

public class LocalSendException : Exception
{
    public LocalSendException(string message) : base(message) { }
    public LocalSendException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class PinRequiredException : LocalSendException
{
    public PinRequiredException(bool invalidPin) : base(invalidPin ? "The remote device rejected the PIN." : "The remote device requires a PIN.") => InvalidPin = invalidPin;
    public bool InvalidPin { get; }
}

public sealed class PinRateLimitedException : LocalSendException
{
    public PinRateLimitedException() : base("The remote device has rate-limited PIN attempts.") { }
}

public sealed class PeerIdentityException : LocalSendException
{
    public PeerIdentityException(string message) : base(message) { }
}
