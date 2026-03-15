using Mono.Cecil;

namespace LangAngo.Cecil.Weaver;

static class Program
{
    static int Main(string[] args)
    {
        string? input = null;
        string? output = null;
        string? namespacePrefix = null;
        string? classPattern = null;

        var dumpIl = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" when i + 1 < args.Length:
                    input = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--namespace" when i + 1 < args.Length:
                    namespacePrefix = args[++i];
                    break;
                case "--class-pattern" when i + 1 < args.Length:
                    classPattern = args[++i];
                    break;
                case "--dump-il":
                    dumpIl = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        if (string.IsNullOrEmpty(input))
        {
            Console.Error.WriteLine("Missing --input <path-to-dll>");
            PrintUsage();
            return 1;
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine("Input file not found: {0}", input);
            return 1;
        }

        output ??= input;

        try
        {
            var count = WeaverRunner.Run(input, output, namespacePrefix, classPattern);
            Console.WriteLine("Instrumented {0} method(s). Output: {1}", count, output);
            if (dumpIl && count > 0)
                DumpIl.DumpMethod(output, "LangAngo.TestApp.ComplexLogicController", "Handle");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Weaver error: {0}", ex.Message);
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage: LangAngo.Cecil.Weaver --input <dll> [--output <dll>] [--namespace <prefix>] [--class-pattern <pattern>]");
        Console.WriteLine("  --input         Path to target assembly (required).");
        Console.WriteLine("  --output        Path for instrumented assembly (default: overwrite input).");
        Console.WriteLine("  --namespace     Include only types in this namespace prefix (e.g. MyApp.Services).");
        Console.WriteLine("  --class-pattern Include only types matching pattern (e.g. *Controller, *Service).");
    }
}
