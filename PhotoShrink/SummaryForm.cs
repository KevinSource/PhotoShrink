namespace PhotoShrink;

/// <summary>One result window per batch. Stays open until the user closes it.</summary>
internal sealed class SummaryForm : Form
{
    public SummaryForm(string text, IReadOnlyList<ShrinkResult> results)
    {
        int shrunk = results.Count(r => r.Status == ShrinkStatus.Shrunk);
        int failed = results.Count(r => r.Status == ShrinkStatus.Failed);

        Text = failed > 0
            ? $"PhotoShrink — {shrunk} shrunk, {failed} failed"
            : $"PhotoShrink — {shrunk} shrunk";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 460);
        MinimumSize = new Size(420, 240);
        ShowIcon = true;

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.75f),
            Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
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
            try { Clipboard.SetText(box.Text); } catch { /* clipboard busy */ }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);

        Controls.Add(box);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        Shown += (_, _) => { box.SelectionStart = 0; box.SelectionLength = 0; close.Focus(); };
    }
}
