using System.Numerics;
using Menhir.Editor.Logging;

namespace Menhir.Editor.UI;

/// <summary>Icon and color of each log level, shared by the Console panel and the status bar.</summary>
public static class LogStyle
{
    public static string Icon(LogLevel level) => level switch
    {
        LogLevel.Warning => Icons.Warning,
        LogLevel.Error => Icons.Error,
        _ => Icons.Info,
    };

    public static Vector4 Color(LogLevel level) => level switch
    {
        LogLevel.Warning => EditorTheme.Warning,
        LogLevel.Error => EditorTheme.Error,
        _ => EditorTheme.TextDim,
    };
}
