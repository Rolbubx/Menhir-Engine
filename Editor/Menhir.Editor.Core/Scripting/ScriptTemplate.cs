using Menhir.Editor.Assets;

namespace Menhir.Editor.Scripting;

/// <summary>Project > Create > C# Script: a new script class named after its file.</summary>
public static class ScriptTemplate
{
    public const string DefaultName = "NewScript";

    public static string Content(string className) => $$"""
        using MenhirEngine;

        public class {{className}} : Script
        {
            // Before the first Update.
            public override void Start()
            {
            }

            // Every frame.
            public override void Update()
            {
            }
        }

        """;

    /// <summary>Creates NewScript.cs (NewScript1.cs, ... when taken) in a folder. Returns its asset path.</summary>
    public static string Create(AssetDatabase assets, string folder)
    {
        if (!assets.IsFolder(folder))
            throw new ArgumentException($"'{folder}' is not a folder.");
        string name = DefaultName;
        for (int i = 1; File.Exists(assets.ToAbsolutePath(AssetPaths.Combine(folder, name + ".cs"))); i++)
            name = DefaultName + i;
        string path = AssetPaths.Combine(folder, name + ".cs");
        File.WriteAllText(assets.ToAbsolutePath(path), Content(name));
        assets.Refresh();
        return path;
    }

    /// <summary>
    /// After a script file is renamed: when it is still the untouched template, its class takes the
    /// new name, like Unity does. Returns true when the file was rewritten.
    /// </summary>
    public static bool RenameClass(string absolutePath, string oldFileName, string newFileName)
    {
        string oldClass = Path.GetFileNameWithoutExtension(oldFileName);
        string newClass = Path.GetFileNameWithoutExtension(newFileName);
        if (!newFileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(absolutePath) ||
            !IsIdentifier(newClass) || File.ReadAllText(absolutePath) != Content(oldClass))
            return false;
        File.WriteAllText(absolutePath, Content(newClass));
        return true;
    }

    /// <summary>A name C# accepts for a class (letters, digits and _, not starting with a digit).</summary>
    public static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]))
            return false;
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }
        return true;
    }
}
