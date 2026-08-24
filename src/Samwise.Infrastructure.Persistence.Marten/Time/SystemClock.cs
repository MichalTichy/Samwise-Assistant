using Samwise.Logic.Time;

namespace Samwise.Infrastructure.Persistence.Marten.Time;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
