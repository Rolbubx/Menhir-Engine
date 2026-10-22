using System.Text;
using System.Text.RegularExpressions;

namespace Menhir.Editor.Scripting;

/// <summary>A script turned into the C file the compiler gets, or the errors that stop it.</summary>
public sealed record ScriptTransformResult(string? Generated, IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>
/// Menhir's simple script syntax (Engine/Scripting/include/menhir_simple.h, Docs/Scripting.md): a file
/// with MH_VARS, MH_START() { ... }, MH_UPDATE() { ... }... is one script named after the file. The
/// compiler gets a generated copy: MH_SCRIPT_NAME and menhir_simple.h first, MH_VARS' defaults moved into
/// fields (the inspector's) or into the script's initialization, coroutines wrapped, the MH_SCENE.Name
/// objects it uses, then MH_REGISTER at the end with the callbacks the file has (and an empty
/// MH_ON_GAME_CLOSE when it has none). #line directives keep the compiler's messages on the file's own
/// lines and columns (a line only changes after an inserted text). Files that use MH_SCRIPT, and files
/// with no script at all, are compiled as they are.
/// </summary>
public static partial class ScriptTransform
{
    /// <summary>Whether the file uses the simple syntax.</summary>
    public static bool IsSimple(string source)
    {
        string code = CodeOnly(source);
        return !ScriptMacro().IsMatch(code) && SimpleMarker().IsMatch(code);
    }

    /// <param name="path">The script's full path (for #line).</param>
    /// <param name="displayPath">Its path for messages ("Assets/Scripts/Player.c").</param>
    public static ScriptTransformResult Transform(string source, string path, string displayPath)
    {
        var diagnostics = new List<ScriptDiagnostic>();
        string name = Path.GetFileNameWithoutExtension(path);
        string code = CodeOnly(source);
        var edits = new List<(int Start, int Length, string Text)>();

        void Error(int index, string message)
        {
            (int line, int column) = Position(source, index);
            diagnostics.Add(new ScriptDiagnostic(displayPath, line, column, message, true));
        }

        if (!ScriptTemplate.IsIdentifier(name))
        {
            Error(0, $"A script's file name is its C name: '{name}' has to be letters, digits and _ (not starting with a digit).");
            return new ScriptTransformResult(null, diagnostics);
        }

        // MH_CREATE Test.c: only says the file's name.
        foreach (Match m in CreateLine().Matches(code))
        {
            string named = m.Groups["name"].Value;
            if (named != name)
                Error(m.Index, $"MH_CREATE names {named}, but the file is {name}.c (a script is named after its file).");
            edits.Add((m.Index, m.Length, ""));
        }

        // MH_VARS { ... };
        var fields = new List<string>();
        var inits = new List<string>();
        MatchCollection vars = VarsBlock().Matches(code);
        if (vars.Count > 1)
            Error(vars[1].Index, "A script has one MH_VARS block.");
        if (vars.Count > 0)
            ParseVars(source, code, vars[0], name, fields, inits, edits, Error);
        bool hasVars = vars.Count > 0;

        // MH_COROUTINE(name) { ... }: the body runs as a state machine between its waits.
        var prototypes = new List<string>();
        foreach (Match m in Coroutine().Matches(code))
        {
            prototypes.Add($"MH_COROUTINE({m.Groups["name"].Value});");
            int open = SkipSpace(code, m.Index + m.Length);
            if (open >= code.Length || code[open] != '{')
            {
                Error(m.Index, "MH_COROUTINE(name) is followed by its body: { ... }.");
                continue;
            }
            int close = MatchingBrace(code, open);
            if (close < 0)
            {
                Error(open, "This { has no matching }.");
                continue;
            }
            edits.Add((open + 1, 0, " MH__CO_BEGIN"));
            edits.Add((close, 0, "MH__CO_END "));
        }
        foreach (Match m in Timer().Matches(code))
            prototypes.Add($"MH_TIMER({m.Groups["name"].Value});");

        // The callbacks it has.
        var register = new List<string>();
        bool Has(Regex regex) => regex.IsMatch(code);
        if (Has(Start())) register.Add("MH_ENTRYPOINT_ONLOAD");
        if (Has(Update())) register.Add("MH_ONEVERYFRAME_UPDATE");
        if (Has(LateUpdate())) register.Add("MH_ONEVERYFRAME_LATE_UPDATE");
        if (Has(FixedUpdate())) register.Add("MH_ONEVERYFRAME_FIX_UP");
        foreach (Match m in EmptyNamed().Matches(code))
            Error(m.Index, $"{m.Groups["macro"].Value}(name) needs a name for its parameter, e.g. {m.Groups["macro"].Value}({(m.Groups["macro"].Value == "MH_FIXED_UPDATE" ? "dt" : m.Groups["macro"].Value == "MH_ON_GAME_CLOSE" ? "time" : "other")}).");
        bool hasClose = Has(GameClose());
        register.Add("MH_ONGAMECLOSE");
        if (Has(Destroy())) register.Add("MH_ONDESTROY");
        if (Has(CollisionEnter())) register.Add("MH_ONCOLLISION_ENTER");
        if (Has(CollisionExit())) register.Add("MH_ONCOLLISION_EXIT");
        if (Has(TriggerEnter())) register.Add("MH_ONTRIGGER_ENTER");
        if (Has(TriggerExit())) register.Add("MH_ONTRIGGER_EXIT");
        bool userRegister = Has(Register());

        if (diagnostics.Count > 0)
            return new ScriptTransformResult(null, diagnostics);

        var output = new StringBuilder();
        output.Append($"/* Generated by Menhir from {displayPath}: edit that file instead. */\n");
        output.Append($"#define MH_SCRIPT_NAME {name}\n");
        output.Append("#include <menhir_simple.h>\n");
        output.Append(fields.Count > 0
            ? $"#define MH__FIELD_LIST .fields = {name}__mh_fields, .field_count = {fields.Count}\n"
            : "#define MH__FIELD_LIST .fields = 0, .field_count = 0\n");
        output.Append($"static void {name}__mh_init(MH__VARS_T *mh__self, mh_entity mh__parent);\n");
        if (!hasVars)
            output.Append("MH_VARS { char mh__unused; };\n");
        foreach (string prototype in prototypes.Distinct())
            output.Append(prototype).Append('\n');
        AppendScene(output, code);
        output.Append($"#line 1 \"{LinePath(path)}\"\n");
        output.Append(Apply(source, edits));
        int lines = source.Count(c => c == '\n') + 1;
        output.Append($"\n#line {lines} \"{LinePath(path)}\"\n");
        if (!hasClose && !userRegister)
            output.Append("MH_ON_GAME_CLOSE(MH_UNUSED_CMP) { /* Generated automatically by the Menhir Engine */ (void)MH_UNUSED_CMP; }\n");
        output.Append($"static void {name}__mh_init(MH__VARS_T *mh__self, mh_entity mh__parent)\n{{\n");
        foreach (string init in inits)
            output.Append("    ").Append(init).Append('\n');
        if (Has(Awake()))
            output.Append($"    {name}__awake(mh__self, mh__parent);\n");
        output.Append("    (void)mh__self;\n    (void)mh__parent;\n}\n");
        if (!userRegister)
            output.Append($"MH_REGISTER({string.Join(", ", register)})\n");
        return new ScriptTransformResult(output.ToString(), diagnostics);
    }

    // ---- MH_VARS ------------------------------------------------------------------------------------

    private static void ParseVars(string source, string code, Match block, string name, List<string> fields, List<string> inits,
        List<(int Start, int Length, string Text)> edits, Action<int, string> error)
    {
        int open = block.Index + block.Length - 1;
        int close = MatchingBrace(code, open);
        if (close < 0)
        {
            error(open, "This { has no matching }.");
            return;
        }
        int semicolon = SkipSpace(code, close + 1);
        if (semicolon >= code.Length || code[semicolon] != ';')
        {
            error(close, "MH_VARS { ... } ends with a semicolon: };");
            return;
        }

        int start = open + 1;
        for (int i = open + 1; i < close; i++)
        {
            if (code[i] is '(' or '{' or '[')
            {
                i = Skip(code, i);
                continue;
            }
            if (code[i] != ';')
                continue;
            Member(source, code, start, i, fields, inits, edits, error);
            start = i + 1;
        }
        if (code[start..close].Trim().Length > 0)
            error(start, "Each variable of MH_VARS ends with a semicolon.");

        if (fields.Count > 0)
            edits.Add((semicolon + 1, 0, $" static const mh_field {name}__mh_fields[] = {{ {string.Join(", ", fields)} }};"));
    }

    private static void Member(string source, string code, int start, int end, List<string> fields, List<string> inits,
        List<(int Start, int Length, string Text)> edits, Action<int, string> error)
    {
        string text = code[start..end];
        if (text.Trim().Length == 0)
            return;
        int equals = TopLevel(text, '=');
        string declaration = (equals >= 0 ? text[..equals] : text).Trim();
        string? initializer = equals >= 0 ? source.Substring(start + equals + 1, end - start - equals - 1).Trim() : null;
        int at = start + text.Length - text.TrimStart().Length;
        if (TopLevel(declaration, ',') >= 0)
        {
            error(at, "One variable per declaration in MH_VARS (float a; float b;).");
            return;
        }
        Match d = Declarator().Match(declaration);
        if (!d.Success)
        {
            error(at, $"'{declaration}' is not a variable declaration.");
            return;
        }
        string member = d.Groups["name"].Value, array = d.Groups["array"].Value;
        string type = Regex.Replace(d.Groups["type"].Value.Trim(), @"\s+", " ").Replace(" *", "*").Replace("* ", "*");
        if (equals >= 0)
            edits.Add((start + equals, end - start - equals, "")); // a struct member has no initializer

        // The comment after it, on its line: MH_RANGE(min, max), MH_HIDE, and the tooltip.
        string comment = CommentAfter(source, end);
        bool hidden = Regex.IsMatch(comment, @"\bMH_HIDE\b");
        Match range = Regex.Match(comment, @"\bMH_RANGE\s*\(\s*(?<min>[^,()]+?)\s*,\s*(?<max>[^,()]+?)\s*\)");
        string tooltip = Regex.Replace(Regex.Replace(comment, @"\bMH_HIDE\b", ""), @"\bMH_RANGE\s*\([^)]*\)", "").Trim();

        string? kind = array.Length > 0 || hidden ? null : KindOf(type);
        string offset = $"(uint32_t)offsetof(MH__VARS_T, {member})";
        if (kind == null)
        {
            // Not a field: the initialization gives it its value.
            if (initializer is { Length: > 0 })
                inits.Add($"{{ {d.Groups["type"].Value.Trim()} mh__value{array} = {initializer}; __builtin_memcpy(&mh__self->{member}, &mh__value, sizeof mh__value); }}");
            else if (array.Length == 0 && type is "MH_GAMEOBJECT" or "MH_REFERENCE" or "mh_entity")
                inits.Add($"mh__self->{member} = MH_NONE;");
            return;
        }

        string number = "0";
        string textDefault = "\"\"";
        int count = kind switch { "MH_FIELD_VEC2" => 2, "MH_FIELD_VEC3" => 3, "MH_FIELD_COLOR" => 4, _ => 1 };
        if (initializer is { Length: > 0 })
        {
            if (kind == "MH_FIELD_STRING")
            {
                if (!initializer.StartsWith('"'))
                {
                    error(start + equals + 1, $"The default of {member} is a text in quotes.");
                    return;
                }
                textDefault = initializer;
            }
            else if (count == 1)
                number = initializer;
            else
            {
                List<string>? parts = Components(initializer);
                if (parts == null || parts.Count != count)
                {
                    error(start + equals + 1, $"The default of {member} has {count} numbers: {{{string.Join(", ", Enumerable.Repeat("0", count))}}} " +
                                              $"or {(count == 2 ? "MH_VEC2" : count == 3 ? "MH_VEC3" : "MH_COLOR")}({string.Join(", ", Enumerable.Repeat("0", count))}).");
                    return;
                }
                number = string.Join(", ", parts);
            }
        }
        var field = new StringBuilder($"{{ .name = \"{member}\", .kind = {kind}, .offset = {offset}, .number = {{ {number} }}");
        if (kind == "MH_FIELD_STRING")
            field.Append($", .text = {textDefault}");
        if (range.Success)
            field.Append($", .has_range = 1, .min = (float)({range.Groups["min"].Value}), .max = (float)({range.Groups["max"].Value})");
        if (tooltip.Length > 0)
            field.Append($", .tooltip = \"{Escape(tooltip)}\"");
        field.Append(" }");
        fields.Add(field.ToString());
    }

    private static string? KindOf(string type) => type switch
    {
        "int" or "int32_t" => "MH_FIELD_INT",
        "float" => "MH_FIELD_FLOAT",
        "bool" or "_Bool" => "MH_FIELD_BOOL",
        "mh_vec2" or "MH_VEC2" => "MH_FIELD_VEC2",
        "mh_vec3" or "MH_VEC3" => "MH_FIELD_VEC3",
        "mh_color" or "MH_COLOR" => "MH_FIELD_COLOR",
        "const char*" or "char const*" or "MH_STRING" => "MH_FIELD_STRING",
        _ => null,
    };

    /// <summary>"{1, 2, 3}" or "MH_VEC3(1, 2, 3)" (any call) as its parts.</summary>
    private static List<string>? Components(string initializer)
    {
        string inner;
        if (initializer.StartsWith('{') && initializer.EndsWith('}'))
            inner = initializer[1..^1];
        else if (Regex.Match(initializer, @"^\w+\s*\((?<args>.*)\)$", RegexOptions.Singleline) is { Success: true } call)
            inner = call.Groups["args"].Value;
        else
            return null;
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i < inner.Length && inner[i] is '(' or '{' or '[')
            {
                i = Skip(inner, i);
                continue;
            }
            if (i == inner.Length || inner[i] == ',')
            {
                parts.Add(inner[start..i].Trim());
                start = i + 1;
            }
        }
        return parts.Any(p => p.Length == 0) ? null : parts;
    }

    private static string CommentAfter(string source, int semicolon)
    {
        int lineEnd = source.IndexOf('\n', semicolon);
        string rest = source[(semicolon + 1)..(lineEnd < 0 ? source.Length : lineEnd)].Trim();
        if (rest.StartsWith("//"))
            return rest[2..].Trim();
        if (rest.StartsWith("/*") && rest.IndexOf("*/", StringComparison.Ordinal) is var end and > 0)
            return rest[2..end].Trim();
        return "";
    }

    // ---- MH_SCENE --------------------------------------------------------------------------------------

    private static void AppendScene(StringBuilder output, string code)
    {
        List<string> names = SceneObject().Matches(code).Select(m => m.Groups["name"].Value).Distinct().ToList();
        if (names.Count == 0)
            return;
        output.Append("typedef struct { ").Append(string.Concat(names.Select(n => $"mh_entity {n}; "))).Append("} mh__scene_t;\n");
        output.Append("static mh__scene_t mh__scene(void)\n{\n");
        output.Append($"    static mh_entity cache[{names.Count}] = {{ {string.Join(", ", names.Select(_ => "MH_NONE"))} }};\n");
        output.Append("    mh__scene_t s;\n");
        for (int i = 0; i < names.Count; i++)
            output.Append($"    s.{names[i]} = mh__scene_object(\"{names[i]}\", &cache[{i}]);\n");
        output.Append("    return s;\n}\n#define MH_SCENE (mh__scene())\n");
    }

    // ---- Text -----------------------------------------------------------------------------------------

    /// <summary>The source with comments and the insides of strings and characters blanked (lines kept).</summary>
    public static string CodeOnly(string source)
    {
        var code = source.ToCharArray();
        for (int i = 0; i < code.Length; i++)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                for (; i < code.Length && source[i] != '\n'; i++)
                    code[i] = ' ';
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                for (; i < end; i++)
                    if (source[i] != '\n')
                        code[i] = ' ';
                i--;
            }
            else if (c is '"' or '\'')
            {
                for (i++; i < source.Length && source[i] != c && source[i] != '\n'; i++)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                        code[i++] = ' ';
                    code[i] = ' ';
                }
            }
        }
        return new string(code);
    }

    /// <summary>Applies the edits (removed text becomes spaces, so lines stay where they are).</summary>
    private static string Apply(string source, List<(int Start, int Length, string Text)> edits)
    {
        var result = new StringBuilder(source);
        foreach ((int start, int length, string text) in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
        {
            var blank = new StringBuilder();
            for (int i = start; i < start + length; i++)
                blank.Append(source[i] == '\n' ? '\n' : source[i] == '\r' ? '\r' : ' ');
            result.Remove(start, length).Insert(start, text + blank.ToString()[Math.Min(text.Length, blank.Length)..]);
        }
        return result.ToString();
    }

    private static int SkipSpace(string code, int i)
    {
        while (i < code.Length && char.IsWhiteSpace(code[i]))
            i++;
        return i;
    }

    /// <summary>From an opening (, { or [ to its closing one.</summary>
    private static int Skip(string code, int open)
    {
        int close = MatchingBrace(code, open);
        return close < 0 ? code.Length : close;
    }

    private static int MatchingBrace(string code, int open)
    {
        char opening = code[open], closing = opening switch { '(' => ')', '[' => ']', _ => '}' };
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == opening)
                depth++;
            else if (code[i] == closing && --depth == 0)
                return i;
        }
        return -1;
    }

    /// <summary>The first <paramref name="c"/> outside brackets, or -1.</summary>
    private static int TopLevel(string text, char c)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '(' or '{' or '[')
                i = Skip(text, i);
            else if (text[i] == c)
                return i;
        }
        return -1;
    }

    private static (int Line, int Column) Position(string source, int index)
    {
        int line = 1, column = 1;
        for (int i = 0; i < index && i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
                column++;
        }
        return (line, column);
    }

    private static string LinePath(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [GeneratedRegex(@"\bMH_SCRIPT\s*\(")]
    private static partial Regex ScriptMacro();

    [GeneratedRegex(@"\b(?:MH_CREATE|MH_VARS)\b|\bMH_(?:AWAKE|START|UPDATE|LATE_UPDATE|ON_DESTROY)\s*\(\s*\)|\bMH_(?:FIXED_UPDATE|ON_GAME_CLOSE|ON_COLLISION(?:_ENTER|_EXIT)?|ON_TRIGGER(?:_ENTER|_EXIT)?|COROUTINE|TIMER)\s*\(\s*\w*\s*\)|\bMH_REGISTER\s*\(")]
    private static partial Regex SimpleMarker();

    [GeneratedRegex(@"\bMH_CREATE\b[ \t]*(?:\(\s*(?<name>\w+)\s*\)|(?<name>\w+)(?:\.[cC]\b)?)[ \t]*;?")]
    private static partial Regex CreateLine();

    [GeneratedRegex(@"\bMH_VARS\s*\{")]
    private static partial Regex VarsBlock();

    [GeneratedRegex(@"^(?<type>.*?[\w\*\s])(?<name>[A-Za-z_]\w*)\s*(?<array>(?:\[[^\]]*\]\s*)*)$", RegexOptions.Singleline)]
    private static partial Regex Declarator();

    [GeneratedRegex(@"\bMH_COROUTINE\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex Coroutine();

    [GeneratedRegex(@"\bMH_TIMER\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex Timer();

    [GeneratedRegex(@"\bMH_AWAKE\s*\(\s*\)")]
    private static partial Regex Awake();

    [GeneratedRegex(@"\bMH_START\s*\(\s*\)")]
    private static partial Regex Start();

    [GeneratedRegex(@"\bMH_UPDATE\s*\(\s*\)")]
    private static partial Regex Update();

    [GeneratedRegex(@"\bMH_LATE_UPDATE\s*\(\s*\)")]
    private static partial Regex LateUpdate();

    [GeneratedRegex(@"\bMH_FIXED_UPDATE\s*\(\s*[A-Za-z_]\w*\s*\)")]
    private static partial Regex FixedUpdate();

    [GeneratedRegex(@"\bMH_ON_GAME_CLOSE\s*\(\s*[A-Za-z_]\w*\s*\)")]
    private static partial Regex GameClose();

    [GeneratedRegex(@"\b(?<macro>MH_FIXED_UPDATE|MH_ON_GAME_CLOSE|MH_ON_COLLISION(?:_ENTER|_EXIT)?|MH_ON_TRIGGER(?:_ENTER|_EXIT)?)\s*\(\s*\)")]
    private static partial Regex EmptyNamed();

    [GeneratedRegex(@"\bMH_ON_DESTROY\s*\(\s*\)")]
    private static partial Regex Destroy();

    [GeneratedRegex(@"\bMH_ON_COLLISION(?:_ENTER)?\s*\(\s*[A-Za-z_]\w*\s*\)")]
    private static partial Regex CollisionEnter();

    [GeneratedRegex(@"\bMH_ON_COLLISION_EXIT\s*\(\s*[A-Za-z_]\w*\s*\)")]
    private static partial Regex CollisionExit();

    [GeneratedRegex(@"\bMH_ON_TRIGGER(?:_ENTER)?\s*\(\s*[A-Za-z_]\w*\s*\)")]
    private static partial Regex TriggerEnter();

    [GeneratedRegex(@"\bMH_ON_TRIGGER_EXIT\s*\(\s*[A-Za-z_]\w*\s*\)")]
    private static partial Regex TriggerExit();

    [GeneratedRegex(@"\bMH_REGISTER\s*\(")]
    private static partial Regex Register();

    [GeneratedRegex(@"\bMH_SCENE\s*\.\s*(?<name>[A-Za-z_]\w*)")]
    private static partial Regex SceneObject();
}
