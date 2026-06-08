using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SplitPlay.Core.Models;
using SplitPlay.Input;

namespace SplitPlay.TestTarget;

/// <summary>
/// The test window itself. Shows which player/region it represents and the live
/// input of its assigned controller, so during testing you can confirm both the
/// split placement and which pad maps to which side.
/// </summary>
public sealed class TestWindowForm : Form
{
    private readonly TestWindowOptions _options;
    private readonly Color _background;
    private readonly Color _foreground;
    private readonly Label _titleLabel;
    private readonly Label _inputLabel;
    private readonly System.Windows.Forms.Timer _pollTimer;

    public TestWindowForm(TestWindowOptions options)
    {
        _options = options;
        _background = ParseColorOrDefault(options.Color, Color.FromArgb(0x4F, 0xD1, 0xA5));
        _foreground = GetReadableForeground(_background);

        // A title is required so the launch engine can find this window reliably.
        Text = $"SplitPlay Player {options.Player}";
        BackColor = _background;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 640;
        Height = 420;
        KeyPreview = true;
        DoubleBuffered = true;

        _titleLabel = new Label
        {
            Text = $"PLAYER {options.Player}",
            ForeColor = _foreground,
            Font = new Font("Segoe UI", 48f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 160,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _inputLabel = new Label
        {
            Text = BuildHeader(),
            ForeColor = _foreground,
            Font = new Font("Consolas", 16f, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.Add(_inputLabel);
        Controls.Add(_titleLabel);

        // Poll the assigned controller ~30 fps for responsive input feedback.
        _pollTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // Esc closes the window for convenience during testing.
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    private string BuildHeader() =>
        _options.Controller >= 0
            ? $"Controller {_options.Controller + 1}\n\nPress buttons to test input"
            : "No controller assigned";

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_options.Controller < 0)
        {
            return;
        }

        GamepadState state = XInputReader.ReadState(_options.Controller);
        if (!state.IsConnected)
        {
            _inputLabel.Text = $"Controller {_options.Controller + 1}\n\n(disconnected)";
            return;
        }

        string buttons = DescribeButtons(state.Buttons);
        _inputLabel.Text =
            $"Controller {_options.Controller + 1}\n\n" +
            $"Buttons: {(buttons.Length == 0 ? "-" : buttons)}\n" +
            $"Triggers: L {state.LeftTrigger}  R {state.RightTrigger}\n" +
            $"Left stick: {state.LeftStickX}, {state.LeftStickY}\n" +
            $"Right stick: {state.RightStickX}, {state.RightStickY}";
    }

    private static string DescribeButtons(GamepadButtons buttons)
    {
        if (buttons == GamepadButtons.None)
        {
            return string.Empty;
        }

        return string.Join(" ", Enum.GetValues<GamepadButtons>()
            .Where(b => b != GamepadButtons.None && (buttons & b) == b)
            .Select(b => b.ToString()));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        base.OnFormClosed(e);
    }

    private static Color ParseColorOrDefault(string hex, Color fallback)
    {
        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Picks black or white text for good contrast on the given background.</summary>
    private static Color GetReadableForeground(Color background)
    {
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.55 ? Color.FromArgb(0x10, 0x24, 0x1D) : Color.White;
    }
}
