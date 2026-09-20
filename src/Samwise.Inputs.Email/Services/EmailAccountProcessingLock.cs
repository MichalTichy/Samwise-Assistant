using System.Collections.Concurrent;

namespace Samwise.Inputs.Email.Services;

internal interface IEmailAccountProcessingLock
{
    Task<IDisposable> AcquireAsync(string emailAccountId, CancellationToken cancellationToken);
}

internal sealed class EmailAccountProcessingLock : IEmailAccountProcessingLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(
        string emailAccountId,
        CancellationToken cancellationToken)
    {
        var accountLock = locks.GetOrAdd(emailAccountId, _ => new SemaphoreSlim(1, 1));
        await accountLock.WaitAsync(cancellationToken);
        return new LockLease(accountLock);
    }

    private sealed class LockLease(SemaphoreSlim accountLock) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            accountLock.Release();
        }
    }
}
