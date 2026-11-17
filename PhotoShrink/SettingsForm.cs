namespace PhotoShrink;

/// <summary>Opens when PhotoShrink.exe is started with no arguments.</summary>
internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _quality = new() { Minimum = 1, Maximum = 100, Width = 70 };
    private readonly Label _state = new() { AutoSize = false, Width = 470, Height = 80 };
    private readonly Button _install = new() { Text = "Install menu", Width = 130 };
    private readonly Button _uninstall = new() { Text = "Uninstall", Width = 130 };

    public SettingsForm()
    {
        Text = "PhotoShrink settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var settings = Settings.Load();
        _quality.Value = settings.JpegQuality;

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(14),
            WrapContents = false,
        };

        var qualityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        qualityRow.Controls.Add(new Label { Text = "JPEG quality (1–100):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        qualityRow.Controls.Add(_quality);
        var save = new Button { Text = "Save", Width = 80 };
        save.Click += (_, _) =>
        {
            var s = Settings.Load();
            s.JpegQuality = (int)_quality.Value;
            try
            {
                s.Save();
                MessageBox.Show(this, $"Saved. JPEG quality is now {s.JpegQuality}.", "PhotoShrink");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings: " + ex.Message, "PhotoShrink",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        qualityRow.Controls.Add(save);

        layout.Controls.Add(qualityRow);
        layout.Controls.Add(new Label
        {
            Text = "Higher = better quality, larger files. 85 is a good balance.\nSaved in " + Settings.FilePath,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 2, 0, 14),
        });

        layout.Controls.Add(new Label { Text = "Right-click menu and Send To shortcuts:", AutoSize = true });
        layout.Controls.Add(_state);

        var buttonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttonRow.Controls.Add(_install);
        buttonRow.Controls.Add(_uninstall);
        layout.Controls.Add(buttonRow);

        _install.Click += (_, _) => Run(() => ShellIntegration.Install(ShellIntegration.CurrentExePath),
            "Installed. Right-click a photo → Show more options → Shrink photo.");
        _uninstall.Click += (_, _) => Run(ShellIntegration.Uninstall,
            "Uninstalled. All menu entries and Send To shortcuts were removed.");

        Controls.Add(layout);
        RefreshState();
    }

    private void Run(Action action, string doneMessage)
    {
        try
        {
            action();
            MessageBox.Show(this, doneMessage, "PhotoShrink", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PhotoShrink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshState();
    }

    private void RefreshState()
    {
        string? exe = ShellIntegration.InstalledExePath();
        string me = ShellIntegration.CurrentExePath;
        if (exe == null)
        {
            _state.Text = "Not installed.\nInstall registers this copy:\n" + me;
            _uninstall.Enabled = ShellIntegration.ListInstalledItems().Any();
        }
        else
        {
            bool same = string.Equals(exe, me, StringComparison.OrdinalIgnoreCase);
            _state.Text = "Installed. The menu runs:\n" + exe +
                          (same ? "" : "\n(Not this copy. Click Install to point it here.)");
            _uninstall.Enabled = true;
        }
        _install.Text = exe == null ? "Install menu" : "Reinstall menu";
    }
}
