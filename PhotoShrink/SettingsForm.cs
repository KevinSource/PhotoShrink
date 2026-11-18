namespace PhotoShrink;

/// <summary>Opens when PhotoShrink.exe is started with no arguments, or from Settings… buttons.</summary>
internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _quality = new() { Minimum = 1, Maximum = 100, Width = 70 };
    private readonly Dictionary<ShrinkMode, RadioButton> _modes = new();
    private readonly CheckBox _confirm = new() { Text = "Ask before shrinking (shows which option above is used)", AutoSize = true };
    private readonly Label _state = new() { AutoSize = false, Width = 470, Height = 80 };
    // AutoSize: the labels change with the install state (see RefreshState).
    private readonly Button _install = new() { AutoSize = true, MinimumSize = new Size(130, 0), Padding = new Padding(6, 0, 6, 0) };
    private readonly Button _uninstall = new() { Text = "Remove from right-click menu", AutoSize = true, MinimumSize = new Size(130, 0), Padding = new Padding(6, 0, 6, 0) };

    public SettingsForm()
    {
        Text = "PhotoShrink settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
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

        // --- When shrinking
        var modeGroup = new GroupBox { Text = "When shrinking", AutoSize = true, Padding = new Padding(10, 6, 10, 8) };
        var modeList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(10, 22) };
        foreach (ShrinkMode m in Enum.GetValues<ShrinkMode>())
        {
            var rb = new RadioButton
            {
                Text = ShrinkModeText.Label(m) + (m == ShrinkMode.RecycleBin ? "  (default)" : ""),
                AutoSize = true,
                Checked = settings.Mode == m,
            };
            _modes[m] = rb;
            modeList.Controls.Add(rb);
        }
        modeList.Controls.Add(new Label
        {
            Text = "On drives without a Recycle Bin (network, most USB drives) originals go to the Originals folder.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(18, 2, 0, 0),
        });
        modeGroup.Controls.Add(modeList);
        layout.Controls.Add(modeGroup);

        _confirm.Checked = settings.ConfirmBeforeShrinking;
        _confirm.Margin = new Padding(3, 8, 3, 8);
        layout.Controls.Add(_confirm);

        // --- JPEG quality
        var qualityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        qualityRow.Controls.Add(new Label { Text = "JPEG quality (1–100):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        qualityRow.Controls.Add(_quality);
        layout.Controls.Add(qualityRow);
        layout.Controls.Add(new Label
        {
            Text = "Higher = better quality, larger files. 85 is a good balance.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 2, 0, 8),
        });

        var save = new Button { Text = "Save settings", Width = 130 };
        save.Click += (_, _) => SaveSettings();
        layout.Controls.Add(save);
        layout.Controls.Add(new Label
        {
            Text = "Saved in " + Settings.FilePath,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 2, 0, 16),
        });

        // --- Install / uninstall
        layout.Controls.Add(new Label { Text = "Right-click menu and Send To shortcuts:", AutoSize = true });
        layout.Controls.Add(_state);
        var buttonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttonRow.Controls.Add(_install);
        buttonRow.Controls.Add(_uninstall);
        layout.Controls.Add(buttonRow);

        _install.Click += (_, _) => Run(() => ShellIntegration.Install(ShellIntegration.CurrentExePath),
            "Added. Right-click a photo → Show more options → Shrink photo.\nThe Send To shortcuts were added too.");
        _uninstall.Click += (_, _) => Run(ShellIntegration.Uninstall,
            "Removed. The right-click menu entries and Send To shortcuts are gone.\nYou can delete PhotoShrink.exe now if you don't want it.");

        Controls.Add(layout);
        RefreshState();
    }

    private void SaveSettings()
    {
        var s = Settings.Load();
        s.JpegQuality = (int)_quality.Value;
        s.Mode = _modes.First(kv => kv.Value.Checked).Key;
        s.ConfirmBeforeShrinking = _confirm.Checked;
        try
        {
            s.Save();
            if (Modal)
            {
                // Opened from the confirmation or summary window: saving takes you straight back there.
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(this, "Saved.", "PhotoShrink");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save settings: " + ex.Message, "PhotoShrink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            _state.Text = "Not on the right-click menu yet.\nAdding it will run this copy:\n" + me;
            _uninstall.Enabled = ShellIntegration.ListInstalledItems().Any();
        }
        else
        {
            bool same = string.Equals(exe, me, StringComparison.OrdinalIgnoreCase);
            _state.Text = "On the right-click menu. It runs:\n" + exe +
                          (same ? "" : "\n(Not this copy. Click \"Use this copy for the menu\" to switch.)");
            _uninstall.Enabled = true;
        }
        _install.Text = exe == null
            ? "Add to right-click menu"
            : string.Equals(exe, me, StringComparison.OrdinalIgnoreCase)
                ? "Repair right-click menu"
                : "Use this copy for the menu";
    }
}
