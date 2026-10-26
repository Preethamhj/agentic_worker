using System.Text;
using System.Text.Json;

namespace Jarvis.Eval;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "run")
        {
            var options = ParseRunOptions(args[1..]);
            new EvalFileRunner().RunAsync(options).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 3
            && args[0] == "--write-catalog"
            && int.TryParse(args[1], out var seed))
        {
            var path = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(EvalTaskCatalog.Generate(seed), EvalTaskCatalog.JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Console.WriteLine("Usage: Jarvis.Eval --write-catalog <seed> <path>");
    }

    private static EvalFileRunOptions ParseRunOptions(string[] args)
    {
        var seed = EvalTaskCatalog.DefaultSeed;
        var reps = 1;
        var output = Path.Combine(Environment.CurrentDirectory, "eval-output");
        string? tool = null;
        var smoke = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--seed": seed = int.Parse(args[++index]); break;
                case "--reps": reps = int.Parse(args[++index]); break;
                case "--tools": tool = args[++index]; break;
                case "--out": output = Path.GetFullPath(args[++index]); break;
                case "--smoke": smoke = true; break;
            }
        }
        return new EvalFileRunOptions { Seed = seed, Repetitions = reps, ToolFilter = tool, OutputDirectory = output, Smoke = smoke };
    }
}
