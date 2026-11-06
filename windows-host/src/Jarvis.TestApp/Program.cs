namespace Jarvis.TestApp;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        if (args.Length != 2
            || !string.Equals(args[0], "--exit-after-ms", StringComparison.Ordinal)
            || !int.TryParse(args[1], out var durationMilliseconds)
            || durationMilliseconds < 0)
        {
            return 2;
        }

        Thread.Sleep(durationMilliseconds);
        return 0;
    }
}
