using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Hiragana Battle - Patch italiana")]
[assembly: AssemblyProduct("Hiragana Battle - Patch italiana")]
[assembly: AssemblyVersion("1.0.0.0")]

internal static class SteamGame
{
    internal const string Executable = "Learn Japanese To Survive - Hiragana Battle.exe";
    private const string AppManifest = "appmanifest_438270.acf";

    internal static bool IsGame(string folder)
    {
        return !String.IsNullOrWhiteSpace(folder) && folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
            File.Exists(Path.Combine(folder, Executable)) &&
            File.Exists(Path.Combine(folder, "www", "data", "System.json"));
    }

    private static string RegistryPath(RegistryKey hive, string subkey, string name)
    {
        using (var key = hive.OpenSubKey(subkey))
            return key == null ? null : key.GetValue(name) as string;
    }

    internal static IEnumerable<string> Libraries(string steam)
    {
        yield return steam;
        string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(list))
            yield break;
        var paths = Regex.Matches(File.ReadAllText(list),
            @"(?m)^\s*""(?:path|\d+)""\s*""((?:[A-Za-z]:|\\\\)[^""\r\n]+)""");
        foreach (Match match in paths)
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
    }

    internal static string GameInLibrary(string library)
    {
        string steamapps = Path.Combine(library, "steamapps");
        string manifest = Path.Combine(steamapps, AppManifest);
        if (File.Exists(manifest))
        {
            var match = Regex.Match(File.ReadAllText(manifest), @"""installdir""\s*""([^""\\/]+)""");
            if (match.Success && match.Groups[1].Value != "." && match.Groups[1].Value != "..")
            {
                string found = Path.Combine(steamapps, "common", match.Groups[1].Value);
                if (IsGame(found))
                    return found;
            }
        }
        string standard = Path.Combine(steamapps, "common", "HiraganaBattle");
        return IsGame(standard) ? standard : null;
    }

    internal static string Find()
    {
        if (IsGame(AppDomain.CurrentDomain.BaseDirectory))
            return AppDomain.CurrentDomain.BaseDirectory;
        var roots = new[] {
            RegistryPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            RegistryPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            RegistryPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")
        };
        foreach (string root in roots.Where(r => !String.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string library in Libraries(root))
            {
                string found = GameInLibrary(library);
                if (found != null)
                    return found;
            }
        }
        return null;
    }
}

internal static class Patch
{
    internal const string BackupName = "Backup-patch-italiana";

    private sealed class Change
    {
        internal string RelativePath;
        internal string Target;
        internal byte[] Before;
        internal byte[] After;
    }

    private static string Hash(byte[] bytes)
    {
        if (bytes == null)
            return "-";
        using (var algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] ApplyDelta(Stream delta, byte[] before, int size)
    {
        var output = new byte[size];
        using (var reader = new BinaryReader(delta))
        {
            int position = 0;
            while (position < output.Length)
            {
                int command = reader.ReadByte();
                int offset = command == 0 ? reader.ReadInt32() : 0;
                int length = reader.ReadInt32();
                if (length <= 0 || length > output.Length - position)
                    throw new InvalidDataException("La dimensione della patch non è valida.");
                if (command == 0 && before != null && offset >= 0 && offset <= before.Length - length)
                    Buffer.BlockCopy(before, offset, output, position, length);
                else if (command == 1)
                {
                    byte[] literal = reader.ReadBytes(length);
                    if (literal.Length != length) throw new EndOfStreamException();
                    Buffer.BlockCopy(literal, 0, output, position, length);
                }
                else
                    throw new InvalidDataException("La patch è danneggiata.");
                position += length;
            }
            if (delta.ReadByte() != -1)
                throw new InvalidDataException("La patch contiene dati in eccesso.");
        }
        return output;
    }

    private static List<Change> Prepare(string folder)
    {
        var changes = new List<Change>();
        var assembly = typeof(Patch).Assembly;
        using (var bundle = assembly.GetManifestResourceStream("HiraganaPatch"))
        using (var archive = new ZipArchive(bundle, ZipArchiveMode.Read))
        using (var manifest = new StreamReader(archive.GetEntry("manifest.tsv").Open()))
        {
            string line;
            while ((line = manifest.ReadLine()) != null)
            {
                string[] entry = line.Split('\t');
                string relative = entry[0].Replace('/', Path.DirectorySeparatorChar);
                string target = Path.GetFullPath(Path.Combine(folder, relative));
                if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Percorso non valido nella patch.");
                byte[] before = File.Exists(target) ? File.ReadAllBytes(target) : null;
                string hash = Hash(before);
                if (hash == entry[2])
                    continue;
                if (hash != entry[1])
                    throw new InvalidDataException("Versione del file non riconosciuta:\n" + relative +
                        "\n\nLa patch richiede i file originali della versione Steam supportata. " +
                        "Puoi ripristinarli con «Verifica integrità dei file di gioco» in Steam.");
                byte[] after = ApplyDelta(archive.GetEntry(entry[4]).Open(), before, Int32.Parse(entry[3]));
                if (Hash(after) != entry[2])
                    throw new InvalidDataException("Il controllo di integrità della patch è fallito.");
                changes.Add(new Change { RelativePath = relative, Target = target, Before = before, After = after });
            }
        }
        return changes;
    }

    private static void SaveOriginal(string backup, Change change)
    {
        if (change.Before == null)
            return;
        string path = Path.Combine(backup, change.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (File.Exists(path))
        {
            if (Hash(File.ReadAllBytes(path)) != Hash(change.Before))
                throw new IOException("La copia di sicurezza contiene una versione diversa di " + change.RelativePath);
            return;
        }
        AtomicWrite(path, change.Before, false);
    }

    private static void AtomicWrite(string target, byte[] data, bool replace)
    {
        string staged = Path.Combine(Path.GetDirectoryName(target), ".hiragana-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(staged, data);
            if (replace)
                File.Replace(staged, target, null);
            else
                File.Move(staged, target);
        }
        finally
        {
            if (File.Exists(staged))
                File.Delete(staged);
        }
    }

    internal static int Install(string folder)
    {
        folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!SteamGame.IsGame(folder))
            throw new InvalidDataException("Seleziona la cartella che contiene «" + SteamGame.Executable + "».");
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SteamGame.Executable));
        bool gameIsRunning = running.Length != 0;
        foreach (var process in running)
            process.Dispose();
        if (gameIsRunning)
            throw new IOException("Chiudi Hiragana Battle prima di installare la patch.");

        var changes = Prepare(folder);
        if (changes.Count == 0)
            return 0;
        string backup = Path.Combine(folder, BackupName);
        Directory.CreateDirectory(backup);
        string lockPath = Path.Combine(backup, ".install.lock");
        using (var installLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            foreach (var change in changes)
            {
                byte[] current = File.Exists(change.Target) ? File.ReadAllBytes(change.Target) : null;
                if (Hash(current) != Hash(change.Before))
                    throw new IOException("I file del gioco sono cambiati. Chiudi Steam se sta aggiornando il gioco e riprova.");
                SaveOriginal(backup, change);
            }
            var applied = new Stack<Change>();
            try
            {
                foreach (var change in changes)
                {
                    AtomicWrite(change.Target, change.After, change.Before != null);
                    applied.Push(change);
                }
            }
            catch (Exception error)
            {
                Exception rollbackError = null;
                foreach (var change in applied)
                {
                    try
                    {
                        if (change.Before == null)
                            File.Delete(change.Target);
                        else
                            AtomicWrite(change.Target, change.Before, true);
                    }
                    catch (Exception failure)
                    {
                        rollbackError = failure;
                    }
                }
                if (rollbackError != null)
                    throw new IOException("Installazione interrotta. Usa la copia dei file originali in:\n" + backup, error);
                throw;
            }
        }
        return changes.Count;
    }
}

internal sealed class Installer : Form
{
    private readonly TextBox path = new TextBox();
    private readonly Label status = new Label();

    internal Installer()
    {
        Text = "Hiragana Battle – Patch italiana";
        ClientSize = new Size(635, 230);
        MinimumSize = new Size(650, 260);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont;
        var title = new Label { Text = "Patch italiana per Hiragana Battle", AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Location = new Point(18, 16) };
        var instructions = new Label { Text = "Seleziona la cartella di installazione del gioco.", AutoSize = true,
            Location = new Point(20, 60) };
        path.SetBounds(20, 89, 485, 26);
        path.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        var browse = new Button { Text = "Sfoglia…", Location = new Point(515, 86), Size = new Size(100, 30),
            Anchor = AnchorStyles.Right | AnchorStyles.Top };
        browse.Click += Browse;
        status.SetBounds(20, 125, 595, 42);
        status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        path.TextChanged += delegate { ShowFolderStatus(); };
        var apply = new Button { Text = "Installa la patch", Location = new Point(460, 178), Size = new Size(155, 35),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        apply.Click += Install;
        AcceptButton = apply;
        Controls.AddRange(new Control[] { title, instructions, path, browse, status, apply });
        try { path.Text = SteamGame.Find() ?? ""; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        ShowFolderStatus();
    }

    private void ShowFolderStatus()
    {
        status.Text = SteamGame.IsGame(path.Text.Trim()) ? "Installazione del gioco individuata." :
            "Gioco non trovato automaticamente. Usa «Sfoglia…» per indicare la cartella.";
    }

    private void Browse(object sender, EventArgs args)
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Seleziona la cartella di Hiragana Battle.";
            dialog.ShowNewFolderButton = false;
            if (Directory.Exists(path.Text.Trim()))
                dialog.SelectedPath = path.Text.Trim();
            if (dialog.ShowDialog(this) == DialogResult.OK)
                path.Text = dialog.SelectedPath;
        }
    }

    private void Install(object sender, EventArgs args)
    {
        string folder = path.Text.Trim();
        if (!SteamGame.IsGame(folder))
        {
            MessageBox.Show(this, "Scegli la cartella di installazione del gioco.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Program.RunPatch(folder, true);
    }
}

internal static class Program
{
    private const string Title = "Hiragana Battle – Patch italiana";

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--install")
            RunPatch(args[1], false);
        else
            Application.Run(new Installer());
    }

    internal static void RunPatch(string folder, bool canElevate)
    {
        try
        {
            int count = Patch.Install(folder);
            MessageBox.Show(count == 0 ? "La patch italiana è già installata." :
                "Patch italiana installata. Ora puoi avviare il gioco da Steam.\n\nCopia dei file originali:\n" +
                Path.Combine(folder, Patch.BackupName), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (UnauthorizedAccessException error)
        {
            bool isAdmin;
            using (var identity = WindowsIdentity.GetCurrent())
                isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            if (!canElevate || isAdmin)
            {
                ShowError(error.Message);
                return;
            }
            try
            {
                string fullPath = Path.GetFullPath(folder).TrimEnd('\\');
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--install \"" + fullPath + "\"")
                    { UseShellExecute = true, Verb = "runas" });
            }
            catch (Win32Exception failure)
            {
                ShowError(failure.NativeErrorCode == 1223 ? "Installazione annullata." : failure.Message);
            }
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

