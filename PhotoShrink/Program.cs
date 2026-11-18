namespace PhotoShrink;

internal static class Program
{
    /// <summary>
    /// Command line:
    ///   PhotoShrink.exe                          settings window
    ///   PhotoShrink.exe --max 1600 file [file…]  shrink files (right-click menu / Send To)
    ///   PhotoShrink.exe --install | --uninstall  register / remove the menu and Send To shortcuts
    /// Extra switches:
    ///   --silent          no message box after --install / --uninstall
    ///   --mode m          recycle | originals | copy — overrides the saved setting for this run
    ///   --yes             skip the confirmation dialog
    ///   --report path     append a JSON line per batch to path and skip dialogs and the summary (testing)
    ///   --test-undo       with --report: undo the batch straight away and report that too (testing)
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        CommandLine cl;
        try
        {
            cl = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show(ex.Message + "\n\n" + CommandLine.Usage, "PhotoShrink",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        if (cl.Install || cl.Uninstall)
            return RunInstall(cl);

        if (cl.MaxSize is null)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new SettingsForm());
            return 0;
        }

        if (cl.Files.Count == 0)
            return 0;

        // Explorer starts one process per selected file. Only the first process
        // (per size) does the work; the rest hand over their paths and exit.
        var coordinator = new BatchCoordinator(cl.MaxSize.Value);
        if (!coordinator.JoinOrLead(cl.Files))
            return 0; // handed our file(s) to the running instance

        using (coordinator)
        {
            ApplicationConfiguration.Initialize();
            var settings = Settings.Load();
            var form = new ProgressForm(coordinator, cl, settings);
            Application.Run(form);
        }
        return 0;
    }

    private static int RunInstall(CommandLine cl)
    {
        string message;
        bool ok = true;
        try
        {
            if (cl.Install)
            {
                ShellIntegration.Install(ShellIntegration.CurrentExePath);
                message = "Installed.\n\nRight-click a .jpg, .jpeg, .png or .gif file → Show more options → Shrink photo.\n" +
                          "Send To shortcuts were added too.\n\nMenu runs: " + ShellIntegration.CurrentExePath;
            }
            else
            {
                ShellIntegration.Uninstall();
                message = "Uninstalled. All PhotoShrink menu entries and Send To shortcuts were removed.";
            }
        }
        catch (Exception ex)
        {
            ok = false;
            message = (cl.Install ? "Install" : "Uninstall") + " failed: " + ex.Message;
        }

        if (!cl.Silent)
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show(message, "PhotoShrink", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        return ok ? 0 : 1;
    }
}
