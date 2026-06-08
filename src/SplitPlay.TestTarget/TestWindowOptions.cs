namespace SplitPlay.TestTarget;

/// <summary>
/// Parsed command-line options for a test window:
/// <c>--player N --controller I --color #RRGGBB</c>.
/// </summary>
public sealed class TestWindowOptions
{
    public int Player { get; init; } = 1;

    /// <summary>XInput controller index assigned to this window (-1 if none).</summary>
    public int Controller { get; init; } = -1;

    public string Color { get; init; } = "#4FD1A5";

    public static TestWindowOptions Parse(string[] args)
    {
        int player = 1;
        int controller = -1;
        string color = "#4FD1A5";

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--player" when int.TryParse(args[i + 1], out int p):
                    player = p;
                    break;
                case "--controller" when int.TryParse(args[i + 1], out int c):
                    controller = c;
                    break;
                case "--color":
                    color = args[i + 1];
                    break;
            }
        }

        return new TestWindowOptions { Player = player, Controller = controller, Color = color };
    }
}
