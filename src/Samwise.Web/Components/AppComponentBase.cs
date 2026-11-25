using Microsoft.AspNetCore.Components;

namespace Samwise.Web.Components;

public abstract class AppComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentLoad;
    private Task? refresh;
    private bool disposed;

    protected AppComponentBase() => ComponentToken = lifetime.Token;

    protected CancellationToken ComponentToken { get; }
    protected bool IsLoading { get; private set; } = true;
    protected string? LoadError { get; private set; }

    [Inject] private ILogger<AppComponentBase> ComponentLogger { get; set; } = default!;

    protected async Task ExecuteLoadAsync<T>(Func<CancellationToken, Task<T>> read, Action<T> apply)
    {
        if (disposed)
        {
            return;
        }

        using var load = CancellationTokenSource.CreateLinkedTokenSource(ComponentToken);
        var previous = currentLoad;
        currentLoad = load;
        IsLoading = true;
        LoadError = null;
        try
        {
            StateHasChanged();
            if (previous is not null)
            {
                await previous.CancelAsync();
            }

            load.Token.ThrowIfCancellationRequested();
            var result = await read(load.Token);
            if (!load.IsCancellationRequested && currentLoad == load)
            {
                apply(result);
            }
        }
        catch (Exception exception) when (load.IsCancellationRequested || currentLoad != load)
        {
            if (exception is not OperationCanceledException)
            {
                ComponentLogger.LogDebug(exception, "An abandoned read failed in {Component}", GetType().Name);
            }
        }
        catch (Exception exception)
        {
            ComponentLogger.LogError(exception, "Loading failed in {Component}", GetType().Name);
            LoadError = "Data could not be loaded. Please try again.";
        }
        finally
        {
            if (currentLoad == load)
            {
                currentLoad = null;
                IsLoading = false;
                if (!disposed)
                {
                    StateHasChanged();
                }
            }
        }
    }

    protected void StartRefreshing(TimeSpan interval, Func<CancellationToken, Task> tick)
    {
        if (!disposed)
        {
            refresh = RefreshPeriodicallyAsync(interval, tick);
        }
    }

    private async Task RefreshPeriodicallyAsync(TimeSpan interval, Func<CancellationToken, Task> tick)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ComponentToken))
            {
                try
                {
                    await InvokeAsync(() => ComponentToken.IsCancellationRequested
                        ? Task.CompletedTask
                        : tick(ComponentToken));
                }
                catch (Exception exception) when (!ComponentToken.IsCancellationRequested)
                {
                    ComponentLogger.LogWarning(exception, "Refreshing {Component} failed", GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (ComponentToken.IsCancellationRequested)
        {
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            await lifetime.CancelAsync();
            if (refresh is not null)
            {
                await refresh;
            }
        }
        finally
        {
            lifetime.Dispose();
        }
    }
}
