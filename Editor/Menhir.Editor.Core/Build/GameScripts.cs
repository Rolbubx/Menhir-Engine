using Menhir.Editor.Projects;
using Menhir.Editor.Scripting;
using Menhir.Translator.CodeGen;
using Menhir.Translator.TypeSystem;

namespace Menhir.Editor.Build;

/// <summary>
/// The scripts of a built game: the scripting API built against Menhir.CoreLib (Menhir.Engine.Native),
/// the project's scripts compiled against it, then everything translated to C by IL2C, with the
/// table the player runs the scripts with (mh_scripts.c).
/// </summary>
public static class GameScripts
{
    public const string ScriptBaseType = "MenhirEngine.Script";

    /// <summary>Writes the scripts' C to <paramref name="cFile"/>; returns false (writing nothing) when the project has no scripts.</summary>
    /// <param name="engineDirectory">The repository's Engine/ folder (Translator/ is next to it).</param>
    /// <exception cref="BuildException">The API or the scripts do not compile, or IL2C cannot translate them.</exception>
    public static bool Translate(Project project, string engineDirectory, string cFile, Action<string> log)
    {
        if (!ScriptCompiler.HasScripts(project))
            return false;

        log("Compiling the scripting API for the game");
        string api = Path.Combine(project.CacheDirectory, "Scripts", "Native");
        string apiProject = Path.Combine(engineDirectory, "Scripting", "Menhir.Engine.Native", "Menhir.Engine.Native.csproj");
        if (!File.Exists(apiProject))
            throw new BuildException($"{apiProject} was not found.");
        (int exitCode, string output) = RunDotnet(["build", apiProject, "-c", "Release", "-o", api, "-nologo", "-v", "q",
            "-clp:NoSummary", "-p:UseSharedCompilation=false"], engineDirectory);
        string coreLib = Path.Combine(api, "Menhir.CoreLib.dll"), engine = Path.Combine(api, "Menhir.Engine.dll");
        if (exitCode != 0 || !File.Exists(coreLib) || !File.Exists(engine))
            throw new BuildException("The scripting API could not be compiled:\n" + output.Trim());

        log("Compiling the scripts for the game");
        ScriptCompileResult scripts = ScriptCompiler.CompileForGame(project, coreLib, engine);
        if (!scripts.Success)
            throw new BuildException("The scripts do not compile for the game:\n" +
                                     string.Join("\n", scripts.Errors.Select(e => e.ToString())));

        log("Translating the scripts to C (IL2C)");
        try
        {
            using var context = new TypeSystemContext();
            context.Load(coreLib, [engine, scripts.AssemblyPath!]);
            string c = new Il2cTranslator(context) { ScriptBaseType = ScriptBaseType }.Translate(null);
            File.WriteAllText(cFile, c);
        }
        catch (TranslationException e)
        {
            throw new BuildException("IL2C cannot translate the scripts: " + e.Message);
        }
        return true;
    }

    private static (int ExitCode, string Output) RunDotnet(string[] arguments, string workingDirectory)
    {
        try
        {
            return ScriptCompiler.RunDotnet(arguments, workingDirectory);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new BuildException($"The .NET SDK (dotnet) is needed to build a game with scripts: {e.Message}");
        }
    }
}
