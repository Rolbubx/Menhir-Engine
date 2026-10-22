using Menhir.Editor.Assets;

namespace Menhir.Editor.Scripting;

/// <summary>Project > Create > C Script: a new script, its struct named after its file.</summary>
public static class ScriptTemplate
{
    public const string DefaultName = "NewScript";

    public static string Content(string name) => $$"""
        #include <menhir.h>

        typedef struct {
            float speed;
        } {{name}};

        /* The fields the inspector shows and scenes save: MH_INT, MH_FLOAT, MH_BOOL, MH_VEC3, MH_STRING... */
        static const mh_field {{name}}_fields[] = {
            MH_FLOAT({{name}}, speed, 1),
        };

        /* Before the first update. */
        static void {{name}}_start({{name}} *self, mh_entity entity)
        {
        }

        /* Every frame. */
        static void {{name}}_update({{name}} *self, mh_entity entity)
        {
        }

        MH_SCRIPT({{name}}, MH_FIELDS({{name}}_fields), MH_START({{name}}_start), MH_UPDATE({{name}}_update));

        """;

    /// <summary>Creates NewScript.c (NewScript1.c, ... when taken) in a folder. Returns its asset path.</summary>
    public static string Create(AssetDatabase assets, string folder)
    {
        if (!assets.IsFolder(folder))
            throw new ArgumentException($"'{folder}' is not a folder.");
        string name = DefaultName;
        for (int i = 1; File.Exists(assets.ToAbsolutePath(AssetPaths.Combine(folder, name + ".c"))); i++)
            name = DefaultName + i;
        string path = AssetPaths.Combine(folder, name + ".c");
        File.WriteAllText(assets.ToAbsolutePath(path), Content(name));
        assets.Refresh();
        return path;
    }

    /// <summary>
    /// After a script file is renamed: when it is still the untouched template, its struct takes the
    /// new name, like Unity does with classes. Returns true when the file was rewritten.
    /// </summary>
    public static bool RenameClass(string absolutePath, string oldFileName, string newFileName)
    {
        string oldName = Path.GetFileNameWithoutExtension(oldFileName);
        string newName = Path.GetFileNameWithoutExtension(newFileName);
        if (!newFileName.EndsWith(".c", StringComparison.OrdinalIgnoreCase) || !File.Exists(absolutePath) ||
            !IsIdentifier(newName) || File.ReadAllText(absolutePath) != Content(oldName))
            return false;
        File.WriteAllText(absolutePath, Content(newName));
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
