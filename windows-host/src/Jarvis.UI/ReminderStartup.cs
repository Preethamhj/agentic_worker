namespace Jarvis.UI;

/// <summary>Routes scheduled reminder invocations without starting the main UI session.</summary>
public static class ReminderStartup
{
    /// <summary>Dispatches reminder arguments if this invocation is a reminder.</summary>
    public static bool TryDispatch(string[] args, Func<string[], int> dispatch, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(dispatch);
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], "--jarvis-reminder", StringComparison.Ordinal))
            return false;
        exitCode = dispatch(args);
        return true;
    }
}
