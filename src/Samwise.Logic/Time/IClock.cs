namespace Samwise.Logic.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
