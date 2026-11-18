namespace PhotoShrink;

/// <summary>One result window per batch. Stays open until the user closes it. Offers Undo.</summary>
internal sealed class SummaryForm : Form
{
    private readonly IReadOnlyList<ShrinkResult> _results;
    private readonly ShrinkMode _mode;
    private readonly TextBox _box;
    private readonly Button _undo = new() { Text = "Undo", Width = 90 };

    public SummaryForm(string text, IReadOnlyList<ShrinkResult> results, ShrinkMode mode)
    {
        _results = results;
        _mode = mode;
        int shrunk = results.Count(r => r.Status == ShrinkStatus.Shrunk);
        int failed = results.Count(r => r.Status == ShrinkStatus.Failed);

        Text = failed > 0
            ? $"PhotoShrink — {shrunk} shrunk, {failed} failed"
            : $"PhotoShrink — {shrunk} shrunk";
        AppIcon.Apply(this);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(860, 480);
        MinimumSize = new Size(480, 260);

        _box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.75f),
            Text = Crlf(text),
            BackColor = SystemColors.Window,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(6),
        };
        var close = new Button { Text = "Close", Width = 90 };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "Copy", Width = 90 };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_box.Text); } catch { /* clipboard busy */ }
        };
        var settings = new Button { Text = "Settings…", Width = 90 };
        settings.Click += (_, _) =>
        {
            Hide();
            try
            {
                using var f = new SettingsForm();
                f.ShowDialog();
            }
            finally
            {
                Show();
                Activate();
            }
        };
        _undo.Enabled = shrunk > 0;
        _undo.Click += (_, _) => RunUndo(shrunk);

        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(_undo);
        buttons.Controls.Add(settings);

        Controls.Add(_box);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        Shown += (_, _) => { _box.SelectionStart = 0; _box.SelectionLength = 0; close.Focus(); };
    }

    private void RunUndo(int shrunk)
    {
        string question = _mode == ShrinkMode.SaveCopy
            ? $"Delete the {shrunk} shrunk cop{(shrunk == 1 ? "y" : "ies")}? The originals were never changed."
            : $"Put the original back for {shrunk} photo{(shrunk == 1 ? "" : "s")}? The shrunk versions will be deleted.";
        if (MessageBox.Show(this, question, "PhotoShrink — Undo", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        Cursor = Cursors.WaitCursor;
        List<UndoResult> undo;
        try
        {
            undo = FileMover.Undo(_results);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _undo.Enabled = false;
        _undo.Text = "Undone";
        _box.AppendText(Crlf(Summary.BuildUndoText(undo)));
        int bad = undo.Count(u => !u.Ok);
        Text = bad == 0 ? "PhotoShrink — undone" : $"PhotoShrink — undone, {bad} problem(s)";
    }

    private static string Crlf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");
}
