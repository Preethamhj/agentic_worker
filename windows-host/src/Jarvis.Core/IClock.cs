namespace Jarvis.Core;

/// <summary>
/// Abstraction for system clock to support deterministic time in testing.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Default system clock using DateTimeOffset.UtcNow.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
