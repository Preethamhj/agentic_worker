using System.Text;
using System.Text.Json;

namespace Jarvis.Eval;

public static class Program
{
    public static void Main(string[] args)
    {
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
}
