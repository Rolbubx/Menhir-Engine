using Menhir.Translator.CodeGen;
using Menhir.Translator.TypeSystem;

namespace Menhir.Translator;

/// <summary>
/// il2c &lt;program.dll&gt; [--corlib Menhir.CoreLib.dll] [--entry Namespace.Type.Main] [-o out]
///      [--build [--cc gcc]] [--engine path/to/Engine]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var inputs = new List<string>();
        string? coreLib = null, entry = null, output = null, compiler = null, engine = null;
        bool build = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--corlib": coreLib = args[++i]; break;
                case "--entry": entry = args[++i]; break;
                case "-o": output = args[++i]; break;
                case "--build": build = true; break;
                case "--cc": compiler = args[++i]; break;
                case "--engine": engine = args[++i]; break;
                case "-h" or "--help":
                    PrintUsage();
                    return 0;
                default: inputs.Add(args[i]); break;
            }
        }
        if (inputs.Count == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            coreLib ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[0]))!, "Menhir.CoreLib.dll");
            output ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[0]))!, "il2c");
            Directory.CreateDirectory(output);
            string name = Path.GetFileNameWithoutExtension(inputs[0]);
            string cFile = Path.Combine(output, name + ".c");

            using var context = new TypeSystemContext();
            context.Load(coreLib, inputs);
            string code = new Il2cTranslator(context).Translate(entry);
            File.WriteAllText(cFile, code);
            Console.WriteLine($"Wrote {cFile}");

            if (build)
            {
                engine ??= CBuilder.FindEngineDirectory(AppContext.BaseDirectory) ?? CBuilder.FindEngineDirectory(Environment.CurrentDirectory)
                    ?? throw new TranslationException("Engine/ not found; pass --engine <path>.");
                string executable = Path.Combine(output, OperatingSystem.IsWindows() ? name + ".exe" : name);
                new CBuilder(engine, compiler ?? "cc").Build(cFile, executable, Path.Combine(output, "cache"));
                Console.WriteLine($"Built {executable}");
            }
            return 0;
        }
        catch (TranslationException e)
        {
            Console.Error.WriteLine("il2c: " + e.Message);
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage: il2c <program.dll> [--corlib Menhir.CoreLib.dll] [--entry Namespace.Type.Main] [-o out]");
        Console.WriteLine("            [--build [--cc gcc]] [--engine path/to/Engine]");
        Console.WriteLine("Translates a .NET assembly built against Menhir.CoreLib into C; --build compiles it with");
        Console.WriteLine("Engine/Runtime and the Boehm collector into an executable.");
    }
}
