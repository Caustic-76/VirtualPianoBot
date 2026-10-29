using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace VirtualPianoBot.Launcher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "VirtualPianoBot6Launcher", out var created);
        if (!created) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm(args));
    }
}

internal sealed class LauncherForm : Form
{
    readonly string AppRoot = AppContext.BaseDirectory;
    readonly string MidiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualPianoBot", "midi");
    readonly Button launch = new();
    readonly Label status = new();
    bool _checkingUpdates;

    public LauncherForm(string[] args)
    {
        Text = "VPB 6 Launcher";
        ClientSize = new Size(820, 500);
        MinimumSize = new Size(760, 460);
        BackColor = Color.FromArgb(8, 11, 16);
        ForeColor = Color.FromArgb(244, 247, 250);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label { Text = "VIRTUAL PIANO BOT 6", Font = new Font("Segoe UI Semibold", 28), ForeColor = Color.FromArgb(184,255,101), AutoSize = true, Location = new Point(42, 38) };
        var sub = new Label { Text = "6.0.1  •  PERFORMANCE STUDIO", ForeColor = Color.FromArgb(176,187,199), AutoSize = true, Location = new Point(46, 88) };
        var line = new Panel { BackColor = Color.FromArgb(38,51,66), Location = new Point(44, 120), Size = new Size(732, 1) };

        launch.Text = "LAUNCH VPB 6";
        launch.Font = new Font("Segoe UI Semibold", 14);
        launch.ForeColor = Color.FromArgb(8,11,16);
        launch.BackColor = Color.FromArgb(184,255,101);
        launch.FlatStyle = FlatStyle.Flat;
        launch.FlatAppearance.BorderSize = 0;
        launch.Location = new Point(44, 155);
        launch.Size = new Size(250, 64);
        launch.Click += (_, _) => LaunchApp();

        var library = MakeButton("OPEN MIDI LIBRARY", 44, 240, 250, 48, OpenMidi);
        var backup = MakeButton("BACK UP MIDI LIBRARY", 310, 240, 250, 48, BackupMidi);
        var updates = MakeButton("CHECK FOR UPDATES", 576, 240, 200, 48, CheckUpdates);
        var docs = MakeButton("OPEN APP FOLDER", 44, 300, 250, 48, () => Process.Start("explorer.exe", AppRoot));
        var settings = MakeButton("OPEN APP DATA", 310, 300, 250, 48, () => Process.Start("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualPianoBot")));
        var repair = MakeButton("BACK UP SETTINGS", 576, 300, 200, 48, BackupSettings);
        var updateSource = MakeButton("UPDATE SOURCE", 576, 350, 200, 40, ConfigureUpdateSource);

        status.Text = "Ready. Your MIDI library is protected in %APPDATA%\\VirtualPianoBot\\midi.";
        status.ForeColor = Color.FromArgb(113,128,145);
        status.AutoSize = false;
        status.Location = new Point(44, 410);
        status.Size = new Size(732, 50);

        Controls.AddRange([title, sub, line, launch, library, backup, updates, docs, settings, repair, updateSource, status]);
        if (args.Length > 0 && File.Exists(args[0])) { BeginInvoke(new Action(() => LaunchApp(args[0]))); }
    }

    Button MakeButton(string text, int x, int y, int w, int h, Action action)
    {
        var b = new Button { Text = text, Location = new Point(x,y), Size = new Size(w,h), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(21,28,38), ForeColor = Color.FromArgb(244,247,250) };
        b.FlatAppearance.BorderColor = Color.FromArgb(38,51,66); b.FlatAppearance.BorderSize = 1; b.Click += (_,_) => action(); return b;
    }

    void LaunchApp(string? midiPath = null)
    {
        var exe = Path.Combine(AppRoot, "runtime", "VPB6Studio.exe");
        if (!File.Exists(exe)) { MessageBox.Show("VPB6Studio.exe was not found. Please reinstall VPB 6.", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = AppRoot, UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(midiPath)) psi.ArgumentList.Add(midiPath);
        Process.Start(psi);
        status.Text = "VPB 6 Studio launched.";
    }

    void OpenMidi() { Directory.CreateDirectory(MidiRoot); Process.Start("explorer.exe", MidiRoot); }

    void BackupMidi()
    {
        Directory.CreateDirectory(MidiRoot);
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder outside your VPB MIDI library for the backup." };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        var midiRootFull = Path.GetFullPath(MidiRoot);
        var selectedFull = Path.GetFullPath(dialog.SelectedPath);
        var relativeToMidi = Path.GetRelativePath(midiRootFull, selectedFull);
        var selectedIsInsideLibrary = relativeToMidi == "."
            || (!Path.IsPathRooted(relativeToMidi)
                && relativeToMidi != ".."
                && !relativeToMidi.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relativeToMidi.StartsWith("../", StringComparison.Ordinal));
        if (selectedIsInsideLibrary)
        {
            MessageBox.Show("Choose a backup destination outside %APPDATA%\\VirtualPianoBot\\midi. This avoids copying a backup into itself.", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var target = Path.Combine(selectedFull, "VirtualPianoBot MIDI Backup", timestamp + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(target);
        var pending = new Stack<string>();
        var copied = 0;
        var failures = new List<string>();
        pending.Push(midiRootFull);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.GetFileName(directory)}: {ex.Message}");
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                        continue;
                    }

                    var extension = Path.GetExtension(entry);
                    if (!extension.Equals(".mid", StringComparison.OrdinalIgnoreCase)
                        && !extension.Equals(".midi", StringComparison.OrdinalIgnoreCase)) continue;

                    var relative = Path.GetRelativePath(midiRootFull, entry);
                    var destination = Path.GetFullPath(Path.Combine(target, relative));
                    var destinationRelative = Path.GetRelativePath(target, destination);
                    if (Path.IsPathRooted(destinationRelative)
                        || destinationRelative == ".."
                        || destinationRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || destinationRelative.StartsWith("../", StringComparison.Ordinal))
                    {
                        failures.Add($"Skipped unsafe path: {relative}");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(entry, destination, overwrite: false);
                    copied++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    failures.Add($"{Path.GetFileName(entry)}: {ex.Message}");
                }
            }
        }

        status.Text = $"MIDI backup finished: {copied} file(s) copied; {failures.Count} issue(s).";
        if (failures.Count == 0)
        {
            MessageBox.Show($"Backup complete. {copied} MIDI file(s) copied with folder structure preserved.\n\n{target}", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            var summary = string.Join(Environment.NewLine, failures.Take(8));
            MessageBox.Show($"Backup copied {copied} file(s), with {failures.Count} issue(s).\n\n{summary}\n\nBackup folder: {target}", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void BackupSettings()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualPianoBot");
        var settings = Path.Combine(data, "settings.json");
        if (!File.Exists(settings))
        {
            MessageBox.Show("No settings.json file exists yet, so there is nothing to back up.", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var backup = settings + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            File.Copy(settings, backup, overwrite: false);
            MessageBox.Show($"Settings backup saved to:\n{backup}\n\nNo settings or MIDI files were changed.", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Settings backup failed. The original file was left untouched.\n\n{ex.Message}", "VPB 6", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ConfigureUpdateSource()
    {
        var currentUrl = string.Empty;
        try { currentUrl = UpdateService.LoadLocalConfiguration(AppRoot).ManifestUrl; }
        catch { /* Allow this dialog to repair a malformed user update-settings.json. */ }

        using var dialog = new Form
        {
            Text = "Configure VPB Update Source",
            ClientSize = new Size(640, 190),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(8, 11, 16),
            ForeColor = Color.FromArgb(244, 247, 250),
            Font = new Font("Segoe UI", 10)
        };
        var explanation = new Label
        {
            Text = "Paste the HTTPS URL for your hosted update-manifest.json. Leave blank to remove the override and use the built-in source.",
            Location = new Point(20, 16),
            Size = new Size(600, 42),
            ForeColor = Color.FromArgb(176, 187, 199)
        };
        var urlInput = new TextBox
        {
            Text = currentUrl,
            Location = new Point(20, 64),
            Size = new Size(600, 30),
            Font = new Font("Segoe UI", 10)
        };
        var save = new Button
        {
            Text = "SAVE SOURCE",
            Location = new Point(380, 122),
            Size = new Size(120, 40),
            BackColor = Color.FromArgb(184, 255, 101),
            ForeColor = Color.FromArgb(8, 11, 16),
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        save.FlatAppearance.BorderSize = 0;
        var cancel = new Button
        {
            Text = "CANCEL",
            Location = new Point(510, 122),
            Size = new Size(110, 40),
            BackColor = Color.FromArgb(21, 28, 38),
            ForeColor = Color.FromArgb(244, 247, 250),
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(38, 51, 66);
        dialog.Controls.AddRange([explanation, urlInput, save, cancel]);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UpdateService.SaveManifestUrl(urlInput.Text);
            status.Text = string.IsNullOrWhiteSpace(urlInput.Text)
                ? "User update-source override removed."
                : "Update source saved to your user profile. Use CHECK FOR UPDATES to check it.";
            MessageBox.Show(
                string.IsNullOrWhiteSpace(urlInput.Text)
                    ? "The user-specific update source was removed. VPB will fall back to the built-in source, if one is configured."
                    : "Update source saved. The URL will be used by CHECK FOR UPDATES.\n\nIt must serve a valid update-manifest.json and matching package over HTTPS.",
                "VPB 6 Update Source", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The update source was not saved. Use an absolute HTTPS URL without embedded credentials.\n\n{ex.Message}",
                "VPB 6 Update Source", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    async void CheckUpdates()
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            status.Text = "Checking the configured secure update source...";
            var local = UpdateService.LoadLocalConfiguration(AppRoot);
            var update = await UpdateService.CheckAsync(local);

            if (update.LatestVersion.CompareTo(update.CurrentVersion) <= 0)
            {
                status.Text = $"VPB {local.Version} is up to date on the {local.Channel} channel.";
                MessageBox.Show($"You're up to date!\n\nInstalled version: {local.Version}\nLatest stable version: {update.Remote.Version}",
                    "VPB 6 Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var notes = string.IsNullOrWhiteSpace(update.Remote.ReleaseNotes)
                ? "No release notes were provided."
                : update.Remote.ReleaseNotes.Trim();
            if (notes.Length > 1800) notes = notes[..1800] + "\n…";
            var choice = MessageBox.Show(
                $"VPB {update.Remote.Version} is available.\n\nInstalled: {local.Version}\nNew version: {update.Remote.Version}\n\n{notes}\n\nDownload, verify and install this update now?\nYour MIDI library and settings will be kept outside the application files.",
                "VPB 6 Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
            {
                status.Text = $"Update {update.Remote.Version} is available when you're ready.";
                return;
            }

            status.Text = $"Downloading VPB {update.Remote.Version} and verifying its checksum...";
            var prepared = await UpdateService.DownloadAndPrepareAsync(update, AppRoot);
            UpdateService.StartApplyScript(prepared, AppRoot, Environment.ProcessId);
            MessageBox.Show(
                $"VPB {update.Remote.Version} has been downloaded and verified.\n\nThe updater will close the launcher, wait for any open Studio window to close, back up the current application files, install the update and relaunch VPB.\n\nYour MIDI library and settings are not part of the update package.\n\nUpdate log: {prepared.LogPath}",
                "VPB 6 Update Ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            status.Text = "The verified updater is applying the update.";
            Application.Exit();
        }
        catch (UpdateNotConfiguredException ex)
        {
            status.Text = "Online updates are not configured yet. VPB has not been changed.";
            MessageBox.Show(ex.Message + "\n\nThe update client is ready, but a hosted HTTPS manifest must be configured before online updates can run.",
                "VPB 6 Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.Text = "Update check failed safely. Your installed VPB files were not changed.";
            MessageBox.Show($"VPB couldn't complete the update. Your current installation and MIDI library have been left untouched.\n\n{ex.Message}",
                "VPB 6 Update Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _checkingUpdates = false;
        }
    }
}
