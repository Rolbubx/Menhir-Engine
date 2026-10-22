namespace Menhir.Setup;

/// <summary>The console wizard's look: the logo, step titles and questions. With --yes, every question takes its default.</summary>
internal static class Ui
{
    public static bool AcceptDefaults { get; set; }

    // Plain ASCII: old Windows consoles show anything else as garbage.
    private const string Logo = """
             _.--._
            /  .   \        __  __  _____  _   _  _   _  ___  ____
           |   :    |      |  \/  || ____|| \ | || | | ||_ _||  _ \
           |  .  :  |      | |\/| ||  _|  |  \| || |_| | | | | |_) |
           | :   .  |      | |  | || |___ | |\  ||  _  | | | |  _ <
           |   .   :|      |_|  |_||_____||_| \_||_| |_||___||_| \_\
          /|  :  .  |\
        ~~~~~~~~~~~~~~~~        E N G I N E   -   scripted in C
        """;

    public static void ShowLogo(string subtitle)
    {
        Console.WriteLine();
        Write(Logo, ConsoleColor.Cyan);
        Console.WriteLine();
        Write($"  {subtitle}\n\n", ConsoleColor.White);
    }

    public static void Step(int number, int count, string title)
    {
        Console.WriteLine();
        Write($"  [{number}/{count}] {title}\n", ConsoleColor.Yellow);
        Write($"  {new string('-', title.Length + 6)}\n", ConsoleColor.DarkYellow);
    }

    public static void Info(string text) => Console.WriteLine("  " + text.Replace("\n", "\n  "));

    public static void Dim(string text) => Write("  " + text.Replace("\n", "\n  ") + "\n", ConsoleColor.DarkGray);

    public static void Good(string text) => Write($"  OK  {text}\n", ConsoleColor.Green);

    public static void Warn(string text) => Write($"  !   {text}\n", ConsoleColor.Yellow);

    public static void Error(string text) => Write($"  X   {text}\n", ConsoleColor.Red);

    /// <summary>A line of text; Enter (or --yes) gives <paramref name="defaultValue"/>.</summary>
    public static string Ask(string question, string defaultValue)
    {
        Write($"  > {question}", ConsoleColor.White);
        if (defaultValue.Length > 0)
            Write($" [{defaultValue}]", ConsoleColor.DarkGray);
        Console.Write(": ");
        if (AcceptDefaults)
        {
            Console.WriteLine(defaultValue);
            return defaultValue;
        }
        string? answer = Console.ReadLine();
        return string.IsNullOrWhiteSpace(answer) ? defaultValue : answer.Trim().Trim('"');
    }

    public static bool Confirm(string question, bool defaultValue)
    {
        while (true)
        {
            string answer = Ask($"{question} ({(defaultValue ? "Y/n" : "y/N")})", "").ToLowerInvariant();
            if (answer.Length == 0)
                return defaultValue;
            if (answer is "y" or "yes" or "o" or "oui")
                return true;
            if (answer is "n" or "no" or "non")
                return false;
        }
    }

    public static void Write(string text, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    /// <summary>Keeps the window open when the program was started by double-clicking it.</summary>
    public static void Pause()
    {
        if (AcceptDefaults || Console.IsInputRedirected)
            return;
        Write("\n  Press Enter to close.", ConsoleColor.DarkGray);
        Console.ReadLine();
    }
}
