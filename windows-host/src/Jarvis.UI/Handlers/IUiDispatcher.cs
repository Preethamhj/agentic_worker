namespace Jarvis.UI;

/// <summary>Marshals asynchronous UI work onto the UI dispatcher.</summary>
public interface IUiDispatcher
{
    /// <summary>Runs a task-producing callback on the UI thread.</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> callback, CancellationToken cancellationToken = default);
}
