using Menhir.Editor.Assets;

namespace Menhir.Editor.Scripting;

/// <summary>Project > Create > C Script: a new script in the simple syntax (menhir_simple.h), named after its file.</summary>
public static class ScriptTemplate
{
    public const string DefaultName = "NewScript";

    public static string Content(string name) => $$"""
        MH_CREATE {{name}}.c

        /* The script's variables, one copy per object. Numbers, booleans, vectors, colors and texts are
           fields the inspector shows (MH_RANGE(min, max), MH_HIDE and a tooltip go in a comment after them). */
        MH_VARS {
            float speed = 1;    // MH_RANGE(0, 10) Meters per second
        };

        /* Before the first update. */
        MH_START()
        {
        }

        /* Every frame. */
        MH_UPDATE()
        {
        }

        """;

    /// <summary>Project > Create > C++ Script: a class deriving from MenhirEngine (menhir.hpp), named after its file.</summary>
    public static string CppContent(string name) => $$"""
        class {{name}} : public MenhirEngine {
        public:
            // Public variables are fields: the inspector shows them (MH_RANGE(min, max), MH_HIDE and a
            // tooltip go in a comment after them).
            float speed = 1;    // MH_RANGE(0, 10) Meters per second

            // Before the first update.
            void OnLoad() {
            }

            // Every frame.
            void OnUpdate(MenhirTick dt) {
            }
        };

        """;

    /// <summary>Creates NewScript.c (NewScript1.c, ... when taken) in a folder. Returns its asset path.</summary>
    public static string Create(AssetDatabase assets, string folder, bool cpp = false)
    {
        string extension = cpp ? ".cpp" : ".c";
        if (!assets.IsFolder(folder))
            throw new ArgumentException($"'{folder}' is not a folder.");
        string name = DefaultName;
        for (int i = 1; File.Exists(assets.ToAbsolutePath(AssetPaths.Combine(folder, name + ".c"))) ||
                        File.Exists(assets.ToAbsolutePath(AssetPaths.Combine(folder, name + ".cpp"))); i++)
            name = DefaultName + i;
        string path = AssetPaths.Combine(folder, name + extension);
        File.WriteAllText(assets.ToAbsolutePath(path), cpp ? CppContent(name) : Content(name));
        assets.Refresh();
        return path;
    }

    /// <summary>
    /// After a script file is renamed: when it is still the untouched template, its MH_CREATE line takes
    /// the new name, like Unity does with classes. Returns true when the file was rewritten.
    /// </summary>
    public static bool RenameClass(string absolutePath, string oldFileName, string newFileName)
    {
        string oldName = Path.GetFileNameWithoutExtension(oldFileName);
        string newName = Path.GetFileNameWithoutExtension(newFileName);
        bool cpp = newFileName.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase);
        if (!(cpp || newFileName.EndsWith(".c", StringComparison.OrdinalIgnoreCase)) || !File.Exists(absolutePath) ||
            !IsIdentifier(newName) || File.ReadAllText(absolutePath) != (cpp ? CppContent(oldName) : Content(oldName)))
            return false;
        File.WriteAllText(absolutePath, cpp ? CppContent(newName) : Content(newName));
        return true;
    }

    /// <summary>A name C accepts (ASCII letters, digits and _, not starting with a digit).</summary>
    public static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]))
            return false;
        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }
        return true;
    }
}
