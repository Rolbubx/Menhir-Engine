using System.Diagnostics;
using Menhir.Translator.CodeGen;
using Menhir.Translator.TypeSystem;

[assembly: AssemblyFixture(typeof(Menhir.Translator.Tests.NativeProgramFixture))]

namespace Menhir.Translator.Tests;

/// <summary>
/// Translates TestPrograms.Menhir.dll to C and builds it once for all tests, and runs programs
/// both natively and on .NET (TestPrograms.Dotnet, same sources). One instance is shared by the
/// assembly's test classes, so they do not build the same executable at the same time.
/// </summary>
public sealed class NativeProgramFixture
{
    public NativeProgramFixture()
    {
        // .../Translator/Menhir.Translator.Tests/bin/<Configuration>/net10.0/
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        string framework = output.Name;
        string configuration = output.Parent!.Name;
        TranslatorDirectory = output.Parent.Parent!.Parent!.Parent!.FullName;

        string programs = Path.Combine(TranslatorDirectory, "TestPrograms");
        string menhirOutput = Path.Combine(programs, "Menhir", "bin", configuration, framework);
        DotnetProgram = Path.Combine(programs, "Dotnet", "bin", configuration, framework, "TestPrograms.Dotnet.dll");

        string work = Path.Combine(AppContext.BaseDirectory, "il2c-test");
        Directory.CreateDirectory(work);
        CFile = Path.Combine(work, "TestPrograms.c");
        Executable = Path.Combine(work, OperatingSystem.IsWindows() ? "TestPrograms.exe" : "TestPrograms");

        using var context = new TypeSystemContext();
        context.Load(Path.Combine(menhirOutput, "Menhir.CoreLib.dll"), [Path.Combine(menhirOutput, "TestPrograms.Menhir.dll")]);
        File.WriteAllText(CFile, new Il2cTranslator(context).Translate("Tests.Runner.Main"));

        string engine = CBuilder.FindEngineDirectory(TranslatorDirectory)
                        ?? throw new InvalidOperationException("Engine/ not found next to Translator/.");
        new CBuilder(engine).Build(CFile, Executable, Path.Combine(work, "cache"));
    }

    public string TranslatorDirectory { get; }
    public string DotnetProgram { get; }
    public string CFile { get; }
    public string Executable { get; }

    public (string Output, string Errors, int ExitCode) RunNative(string program) => Run(Executable, [program]);

    public (string Output, string Errors, int ExitCode) RunDotnet(string program) => Run("dotnet", [DotnetProgram, program]);

    private static (string Output, string Errors, int ExitCode) Run(string file, string[] arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            throw new TimeoutException($"{file} {string.Join(' ', arguments)} did not finish.");
        }
        return (stdout.Result, stderr.Result, process.ExitCode);
    }
}
