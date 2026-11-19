using System.Collections.Concurrent;

namespace FRExplorer;

internal static class StaTask
{
    private static readonly BlockingCollection<Action> WorkQueue = new();
    private static readonly Thread Worker = StartWorker();

    internal static Task<T> Run<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkQueue.Add(
            () =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(operation());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            CancellationToken.None);
        return completion.Task;
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "FRExplorer Shell worker",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void ProcessQueue()
    {
        foreach (Action operation in WorkQueue.GetConsumingEnumerable())
        {
            operation();
        }
    }
}
