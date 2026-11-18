namespace PhotoShrink;

/// <summary>"Shrink 23 photos to 1600 px?" — shown before any file is touched (can be turned off).</summary>
internal sealed class ConfirmForm : Form
{
    private readonly int _maxSize;
    private readonly ShrinkMode? _modeOverride;
    private readonly Label _modeLabel = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 10, 0, 0) };
    private readonly CheckBox _dontAsk = new() { Text = "Don't ask me again", AutoSize = true, Margin = new Padding(0, 14, 0, 0) };

    public ShrinkMode Mode { get; private set; }

    public ConfirmForm(int count, int maxSize, ShrinkMode? modeOverride)
    {
        _maxSize = maxSize;
        _modeOverride = modeOverride;

        Text = "PhotoShrink";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(16, 14, 16, 10),
        };

        layout.Controls.Add(new Label
        {
            Text = $"Shrink {count} selected file{(count == 1 ? "" : "s")} so the long edge is at most {maxSize} px?",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold),
        });
        layout.Controls.Add(_modeLabel);
        layout.Controls.Add(new Label
        {
            Text = "Photos that are already small enough, GIFs and other file types are left alone. " +
                   "You can undo from the summary window when it's done.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0),
        });
        layout.Controls.Add(_dontAsk);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 14, 0, 0),
        };
        var settings = new Button { Text = "Settings…", Width = 100 };
        var spacer = new Panel { Width = 110, Height = 1 };
        var ok = new Button { Text = "Shrink", Width = 100, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { settings, spacer, ok, cancel });
        layout.Controls.Add(buttons);

        settings.Click += (_, _) =>
        {
            using var f = new SettingsForm();
            f.ShowDialog(this);
            RefreshMode();
        };

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        RefreshMode();
    }

    private void RefreshMode()
    {
        var s = Settings.Load();
        Mode = _modeOverride ?? s.Mode;
        _modeLabel.Text = ShrinkModeText.Confirm(Mode, _maxSize);
        _dontAsk.Checked = !s.ConfirmBeforeShrinking;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _dontAsk.Checked)
        {
            try
            {
                var s = Settings.Load();
                s.ConfirmBeforeShrinking = false;
                s.Save();
            }
            catch
            {
                // Not being able to save the preference is no reason to stop.
            }
        }
        base.OnFormClosing(e);
    }
}
