// ClutchKit - stream toolkit for TikTok LIVE Studio.
// ----------------------------------------------------------------------------------
// Counters (as many as you like, each with its own hotkeys and its own look) shown in one overlay,
// and gift actions: TikTok LIVE gifts press keys in your game (Actions.cs, TikTok.cs, ActionsPage.cs).
// Menu: Counters > Gift actions > TikTok LIVE > Overlay (how it gets on stream) > Settings (app options).
// Overlay modes:
//   - Link overlay: a "data:" link pasted once into LIVE Studio (Add source > Link). It loads the
//     overlay script from this app (http://127.0.0.1:<port>, this PC only), so every change made in
//     the app shows up in LIVE Studio automatically - no re-pasting.
//   - Capture window: a plain window to add with LIVE Studio's "Window capture".
// Install: running a downloaded ClutchKit.exe offers to install it for the current Windows user
// (%LOCALAPPDATA%\Programs\ClutchKit, Start menu shortcut, uninstall entry; no admin). Data then
// lives in %APPDATA%\ClutchKit. A copy next to counts.txt / settings.ini runs "portable" from there.
// "ClutchKit.exe /exit" asks a running copy to close itself properly (used when updating).
// "ClutchKit.exe /restart" waits for the running copy to close first (used by "Restart as administrator").
//
// Build (no extra tools needed): powershell -ExecutionPolicy Bypass -File build.ps1
// (compiles every file in src\ with the C# compiler that ships with Windows)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ClutchKit")]
[assembly: AssemblyDescription("ClutchKit - stream toolkit for TikTok LIVE Studio")]
[assembly: AssemblyProduct("ClutchKit")]
[assembly: AssemblyCompany("ClutchKit")]
[assembly: AssemblyCopyright("Copyright (c) 2026 ClutchKit")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: AssemblyInformationalVersion("3.0")]

static class Program
{
    public const string Name = "ClutchKit";
    public const string Version = "3.0";
    // Shared with the older "Death Counter" builds so old and new never run (and count) at the same time.
    public static string MutexName = "Local\\DeathCounterTikTok";

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDPIAware();
        // allow modern TLS for the TikTok LIVE connection (older .NET defaults don't include it)
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }    // TLS 1.2
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }   // TLS 1.3
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
        {
            Installer.Uninstall();
            return;
        }
        if (args.Length > 0 && args[0].Equals("/exit", StringComparison.OrdinalIgnoreCase))
        {
            // close a running ClutchKit properly (removes its tray icon) - used when updating
            try { EventWaitHandle.OpenExisting(App.ExitEventName).Set(); } catch { }
            return;
        }

        string exe = Application.ExecutablePath;
        string exeDir = Path.GetDirectoryName(exe);
        string dataDir;
        if (Installer.IsInstalledCopy(exe)) dataDir = Installer.DataDir;
        else if (Installer.IsPortableFolder(exeDir)) dataDir = exeDir;
        else
        {
            // A freshly downloaded copy: offer to install it.
            bool desktop;
            int choice = SetupForm.Ask(out desktop);
            if (choice == SetupForm.Cancel) return;
            if (choice == SetupForm.Install)
            {
                string error = Installer.Install(exe, desktop);
                if (error != null) { MessageBox.Show("ClutchKit couldn't be installed:\n\n" + error, "ClutchKit"); return; }
                try { Process.Start(Installer.InstalledExe); } catch { }
                return;
            }
            dataDir = exeDir;   // run without installing (portable)
        }

        bool createdNew;
        using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
        {
            if (!createdNew && args.Length > 0 && args[0].Equals("/restart", StringComparison.OrdinalIgnoreCase))
            {
                // restarted by the running copy (e.g. as administrator): wait for it to close
                try { createdNew = mutex.WaitOne(20000); }
                catch (AbandonedMutexException) { createdNew = true; }
            }
            if (!createdNew)
            {
                // Already running: ask the running copy to open its window.
                try { EventWaitHandle.OpenExisting(App.ShowEventName).Set(); }
                catch { MessageBox.Show("ClutchKit is already running (red icon by the clock).", "ClutchKit"); }
                return;
            }
            try
            {
                Directory.CreateDirectory(dataDir);
                Application.Run(new App(dataDir, false));
            }
            catch (Exception ex)
            {
                MessageBox.Show("ClutchKit couldn't start:\n\n" + ex.Message, "ClutchKit");
            }
        }
    }
}

// The app's icon (embedded in the exe), at any size.
public static class AppIcons
{
    static byte[] data;

    static byte[] Data
    {
        get
        {
            if (data == null)
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClutchKit.ico"))
                using (MemoryStream ms = new MemoryStream())
                {
                    if (s != null) s.CopyTo(ms);
                    data = ms.ToArray();
                }
            }
            return data;
        }
    }

    public static Icon Full()
    {
        try { return new Icon(new MemoryStream(Data)); } catch { return SystemIcons.Application; }
    }

    public static Icon Sized(int size)
    {
        try { return new Icon(new MemoryStream(Data), size, size); } catch { return SystemIcons.Application; }
    }

    public static Bitmap Image(int size)
    {
        using (Icon i = Sized(size)) return i.ToBitmap();
    }
}

// Per-user install: no admin rights needed, shows up in Windows Settings > Apps.
public static class Installer
{
    public static string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClutchKit");
    public static string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClutchKit");
    public static string StartMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    public static string DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string UninstallKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ClutchKit";

    public static string InstalledExe { get { return Path.Combine(InstallDir, "ClutchKit.exe"); } }
    public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

    public static bool IsInstalledCopy(string exe)
    {
        try { return string.Equals(Path.GetFullPath(exe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static bool IsPortableFolder(string dir)
    {
        return File.Exists(Path.Combine(dir, "counts.txt")) || File.Exists(Path.Combine(dir, "deaths.txt")) ||
               File.Exists(Path.Combine(dir, "settings.ini"));
    }

    public static bool IsRunning()
    {
        Mutex m;
        if (Mutex.TryOpenExisting(Program.MutexName, out m)) { m.Dispose(); return true; }
        return false;
    }

    public static string Install(string sourceExe, bool desktopShortcut)
    {
        try
        {
            if (IsRunning()) return "ClutchKit is running. Exit it first (right-click the red icon by the clock > Exit), then try again.";
            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(DataDir);
            if (!IsInstalledCopy(sourceExe)) File.Copy(sourceExe, InstalledExe, true);
            Directory.CreateDirectory(StartMenuDir);
            Shortcut(Path.Combine(StartMenuDir, "ClutchKit.lnk"), InstalledExe);
            string desk = Path.Combine(DesktopDir, "ClutchKit.lnk");
            if (desktopShortcut) { Directory.CreateDirectory(DesktopDir); Shortcut(desk, InstalledExe); }
            long sizeKb = new FileInfo(InstalledExe).Length / 1024 + 1;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "ClutchKit");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "ClutchKit");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" /uninstall");
                k.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static void Shortcut(string lnk, string target)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        Type t = sc.GetType();
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
        t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "ClutchKit - stream toolkit for TikTok LIVE Studio" });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        Marshal.FinalReleaseComObject(sc);
        Marshal.FinalReleaseComObject(shell);
    }

    public static void Uninstall()
    {
        if (MessageBox.Show("Uninstall ClutchKit from this PC?", "ClutchKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        // wait a moment in case ClutchKit is just closing (when uninstalling from inside the app)
        for (int i = 0; i < 25 && IsRunning(); i++) Thread.Sleep(200);
        if (IsRunning())
        {
            MessageBox.Show("ClutchKit is still running. Exit it first (right-click the red icon by the clock > Exit), then uninstall again.", "ClutchKit");
            return;
        }
        bool deleteData = Directory.Exists(DataDir) &&
            MessageBox.Show("Also delete your saved counts and settings?", "ClutchKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        try { File.Delete(Path.Combine(StartMenuDir, "ClutchKit.lnk")); } catch { }
        try { File.Delete(Path.Combine(DesktopDir, "ClutchKit.lnk")); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        if (deleteData) { try { Directory.Delete(DataDir, true); } catch { } }
        // the program folder is removed right after this window closes (a running exe can't delete itself)
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }
        catch { }
        MessageBox.Show("ClutchKit has been uninstalled.", "ClutchKit");
    }
}

// First-run window for a freshly downloaded ClutchKit.exe.
public class SetupForm : Form
{
    public const int Cancel = 0, Install = 1, Portable = 2;
    int choice = Cancel;
    readonly CheckBox desktopBox;

    public static int Ask(out bool desktopShortcut)
    {
        using (SetupForm f = new SetupForm())
        {
            f.ShowDialog();
            desktopShortcut = f.desktopBox.Checked;
            return f.choice;
        }
    }

    SetupForm()
    {
        Text = "ClutchKit Setup";
        Icon = AppIcons.Full();
        BackColor = UI.Bg;
        ForeColor = UI.Text;
        Font = UI.F(10f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(UI.D(560), UI.D(300));

        PictureBox logo = new PictureBox();
        logo.Image = AppIcons.Image(UI.D(64));
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.Location = new Point(UI.D(32), UI.D(32));
        logo.Size = new Size(UI.D(64), UI.D(64));
        Controls.Add(logo);

        UI.Lbl(this, (Installer.IsInstalled ? "Update ClutchKit to " : "Install ClutchKit ") + Program.Version, 116, 34, 420, 16f, UI.Text, FontStyle.Bold);
        UI.Para(this, "Stream toolkit for TikTok LIVE Studio - counters with hotkeys, shown live on stream.", 118, 76, 420, 24);
        UI.Para(this, "Installs for your Windows user only (no admin needed): adds ClutchKit to the Start menu and to " +
                      "Windows Settings > Apps, where you can uninstall it any time." +
                      (Installer.IsInstalled ? " Your saved counts and settings are kept." : ""), 118, 108, 420, 64);
        desktopBox = new CheckBox();
        desktopBox.Text = "Also put a shortcut on the desktop";
        desktopBox.Checked = true;
        desktopBox.ForeColor = UI.Text;
        desktopBox.Location = new Point(UI.D(118), UI.D(180));
        desktopBox.AutoSize = true;
        Controls.Add(desktopBox);

        Button install = UI.Btn(this, Installer.IsInstalled ? "Update" : "Install", 118, 228, 150, 42, true);
        Button portable = UI.Btn(this, "Run without installing", 280, 228, 220, 42, false);
        install.Click += delegate { choice = Install; Close(); };
        portable.Click += delegate { choice = Portable; Close(); };
        AcceptButton = install;
    }
}

// ============================================================================================
// Settings
// ============================================================================================

public struct Hotkey
{
    public const int Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
    public int Mods;
    public int Vk;

    public Hotkey(int mods, int vk) { Mods = mods; Vk = vk; }
    public bool IsSet { get { return Vk != 0; } }
    public bool Same(Hotkey o) { return Vk != 0 && Vk == o.Vk && Mods == o.Mods; }

    public override string ToString()
    {
        if (Vk == 0) return "No key";
        List<string> parts = new List<string>();
        if ((Mods & Ctrl) != 0) parts.Add("Ctrl");
        if ((Mods & Alt) != 0) parts.Add("Alt");
        if ((Mods & Shift) != 0) parts.Add("Shift");
        if ((Mods & Win) != 0) parts.Add("Win");
        parts.Add(KeyName(Vk));
        return string.Join(" + ", parts.ToArray());
    }

    public static string KeyName(int vk)
    {
        Keys k = (Keys)vk;
        switch (k)
        {
            case Keys.Add: return "Num +";
            case Keys.Subtract: return "Num -";
            case Keys.Multiply: return "Num *";
            case Keys.Divide: return "Num /";
            case Keys.Decimal: return "Num .";
            case Keys.Oemplus: return "=";
            case Keys.OemMinus: return "-";
            case Keys.Oemcomma: return ",";
            case Keys.OemPeriod: return ".";
            case Keys.Next: return "Page Down";
            case Keys.Prior: return "Page Up";
            case Keys.Return: return "Enter";
        }
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (vk - (int)Keys.NumPad0);
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)vk).ToString();
        return k.ToString();
    }

    public string Serialize() { return Mods + ":" + Vk; }

    public static Hotkey Parse(string s, Hotkey fallback)
    {
        string[] p = (s ?? "").Split(':');
        int m, v;
        if (p.Length == 2 && int.TryParse(p[0], out m) && int.TryParse(p[1], out v) && v >= 0 && v < 256)
            return new Hotkey(m & 15, v);
        return fallback;
    }
}

// How one counter looks and animates on stream.
public class Style
{
    public string Font = "Segoe UI Black";
    public bool Bold = true, Italic = false, Shadow = true;
    public Color TextColor = Color.White, OutlineColor = Color.Black;
    public int OutlineWidth = 6;
    public string Anim = "pop";
    public int AnimMs = 400;
    public Color FlashColor = Color.FromArgb(255, 59, 59);

    public Style Clone() { return (Style)MemberwiseClone(); }

    // keys in kv are lower-case
    public void Read(Dictionary<string, string> kv, string pre)
    {
        string v;
        if (kv.TryGetValue(pre + "font", out v)) Font = Settings.CleanFont(v);
        if (kv.TryGetValue(pre + "bold", out v)) Bold = Settings.ParseBool(v, Bold);
        if (kv.TryGetValue(pre + "italic", out v)) Italic = Settings.ParseBool(v, Italic);
        if (kv.TryGetValue(pre + "shadow", out v)) Shadow = Settings.ParseBool(v, Shadow);
        if (kv.TryGetValue(pre + "textcolor", out v)) TextColor = Settings.ParseColor(v, TextColor);
        if (kv.TryGetValue(pre + "outlinecolor", out v)) OutlineColor = Settings.ParseColor(v, OutlineColor);
        if (kv.TryGetValue(pre + "outlinewidth", out v)) OutlineWidth = Settings.ParseInt(v, OutlineWidth, 0, 20);
        if (kv.TryGetValue(pre + "animation", out v)) Anim = v.Trim();
        if (kv.TryGetValue(pre + "animationms", out v)) AnimMs = Settings.ParseInt(v, AnimMs, 100, 3000);
        if (kv.TryGetValue(pre + "flashcolor", out v)) FlashColor = Settings.ParseColor(v, FlashColor);
    }

    public void Write(StringBuilder b, string pre)
    {
        b.AppendLine(pre + "font=" + Font);
        b.AppendLine(pre + "bold=" + Bold);
        b.AppendLine(pre + "italic=" + Italic);
        b.AppendLine(pre + "shadow=" + Shadow);
        b.AppendLine(pre + "textColor=" + Settings.ToHex(TextColor));
        b.AppendLine(pre + "outlineColor=" + Settings.ToHex(OutlineColor));
        b.AppendLine(pre + "outlineWidth=" + OutlineWidth);
        b.AppendLine(pre + "animation=" + Anim);
        b.AppendLine(pre + "animationMs=" + AnimMs);
        b.AppendLine(pre + "flashColor=" + Settings.ToHex(FlashColor));
    }
}

// One counter: what it's called on stream, its icon, its hotkeys, its look and its number.
public class Counter
{
    public static readonly string[] ActionNames = { "+1", "-1", "Reset" };

    public int Id;
    public string Name = "Deaths:";
    public string Icon = "none";
    public bool Visible = true;
    public Hotkey[] Keys = { new Hotkey(0, 0), new Hotkey(0, 0), new Hotkey(0, 0) };
    public Style Style = new Style();

    // live state (not saved in settings.ini)
    public int Count;
    public int Seq;
    public DateTime AnimStart = DateTime.MinValue;

    public string DisplayName { get { string n = Name.Trim().TrimEnd(':').Trim(); return n.Length > 0 ? n : "Counter " + Id; } }
}

public class Settings
{
    public const int MaxCounters = 8;

    public List<Counter> Counters = new List<Counter>();
    public string Mode = "link";                 // "link" or "window"
    public Color WindowBg = Color.Black;
    public Rectangle CaptureBounds = Rectangle.Empty;
    public bool StartInTray = false, TrayHintShown = false;
    public int Port = 8787;

    // gift actions
    public const int MaxRules = 30;
    public List<GiftRule> Rules = new List<GiftRule>();
    public bool ActionsOn = true;
    public Hotkey PauseKey = new Hotkey(0, 0);   // turns gift actions on/off while you play
    public string OnlyProcess = "";              // only press keys while this program is in front ("" = any)
    public int GapMs = 80;                       // pause between actions
    public int MaxQueue = 50;                    // actions waiting at most; more are skipped
    // TikTok LIVE connection
    public string TikTokUser = "", TikTokKey = "";
    public bool AutoConnect = false;

    public static string ToHex(Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }
    public static string CleanFont(string v) { return v.Trim().Replace("'", "").Replace("\"", ""); }

    public static Color ParseColor(string v, Color fallback)
    {
        try { return ColorTranslator.FromHtml(v.Trim()); } catch { return fallback; }
    }

    public static bool ParseBool(string v, bool fallback)
    {
        v = v.Trim().ToLowerInvariant();
        if (v == "true" || v == "1" || v == "yes") return true;
        if (v == "false" || v == "0" || v == "no") return false;
        return fallback;
    }

    public static int ParseInt(string v, int fallback, int min, int max)
    {
        int n;
        return int.TryParse(v.Trim(), out n) ? Math.Max(min, Math.Min(max, n)) : fallback;
    }

    public static Settings Load(string path)
    {
        Settings s = new Settings();
        Dictionary<string, string> kv = new Dictionary<string, string>();
        if (File.Exists(path))
        {
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    kv[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1);
                }
            }
            catch { }
        }
        string v;
        if (kv.TryGetValue("mode", out v)) s.Mode = v.Trim() == "window" ? "window" : "link";
        if (kv.TryGetValue("windowbackground", out v)) s.WindowBg = ParseColor(v, s.WindowBg);
        if (kv.TryGetValue("startintray", out v)) s.StartInTray = ParseBool(v, s.StartInTray);
        if (kv.TryGetValue("trayhintshown", out v)) s.TrayHintShown = ParseBool(v, s.TrayHintShown);
        if (kv.TryGetValue("port", out v)) s.Port = ParseInt(v, s.Port, 1024, 65535);
        if (kv.TryGetValue("capturewindow", out v))
        {
            string[] p = v.Split(',');
            int x, y, w, h;
            if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) &&
                int.TryParse(p[2], out w) && int.TryParse(p[3], out h) && w > 50 && h > 30)
                s.CaptureBounds = new Rectangle(x, y, w, h);
        }

        if (kv.TryGetValue("actions.on", out v)) s.ActionsOn = ParseBool(v, true);
        if (kv.TryGetValue("actions.pausekey", out v)) s.PauseKey = Hotkey.Parse(v.Trim(), s.PauseKey);
        if (kv.TryGetValue("actions.onlyprogram", out v)) s.OnlyProcess = KeySender.CleanProcessName(v);
        if (kv.TryGetValue("actions.gapms", out v)) s.GapMs = ParseInt(v, s.GapMs, 0, 5000);
        if (kv.TryGetValue("actions.maxqueue", out v)) s.MaxQueue = ParseInt(v, s.MaxQueue, 1, 1000);
        if (kv.TryGetValue("tiktok.user", out v)) s.TikTokUser = v.Trim();
        if (kv.TryGetValue("tiktok.apikey", out v)) s.TikTokKey = v.Trim();
        if (kv.TryGetValue("tiktok.autoconnect", out v)) s.AutoConnect = ParseBool(v, false);
        if (kv.TryGetValue("rules", out v))
        {
            foreach (string part in v.Split(','))
            {
                int id;
                if (!int.TryParse(part.Trim(), out id) || id <= 0 || s.FindRule(id) != null) continue;
                s.Rules.Add(GiftRule.Read(kv, id));
                if (s.Rules.Count >= MaxRules) break;
            }
        }

        // the look from older versions (one look for everything) becomes every counter's starting look
        Style shared = new Style();
        shared.Read(kv, "");

        if (kv.TryGetValue("counters", out v))
        {
            foreach (string part in v.Split(','))
            {
                int id;
                if (!int.TryParse(part.Trim(), out id) || id <= 0 || s.Find(id) != null) continue;
                Counter c = new Counter();
                c.Id = id;
                string pre = "counter." + id + ".";
                if (kv.TryGetValue(pre + "name", out v)) c.Name = v.Trim();
                if (kv.TryGetValue(pre + "icon", out v)) c.Icon = v.Trim();
                if (kv.TryGetValue(pre + "show", out v)) c.Visible = ParseBool(v, true);
                if (kv.TryGetValue(pre + "add", out v)) c.Keys[0] = Hotkey.Parse(v.Trim(), c.Keys[0]);
                if (kv.TryGetValue(pre + "subtract", out v)) c.Keys[1] = Hotkey.Parse(v.Trim(), c.Keys[1]);
                if (kv.TryGetValue(pre + "reset", out v)) c.Keys[2] = Hotkey.Parse(v.Trim(), c.Keys[2]);
                c.Style = shared.Clone();
                c.Style.Read(kv, pre);
                s.Counters.Add(c);
                if (s.Counters.Count >= MaxCounters) break;
            }
        }
        if (s.Counters.Count == 0)
        {
            // first run, or settings from the single-counter version: turn them into counter 1
            Counter c = new Counter();
            c.Id = 1;
            c.Name = "Deaths:";
            c.Icon = "skull";
            c.Keys = new[] { new Hotkey(0, (int)Keys.Add), new Hotkey(0, (int)Keys.Subtract), new Hotkey(0, (int)Keys.Multiply) };
            if (kv.TryGetValue("label", out v)) c.Name = v.Trim();
            if (kv.TryGetValue("icon", out v)) c.Icon = v.Trim();
            if (kv.TryGetValue("hotkey.add", out v)) c.Keys[0] = Hotkey.Parse(v.Trim(), c.Keys[0]);
            if (kv.TryGetValue("hotkey.subtract", out v)) c.Keys[1] = Hotkey.Parse(v.Trim(), c.Keys[1]);
            if (kv.TryGetValue("hotkey.reset", out v)) c.Keys[2] = Hotkey.Parse(v.Trim(), c.Keys[2]);
            c.Style = shared.Clone();
            s.Counters.Add(c);
        }
        return s;
    }

    public Counter Find(int id)
    {
        foreach (Counter c in Counters) if (c.Id == id) return c;
        return null;
    }

    public GiftRule FindRule(int id)
    {
        foreach (GiftRule r in Rules) if (r.Id == id) return r;
        return null;
    }

    public void Save(string path)
    {
        StringBuilder b = new StringBuilder();
        b.AppendLine("# ClutchKit settings - change them in the app (double-click the red icon by the clock).");
        List<string> ids = new List<string>();
        foreach (Counter c in Counters) ids.Add(c.Id.ToString());
        b.AppendLine("counters=" + string.Join(",", ids.ToArray()));
        foreach (Counter c in Counters)
        {
            string pre = "counter." + c.Id + ".";
            b.AppendLine(pre + "name=" + c.Name);
            b.AppendLine(pre + "icon=" + c.Icon);
            b.AppendLine(pre + "show=" + c.Visible);
            b.AppendLine(pre + "add=" + c.Keys[0].Serialize());
            b.AppendLine(pre + "subtract=" + c.Keys[1].Serialize());
            b.AppendLine(pre + "reset=" + c.Keys[2].Serialize());
            c.Style.Write(b, pre);
        }
        b.AppendLine("mode=" + Mode);
        b.AppendLine("windowBackground=" + ToHex(WindowBg));
        if (!CaptureBounds.IsEmpty)
            b.AppendLine("captureWindow=" + CaptureBounds.X + "," + CaptureBounds.Y + "," + CaptureBounds.Width + "," + CaptureBounds.Height);
        b.AppendLine("startInTray=" + StartInTray);
        b.AppendLine("trayHintShown=" + TrayHintShown);
        b.AppendLine("port=" + Port);
        b.AppendLine("actions.on=" + ActionsOn);
        b.AppendLine("actions.pauseKey=" + PauseKey.Serialize());
        b.AppendLine("actions.onlyProgram=" + OnlyProcess);
        b.AppendLine("actions.gapMs=" + GapMs);
        b.AppendLine("actions.maxQueue=" + MaxQueue);
        List<string> rids = new List<string>();
        foreach (GiftRule r in Rules) rids.Add(r.Id.ToString());
        b.AppendLine("rules=" + string.Join(",", rids.ToArray()));
        foreach (GiftRule r in Rules) r.Write(b);
        b.AppendLine("tiktok.user=" + TikTokUser);
        b.AppendLine("tiktok.apiKey=" + TikTokKey);
        b.AppendLine("tiktok.autoConnect=" + AutoConnect);
        try { File.WriteAllText(path, b.ToString()); } catch { }
    }
}

// ============================================================================================
// The app: counters, hotkeys, tray icon, overlay server, windows
// ============================================================================================

public class App : ApplicationContext
{
    public const string ShowEventName = "Local\\ClutchKit.Show";
    public const string ExitEventName = "Local\\ClutchKit.Exit";

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string name);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    public readonly string Folder, CountsFile, SettingsFile;
    public Settings S;
    public readonly Icon AppIcon;
    public bool CapturingHotkey;
    public string ServerError;

    public event EventHandler CountChanged, SettingsChanged, Animated, ActionsToggled;
    public event Action<string> Logged;   // a new line in the activity log (UI thread)

    public GiftEngine Engine;
    public TikTokLive TikTok;
    readonly List<string> log = new List<string>();

    volatile string stateJson = "{}";
    volatile int firstCount;
    readonly bool testMode;
    readonly Control marshal = new Control();
    readonly bool[] held = new bool[256];
    readonly LowLevelKeyboardProc hookProc;   // kept in a field so it is never garbage-collected
    IntPtr hook;
    NotifyIcon tray;
    ContextMenuStrip menu;
    readonly List<ToolStripItem> countItems = new List<ToolStripItem>();
    ToolStripMenuItem captureItem;
    MainForm main;
    CaptureForm capture;
    OverlayServer server;

    public string StateJson { get { return stateJson; } }
    public int FirstCount { get { return firstCount; } }
    public bool KeysOk { get { return hook != IntPtr.Zero; } }
    public MainForm Main { get { return main; } }
    public CaptureForm Capture { get { return capture; } }

    public App(string folder, bool testMode)
    {
        this.testMode = testMode;
        Folder = folder;
        CountsFile = Path.Combine(folder, "counts.txt");
        SettingsFile = Path.Combine(folder, "settings.ini");
        IntPtr forceHandle = marshal.Handle;   // lets key presses be handed to this thread
        AppIcon = AppIcons.Full();

        S = Settings.Load(SettingsFile);
        LoadCounts();
        S.Save(SettingsFile);
        SaveCounts();
        BuildState();

        server = new OverlayServer(S.Port, this);
        ServerError = server.Start();

        Engine = new GiftEngine(this);
        TikTok = new TikTokLive(this);

        BuildTray();
        hookProc = OnKey;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
        if (!testMode) StartListeners();
        if (!testMode && S.AutoConnect && S.TikTokUser.Length > 0 && S.TikTokKey.Length > 0) TikTok.Start(S.TikTokUser, S.TikTokKey);

        if (S.Mode == "window") ShowCapture();
        if (!S.StartInTray) ShowMain();
        else Notify("ClutchKit is running", "Double-click the red icon by the clock for settings.", ToolTipIcon.None);

        if (ServerError != null)
            Notify("Overlay can't start", "Port " + S.Port + " is already in use. Close other copies of ClutchKit and start it again.", ToolTipIcon.Error);
        if (!KeysOk)
            Notify("Hotkeys couldn't be set up", "Exit and start ClutchKit again.", ToolTipIcon.Error);
    }

    // ---------------- counts ----------------

    void LoadCounts()
    {
        bool loaded = false;
        try
        {
            if (File.Exists(CountsFile))
            {
                foreach (string line in File.ReadAllLines(CountsFile))
                {
                    int eq = line.IndexOf('='), id, n;
                    if (eq < 0 || !int.TryParse(line.Substring(0, eq).Trim(), out id) || !int.TryParse(line.Substring(eq + 1).Trim(), out n)) continue;
                    Counter c = S.Find(id);
                    if (c != null) c.Count = Math.Max(0, n);
                }
                loaded = true;
            }
        }
        catch { }
        if (!loaded)
        {
            // count from the single-counter version (deaths.txt) goes to the first counter
            string old = Path.Combine(Folder, "deaths.txt");
            try
            {
                if (File.Exists(old) && S.Counters.Count > 0)
                {
                    S.Counters[0].Count = Math.Max(0, int.Parse(File.ReadAllText(old).Trim()));
                    SaveCounts();
                    if (File.Exists(CountsFile)) File.Delete(old);
                }
            }
            catch { }
        }
    }

    void SaveCounts()
    {
        StringBuilder b = new StringBuilder();
        foreach (Counter c in S.Counters) b.Append(c.Id).Append('=').Append(c.Count).Append("\r\n");
        try { File.WriteAllText(CountsFile, b.ToString()); } catch { }
    }

    public List<Counter> VisibleCounters()
    {
        List<Counter> list = new List<Counter>();
        foreach (Counter c in S.Counters) if (c.Visible) list.Add(c);
        return list;
    }

    public double Progress(Counter c)
    {
        double t = (DateTime.Now - c.AnimStart).TotalMilliseconds / Math.Max(1, c.Style.AnimMs);
        return t >= 0 && t < 1 ? t : -1;
    }

    public int LongestAnimMs()
    {
        int ms = 0;
        foreach (Counter c in S.Counters) ms = Math.Max(ms, c.Style.AnimMs);
        return ms;
    }

    public void SetCount(Counter c, int value)
    {
        value = Math.Max(0, value);
        if (c == null || value == c.Count) return;
        c.Count = value;
        c.Seq++;
        c.AnimStart = DateTime.Now;
        SaveCounts();
        BuildState();
        UpdateTray();
        Raise(CountChanged);
        Raise(Animated);
    }

    public void Add(Counter c, int delta) { if (c != null) SetCount(c, c.Count + delta); }
    public void Reset(Counter c) { SetCount(c, 0); }

    public Counter AddCounter()
    {
        if (S.Counters.Count >= Settings.MaxCounters) return null;
        Counter c = new Counter();
        foreach (Counter o in S.Counters) c.Id = Math.Max(c.Id, o.Id);
        c.Id++;
        c.Name = "Counter " + (S.Counters.Count + 1) + ":";
        if (S.Counters.Count > 0) c.Style = S.Counters[0].Style.Clone();   // start with the same look as the first one
        S.Counters.Add(c);
        SaveCounts();
        ApplySettings();
        return c;
    }

    public void RemoveCounter(Counter c)
    {
        if (S.Counters.Count <= 1) return;
        S.Counters.Remove(c);
        SaveCounts();
        ApplySettings();
        Raise(CountChanged);
    }

    // Plays the animation once - for one counter, or all of them (null).
    public void PreviewAnimation(Counter only)
    {
        foreach (Counter c in S.Counters)
        {
            if (only != null && c != only) continue;
            c.Seq++;
            c.AnimStart = DateTime.Now;
        }
        BuildState();
        Raise(Animated);
    }

    public void ApplySettings()
    {
        S.Save(SettingsFile);
        BuildState();
        UpdateTray();
        Raise(SettingsChanged);
    }

    // ---------------- gift actions ----------------

    // Runs code on the UI thread (safe to call from any thread).
    public void Ui(MethodInvoker m)
    {
        try { marshal.BeginInvoke(m); } catch { }
    }

    // Adds a line to the activity log (any thread).
    public void Log(string text)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        Ui(delegate
        {
            log.Add(line);
            if (log.Count > 300) log.RemoveRange(0, log.Count - 300);
            if (Logged != null) Logged(line);
        });
    }

    public List<string> LogLines { get { return new List<string>(log); } }

    public void SetActionsOn(bool on)
    {
        if (S.ActionsOn == on) return;
        S.ActionsOn = on;
        if (!on) Engine.Clear();
        S.Save(SettingsFile);
        Log(on ? "Gift actions turned on." : "Gift actions paused - gifts won't press any keys.");
        Raise(ActionsToggled);
    }

    public GiftRule AddRule()
    {
        if (S.Rules.Count >= Settings.MaxRules) return null;
        GiftRule r = new GiftRule();
        foreach (GiftRule o in S.Rules) r.Id = Math.Max(r.Id, o.Id);
        r.Id++;
        S.Rules.Add(r);
        ApplySettings();
        return r;
    }

    public void RemoveRule(GiftRule r)
    {
        S.Rules.Remove(r);
        ApplySettings();
    }

    // Games that run as administrator ignore keys from normal programs (a Windows rule), so
    // ClutchKit has to run as administrator too. Starts an elevated copy, then closes this one.
    public void RestartAsAdmin()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/restart");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }
        catch { return; }   // the Windows prompt was cancelled
        ExitApp();
    }

    // Who already uses this key (for the "already used" message), or null.
    // exceptAction -2 = the gift actions on/off key.
    public string KeyOwner(Hotkey hk, Counter exceptCounter, int exceptAction)
    {
        if (exceptAction != -2 && S.PauseKey.Same(hk)) return "Gift actions on/off";
        foreach (Counter c in S.Counters)
            for (int a = 0; a < 3; a++)
                if (!(c == exceptCounter && a == exceptAction) && c.Keys[a].Same(hk))
                    return c.DisplayName + " " + Counter.ActionNames[a];
        return null;
    }

    void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

    static string Js(string s)
    {
        StringBuilder b = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            if (c == '"') b.Append("\\\"");
            else if (c == '\\') b.Append("\\\\");
            else if (c < 32 || c > 126) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    void BuildState()
    {
        StringBuilder b = new StringBuilder("{");
        b.Append("\"overlayVersion\":").Append(Js(Overlay.Version));
        b.Append(",\"counters\":[");
        bool first = true;
        foreach (Counter c in S.Counters)
        {
            if (!c.Visible) continue;
            if (!first) b.Append(',');
            first = false;
            Style st = c.Style;
            b.Append("{\"id\":").Append(c.Id)
             .Append(",\"label\":").Append(Js(c.Name))
             .Append(",\"icon\":").Append(Js(c.Icon))
             .Append(",\"count\":").Append(c.Count)
             .Append(",\"seq\":").Append(c.Seq)
             .Append(",\"font\":").Append(Js(Settings.CleanFont(st.Font)))
             .Append(",\"bold\":").Append(st.Bold ? "true" : "false")
             .Append(",\"italic\":").Append(st.Italic ? "true" : "false")
             .Append(",\"shadow\":").Append(st.Shadow ? "true" : "false")
             .Append(",\"color\":").Append(Js(Settings.ToHex(st.TextColor)))
             .Append(",\"outline\":").Append(Js(Settings.ToHex(st.OutlineColor)))
             .Append(",\"outlineWidth\":").Append(st.OutlineWidth)
             .Append(",\"anim\":").Append(Js(st.Anim))
             .Append(",\"animMs\":").Append(st.AnimMs)
             .Append(",\"flash\":").Append(Js(Settings.ToHex(st.FlashColor)))
             .Append('}');
        }
        b.Append("]}");
        stateJson = b.ToString();
        firstCount = S.Counters.Count > 0 ? S.Counters[0].Count : 0;
    }

    // ---------------- hotkeys ----------------

    bool Down(params int[] vks)
    {
        foreach (int v in vks) if (held[v]) return true;
        return false;
    }

    int CurrentMods()
    {
        int m = 0;
        if (Down(0x11, 0xA2, 0xA3)) m |= Hotkey.Ctrl;
        if (Down(0x12, 0xA4, 0xA5)) m |= Hotkey.Alt;
        if (Down(0x10, 0xA0, 0xA1)) m |= Hotkey.Shift;
        if (Down(0x5B, 0x5C)) m |= Hotkey.Win;
        return m;
    }

    static bool IsModifier(int vk)
    {
        return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
    }

    // A low-level keyboard hook only watches key presses (it doesn't take them away from
    // other apps), which is how OBS hotkeys behave too.
    IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam) & 0xFF;
            int m = (int)wParam;
            // keys pressed by ClutchKit's own gift actions (KBDLLHOOKSTRUCT.dwExtraInfo)
            bool own = Marshal.ReadIntPtr(lParam, 16) == KeySender.Tag;
            if (m == WM_KEYDOWN || m == WM_SYSKEYDOWN)
            {
                if (!held[vk])               // ignore auto-repeat while the key is held down
                {
                    held[vk] = true;
                    if (!IsModifier(vk)) HandleKey(vk, CurrentMods(), own);
                }
            }
            else if (m == WM_KEYUP || m == WM_SYSKEYUP)
            {
                held[vk] = false;
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    void HandleKey(int vk, int mods, bool own)
    {
        if (CapturingHotkey) return;
        Hotkey pressed = new Hotkey(mods, vk);
        // a gift can press a counter's key (e.g. a Rose adds a death), but never the on/off key
        if (!own && pressed.Same(S.PauseKey)) marshal.BeginInvoke((MethodInvoker)delegate { SetActionsOn(!S.ActionsOn); });
        foreach (Counter c in S.Counters)
        {
            Counter target = c;
            if (pressed.Same(c.Keys[0])) marshal.BeginInvoke((MethodInvoker)delegate { Add(target, 1); });
            else if (pressed.Same(c.Keys[1])) marshal.BeginInvoke((MethodInvoker)delegate { Add(target, -1); });
            else if (pressed.Same(c.Keys[2])) marshal.BeginInvoke((MethodInvoker)delegate { Reset(target); });
        }
    }

    // ---------------- windows & tray ----------------

    // "Show" opens the window when ClutchKit is started a second time; "Exit" closes it properly when updating.
    void StartListeners()
    {
        EventWaitHandle exitEv = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        Thread et = new Thread(delegate ()
        {
            exitEv.WaitOne();
            try { marshal.BeginInvoke((MethodInvoker)ExitApp); } catch { }
        });
        et.IsBackground = true;
        et.Start();

        EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        Thread t = new Thread(delegate ()
        {
            while (true)
            {
                ev.WaitOne();
                try { marshal.BeginInvoke((MethodInvoker)ShowMain); } catch { }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    public void ShowMain()
    {
        if (main == null || main.IsDisposed) main = new MainForm(this);
        main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
    }

    public void MainHidden()
    {
        if (!S.TrayHintShown)
        {
            S.TrayHintShown = true;
            S.Save(SettingsFile);
            Notify("Still counting", "ClutchKit keeps running here by the clock. Double-click the red icon to open it, right-click to exit.", ToolTipIcon.Info);
        }
    }

    public void ShowCapture()
    {
        if (capture == null || capture.IsDisposed) capture = new CaptureForm(this);
        capture.Show();
        capture.Activate();
    }

    public void HideCapture()
    {
        if (capture != null && !capture.IsDisposed) capture.Hide();
    }

    public void SetMode(string mode)
    {
        S.Mode = mode;
        if (mode == "window") ShowCapture(); else HideCapture();
        ApplySettings();
    }

    public void CopyLink()
    {
        try { Clipboard.SetText(Overlay.Link(S.Port)); } catch { }
    }

    public void OpenFolder()
    {
        try { Process.Start("explorer.exe", "\"" + Folder.TrimEnd('\\') + "\""); } catch { }
    }

    public void Notify(string title, string text, ToolTipIcon icon)
    {
        if (testMode || tray == null) return;
        tray.ShowBalloonTip(5000, title, text, icon);
    }

    void BuildTray()
    {
        tray = new NotifyIcon();
        menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripSeparator());
        ToolStripItem open = menu.Items.Add("Open ClutchKit", null, delegate { ShowMain(); });
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add("Copy LIVE Studio link", null, delegate { CopyLink(); Notify("Link copied", "In LIVE Studio: Add source > Link > paste (Ctrl+V).", ToolTipIcon.Info); });
        captureItem = new ToolStripMenuItem("Show capture window", null, delegate { ShowCapture(); });
        menu.Items.Add(captureItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { ExitApp(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { ShowMain(); };
        tray.Icon = AppIcons.Sized(SystemInformation.SmallIconSize.Width);
        UpdateTray();
        tray.Visible = !testMode;
    }

    void UpdateTray()
    {
        if (tray == null) return;
        foreach (ToolStripItem i in countItems) { menu.Items.Remove(i); i.Dispose(); }
        countItems.Clear();
        int pos = 0;
        foreach (Counter c in S.Counters)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(c.DisplayName + ": " + c.Count);
            item.Enabled = false;
            menu.Items.Insert(pos++, item);
            countItems.Add(item);
        }
        captureItem.Visible = S.Mode == "window";
        string tip = "ClutchKit";
        if (S.Counters.Count > 0) tip += " - " + S.Counters[0].DisplayName + ": " + S.Counters[0].Count;
        tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
    }

    public void ExitApp()
    {
        if (capture != null && !capture.IsDisposed) capture.RememberBounds();
        S.Save(SettingsFile);
        SaveCounts();
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        TikTok.Stop();
        Engine.Clear();
        server.Stop();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        if (main != null && !main.IsDisposed) { main.AllowClose = true; main.Close(); }
        if (capture != null && !capture.IsDisposed) { capture.AllowClose = true; capture.Close(); }
        ExitThread();
    }
}

// ============================================================================================
// Overlay: the link pasted into LIVE Studio + the script it loads from the app
// ============================================================================================

public static class Overlay
{
    // Bump this when Script() changes: open overlays notice and reload the new script by themselves.
    public const string Version = "4.0";

    // Tiny page packed into a "data:" link so LIVE Studio can always load it (even when the app is
    // closed). It keeps trying to load the real overlay script from the app.
    // Keep this text unchanged - links people already pasted into LIVE Studio depend on it.
    public static string Html(int port)
    {
        return
            "<!doctype html><html><head><meta charset=utf-8><title>Death Counter overlay.html</title>" +
            "<style>html,body{margin:0;height:100%;overflow:hidden;background:transparent}</style></head><body><script>" +
            "window.DC_BASE='http://127.0.0.1:" + port + "';" +
            "window.dcBoot=function(){fetch(DC_BASE+'/overlay.js',{cache:'no-store'}).then(function(r){if(!r.ok)throw 0;return r.text()})" +
            ".then(function(t){(new Function(t))()}).catch(function(){setTimeout(dcBoot,1000)})};" +
            "window.dcReload=function(){document.body.innerHTML='';var s=document.getElementById('dcstyle');if(s)s.parentNode.removeChild(s);setTimeout(dcBoot,200)};" +
            "dcBoot();</script><!--";   // LIVE Studio adds "?winName=..." to the link; this hides it
    }

    // LIVE Studio rules for Link sources: the link must contain "http(s)://" (or it gets
    // "https://" stuck in front) and a "name.letters" part (the "overlay.html" in the title).
    public static string Link(int port)
    {
        StringBuilder sb = new StringBuilder("data:text/html,");
        foreach (char c in Html(port))
        {
            switch (c)
            {
                case '%': sb.Append("%25"); break;
                case '#': sb.Append("%23"); break;
                case '?': sb.Append("%3F"); break;
                case ' ': sb.Append("%20"); break;
                case '"': sb.Append("%22"); break;
                case '\\': sb.Append("%5C"); break;
                case '\r': case '\n': case '\t': break;
                default:
                    if (c < 32 || c > 126) sb.Append(Uri.EscapeDataString(c.ToString()));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // The real overlay, served by the app at /overlay.js. It polls /api/state for every counter
    // (each with its own look), so changes in the app show up live. One row per visible counter.
    public static string Script()
    {
        return
            "(function(){" +
            "var V='" + Version + "',B=window.DC_BASE||'http://127.0.0.1:8787';" +
            "var css='html,body{margin:0;height:100%;overflow:hidden;background:transparent}" +
            "body{display:flex;align-items:center;justify-content:center}" +
            "#dcw{display:flex;flex-direction:column;align-items:center;gap:.08em;line-height:1.15;font-size:100px;visibility:hidden}" +
            ".dcr{display:inline-flex;align-items:center;gap:.28em;white-space:nowrap}.dcr>span{display:inline-block}.dcn{font-variant-numeric:tabular-nums}" +
            "@keyframes dc-pop{0%{transform:scale(1)}35%{transform:scale(1.4)}100%{transform:scale(1)}}" +
            "@keyframes dc-bounce{0%,55%,100%{transform:translateY(0)}30%{transform:translateY(-35%)}75%{transform:translateY(-12%)}}" +
            "@keyframes dc-shake{0%,100%{transform:translateX(0)}15%{transform:translateX(-8%)}30%{transform:translateX(8%)}" +
            "45%{transform:translateX(-6%)}60%{transform:translateX(6%)}75%{transform:translateX(-3%)}90%{transform:translateX(3%)}}" +
            "@keyframes dc-flash{0%{color:var(--dcflash)}100%{color:var(--dccolor)}}" +
            "@keyframes dc-slide{0%{transform:translateY(60%);opacity:0}100%{transform:translateY(0);opacity:1}}';" +
            "var st=document.createElement('style');st.id='dcstyle';st.textContent=css;document.head.appendChild(st);" +
            "var w=document.createElement('div');w.id='dcw';document.body.appendChild(w);" +
            "var ICONS={skull:'\\uD83D\\uDC80',crossbones:'\\u2620\\uFE0F',coffin:'\\u26B0\\uFE0F',swords:'\\u2694\\uFE0F',none:''};" +
            "var rows={},order='',alive=true;" +
            "function sh(c){var o=[],q=c.outlineWidth/100;if(q>0){for(var a=0;a<16;a++){var x=Math.cos(a*Math.PI/8)*q,y=Math.sin(a*Math.PI/8)*q;" +
            "o.push(x.toFixed(3)+'em '+y.toFixed(3)+'em 0 '+c.outline)}}if(c.shadow)o.push('0 .08em .25em rgba(0,0,0,.5)');return o.length?o.join(','):'none'}" +
            "function fit(){w.style.fontSize='100px';var a=w.offsetWidth,h=w.offsetHeight;if(!a||!h)return;" +
            "var k=Math.min(innerWidth*0.94/a,innerHeight*0.86/h);w.style.fontSize=Math.max(6,100*k)+'px'}" +
            "function look(r,c){var k=JSON.stringify([c.font,c.bold,c.italic,c.color,c.outline,c.outlineWidth,c.shadow,c.flash]);" +
            "if(k===r.key)return false;r.key=k;var e=r.el.style;" +
            "e.fontFamily=\"'\"+c.font+\"','Segoe UI',sans-serif\";e.fontWeight=c.bold?'900':'400';e.fontStyle=c.italic?'italic':'normal';" +
            "e.color=c.color;e.setProperty('--dccolor',c.color);e.setProperty('--dcflash',c.flash);e.textShadow=sh(c);return true}" +
            "function row(c){var r=rows[c.id];if(r)return r;var el=document.createElement('div');el.className='dcr';" +
            "var ic=document.createElement('span'),lb=document.createElement('span'),n=document.createElement('span');n.className='dcn';" +
            "el.appendChild(ic);el.appendChild(lb);el.appendChild(n);return rows[c.id]={el:el,ic:ic,lb:lb,n:n,seq:c.seq,count:null,label:null,icon:null,key:''}}" +
            "function anim(r,c){if(c.anim==='none')return;r.n.style.animation='none';void r.n.offsetWidth;r.n.style.animation='dc-'+c.anim+' '+c.animMs+'ms ease-out'}" +
            "function tick(){if(!alive)return;fetch(B+'/api/state',{cache:'no-store'}).then(function(r){return r.json()}).then(function(s){" +
            "if(s.overlayVersion!==V){alive=false;if(window.dcReload)window.dcReload();return}" +
            "var ch=false,seen={},ids=[];" +
            "for(var i=0;i<s.counters.length;i++){var c=s.counters[i],r=row(c);seen[c.id]=1;ids.push(c.id);" +
            "if(look(r,c))ch=true;" +
            "if(r.label!==c.label){r.lb.textContent=c.label;r.lb.style.display=c.label?'':'none';r.label=c.label;ch=true}" +
            "if(r.icon!==c.icon){r.ic.textContent=ICONS[c.icon]||'';r.ic.style.display=r.ic.textContent?'':'none';r.icon=c.icon;ch=true}" +
            "if(r.count!==c.count){r.n.textContent=c.count;r.count=c.count;ch=true}" +
            "if(c.seq!==r.seq){anim(r,c);r.seq=c.seq}}" +
            "for(var id in rows){if(!seen[id]){if(rows[id].el.parentNode)w.removeChild(rows[id].el);delete rows[id];ch=true}}" +
            "var o=ids.join(',');if(o!==order){for(var j=0;j<ids.length;j++)w.appendChild(rows[ids[j]].el);order=o;ch=true}" +
            "if(ch)fit();w.style.visibility=ids.length?'visible':'hidden';setTimeout(tick,150)" +
            "}).catch(function(){setTimeout(tick,1000)})}" +
            "addEventListener('resize',fit);tick();" +
            "})();";
    }
}

// Answers the overlay on http://127.0.0.1:<port>. Only reachable from this PC.
public class OverlayServer
{
    readonly int port;
    readonly App app;
    TcpListener v4, v6;

    public OverlayServer(int port, App app) { this.port = port; this.app = app; }

    public string Start()
    {
        try
        {
            v4 = new TcpListener(IPAddress.Loopback, port);
            v4.Start();
            StartThread(AcceptLoop, v4);
        }
        catch (Exception ex) { return ex.Message; }
        try
        {
            v6 = new TcpListener(IPAddress.IPv6Loopback, port);
            v6.Start();
            StartThread(AcceptLoop, v6);
        }
        catch { v6 = null; }
        return null;
    }

    public void Stop()
    {
        try { if (v4 != null) v4.Stop(); } catch { }
        try { if (v6 != null) v6.Stop(); } catch { }
    }

    static void StartThread(ParameterizedThreadStart fn, object arg)
    {
        Thread t = new Thread(fn);
        t.IsBackground = true;
        t.Start(arg);
    }

    void AcceptLoop(object o)
    {
        TcpListener listener = (TcpListener)o;
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); } catch { break; }
            StartThread(HandleClient, client);
        }
    }

    void HandleClient(object o)
    {
        TcpClient client = (TcpClient)o;
        try
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            {
                ns.ReadTimeout = 30000;
                StreamReader reader = new StreamReader(ns, Encoding.ASCII, false, 4096, true);
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    bool close = false;
                    string h;
                    while (!string.IsNullOrEmpty(h = reader.ReadLine()))
                    {
                        if (h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) &&
                            h.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0) close = true;
                    }
                    if (h == null) break;

                    string[] parts = line.Split(' ');
                    string method = parts[0].ToUpperInvariant();
                    string path = parts.Length > 1 ? parts[1] : "/";
                    int q = path.IndexOf('?');
                    if (q >= 0) path = path.Substring(0, q);

                    string status = "200 OK", type = "application/json", body;
                    if (method == "OPTIONS") { status = "204 No Content"; body = ""; }
                    else if (path == "/api/state") body = app.StateJson;
                    else if (path == "/api/count") body = "{\"count\":" + app.FirstCount + "}";   // links from older versions
                    else if (path == "/overlay.js") { type = "application/javascript"; body = Overlay.Script(); }
                    else { status = "404 Not Found"; type = "text/plain"; body = "Not found"; }

                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    string head = "HTTP/1.1 " + status + "\r\n" +
                                  "Content-Type: " + type + "; charset=utf-8\r\n" +
                                  "Content-Length: " + bytes.Length + "\r\n" +
                                  "Cache-Control: no-store\r\n" +
                                  "Access-Control-Allow-Origin: *\r\n" +
                                  "Access-Control-Allow-Methods: GET, OPTIONS\r\n" +
                                  "Access-Control-Allow-Headers: *\r\n" +
                                  "Access-Control-Allow-Private-Network: true\r\n" +
                                  "Connection: " + (close ? "close" : "keep-alive") + "\r\n\r\n";
                    byte[] hb = Encoding.ASCII.GetBytes(head);
                    ns.Write(hb, 0, hb.Length);
                    if (method != "HEAD") ns.Write(bytes, 0, bytes.Length);
                    ns.Flush();
                    if (close) break;
                }
            }
        }
        catch { }
    }
}

// ============================================================================================
// Drawing the counters (capture window + previews)
// ============================================================================================

public static class Renderer
{
    public static readonly string[] IconKeys = { "skull", "crossbones", "coffin", "swords", "none" };
    public static readonly string[] IconNames = { "Skull", "Skull and crossbones", "Coffin", "Crossed swords", "No icon" };
    public static readonly string[] AnimKeys = { "none", "pop", "bounce", "shake", "flash", "slide" };
    public static readonly string[] AnimNames = { "None", "Pop", "Bounce", "Shake", "Flash color", "Slide up" };

    class Row
    {
        public Counter C;
        public GraphicsPath Num, Label, IconOuter, IconHoles;
        public float Outline;   // outline width in path units (font size 100)

        public RectangleF Bounds
        {
            get
            {
                RectangleF b = Num.GetBounds();
                if (Label != null) b = RectangleF.Union(b, Label.GetBounds());
                if (IconOuter != null) b = RectangleF.Union(b, IconOuter.GetBounds());
                return b;
            }
        }

        public void Apply(Matrix m)
        {
            Num.Transform(m);
            if (Label != null) Label.Transform(m);
            if (IconOuter != null) IconOuter.Transform(m);
            if (IconHoles != null) IconHoles.Transform(m);
        }

        public void Dispose()
        {
            if (Num != null) Num.Dispose();
            if (Label != null) Label.Dispose();
            if (IconOuter != null) IconOuter.Dispose();
            if (IconHoles != null) IconHoles.Dispose();
        }
    }

    public static FontFamily Family(string name)
    {
        try { return new FontFamily(name); } catch { return new FontFamily("Segoe UI"); }
    }

    static FontStyle PickStyle(FontFamily f, bool bold, bool italic)
    {
        FontStyle want = (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
        if (f.IsStyleAvailable(want)) return want;
        foreach (FontStyle t in new[] { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic })
            if (f.IsStyleAvailable(t)) return t;
        return FontStyle.Regular;
    }

    static double Interp(double t, double[] times, double[] vals)
    {
        if (t <= times[0]) return vals[0];
        for (int i = 1; i < times.Length; i++)
        {
            if (t <= times[i])
            {
                double u = (t - times[i - 1]) / (times[i] - times[i - 1]);
                u = 1 - (1 - u) * (1 - u);   // ease-out within each step
                return vals[i - 1] + (vals[i] - vals[i - 1]) * u;
            }
        }
        return vals[vals.Length - 1];
    }

    static Color Lerp(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static void Move(GraphicsPath p, float dx, float dy)
    {
        if (p == null) return;
        using (Matrix m = new Matrix()) { m.Translate(dx, dy); p.Transform(m); }
    }

    // Builds the icon at the height of the digits, left edge at x = 0. Skull is drawn by hand;
    // the others use Windows' symbol font.
    static void MakeIcon(string key, RectangleF digits, out GraphicsPath outer, out GraphicsPath holes)
    {
        outer = null; holes = null;
        if (key == "skull")
        {
            outer = new GraphicsPath(FillMode.Winding);
            outer.AddEllipse(0, 0, 100, 86);
            outer.AddRectangle(new RectangleF(22, 60, 56, 42));
            holes = new GraphicsPath(FillMode.Winding);
            holes.AddEllipse(15, 32, 28, 26);
            holes.AddEllipse(57, 32, 28, 26);
            holes.AddPolygon(new[] { new PointF(50, 60), new PointF(43, 74), new PointF(57, 74) });
            holes.AddRectangle(new RectangleF(37, 86, 5, 16));
            holes.AddRectangle(new RectangleF(58, 86, 5, 16));
        }
        else
        {
            string glyph = key == "crossbones" ? "\u2620" : key == "coffin" ? "\u26B0" : key == "swords" ? "\u2694" : null;
            if (glyph == null) return;
            outer = new GraphicsPath();
            using (FontFamily sym = Family("Segoe UI Symbol"))
                outer.AddString(glyph, sym, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
            if (outer.PointCount == 0) { outer.Dispose(); outer = null; return; }
        }
        RectangleF ob = outer.GetBounds();
        float k = digits.Height * 1.12f / ob.Height;
        float cy = digits.Y + digits.Height / 2f;
        using (Matrix m = new Matrix())
        {
            m.Translate(0, cy);
            m.Scale(k, k);
            m.Translate(-ob.X, -(ob.Y + ob.Height / 2f));
            outer.Transform(m);
            if (holes != null) holes.Transform(m);
        }
    }

    // One counter's row in its own font, laid out with its digit center at y = 0.
    static Row BuildRow(Counter c, float em, out float lineHeight)
    {
        Row r = new Row();
        r.C = c;
        r.Outline = c.Style.OutlineWidth / 100f * em;
        using (FontFamily fam = Family(c.Style.Font))
        {
            FontStyle fs = PickStyle(fam, c.Style.Bold, c.Style.Italic);
            RectangleF digitBox;
            using (GraphicsPath probe = new GraphicsPath())
            {
                probe.AddString("0", fam, (int)fs, em, PointF.Empty, StringFormat.GenericTypographic);
                digitBox = probe.GetBounds();
            }
            lineHeight = digitBox.Height * 1.45f;
            r.Num = new GraphicsPath();
            r.Num.AddString(c.Count.ToString(), fam, (int)fs, em, PointF.Empty, StringFormat.GenericTypographic);
            if (!string.IsNullOrEmpty(c.Name))
            {
                r.Label = new GraphicsPath();
                r.Label.AddString(c.Name, fam, (int)fs, em, PointF.Empty, StringFormat.GenericTypographic);
                if (r.Label.PointCount == 0) { r.Label.Dispose(); r.Label = null; }
            }
            MakeIcon(c.Icon, digitBox, out r.IconOuter, out r.IconHoles);

            // icon, label, number side by side
            RectangleF nb = r.Num.GetBounds();
            float gap = em * 0.28f, x = 0;
            if (r.IconOuter != null) x = r.IconOuter.GetBounds().Right + gap;
            if (r.Label != null) { RectangleF lb = r.Label.GetBounds(); Move(r.Label, x - lb.X, 0); x += lb.Width + gap; }
            Move(r.Num, x - nb.X, 0);

            // digit center to y = 0, left edge to x = 0
            RectangleF b = r.Bounds;
            using (Matrix m = new Matrix()) { m.Translate(-b.X, -(digitBox.Y + digitBox.Height / 2f)); r.Apply(m); }
        }
        return r;
    }

    // Draws the counters as rows, stacked and centered, each in its own look, scaled to fit the area.
    // progress(counter) = animation progress 0..1, or -1 for none.
    public static void Draw(Graphics g, RectangleF area, IList<Counter> counters, Func<Counter, double> progress)
    {
        if (counters == null || counters.Count == 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        const float em = 100f;
        List<Row> rows = new List<Row>();
        try
        {
            float y = 0, maxW = 0, maxOutline = 0;
            bool anyShadow = false;
            foreach (Counter c in counters)
            {
                float line;
                Row r = BuildRow(c, em, out line);
                using (Matrix m = new Matrix()) { m.Translate(0, y + line / 2f); r.Apply(m); }
                maxW = Math.Max(maxW, r.Bounds.Width);
                maxOutline = Math.Max(maxOutline, r.Outline);
                anyShadow |= c.Style.Shadow;
                rows.Add(r);
                y += line;
            }
            foreach (Row r in rows)
            {
                RectangleF b = r.Bounds;
                using (Matrix m = new Matrix()) { m.Translate((maxW - b.Width) / 2f, 0); r.Apply(m); }
            }
            RectangleF total = new RectangleF(0, 0, maxW, y);
            foreach (Row r in rows) total = RectangleF.Union(total, r.Bounds);

            float shadowOff = anyShadow ? em * 0.08f : 0f;
            float scale = Math.Min(area.Width * 0.92f / (total.Width + 2 * maxOutline), area.Height * 0.86f / (total.Height + 2 * maxOutline + shadowOff));
            if (scale <= 0) return;
            using (Matrix m = new Matrix())
            {
                m.Translate(area.X + area.Width / 2f, area.Y + area.Height / 2f);
                m.Scale(scale, scale);
                m.Translate(-(total.X + total.Width / 2f), -(total.Y + total.Height / 2f));
                foreach (Row r in rows) r.Apply(m);
            }

            foreach (Row r in rows)
            {
                Style s = r.C.Style;
                float penW = 2 * r.Outline * scale;
                float shadowPx = s.Shadow ? em * 0.08f * scale : 0f;
                Color fill = s.TextColor;
                int alpha = 255;
                double t = progress != null ? progress(r.C) : -1;
                if (t >= 0 && t < 1) Animate(r.Num, s, t, ref fill, ref alpha);
                Paint(g, r.IconOuter, s.TextColor, s, penW, shadowPx, 255);
                if (r.IconHoles != null)
                    using (Brush hb = new SolidBrush(s.OutlineWidth > 0 ? s.OutlineColor : Color.Black)) g.FillPath(hb, r.IconHoles);
                Paint(g, r.Label, s.TextColor, s, penW, shadowPx, 255);
                Paint(g, r.Num, fill, s, penW, shadowPx, alpha);
            }
        }
        finally
        {
            foreach (Row r in rows) r.Dispose();
        }
    }

    static void Animate(GraphicsPath num, Style s, double t, ref Color fill, ref int alpha)
    {
        RectangleF b2 = num.GetBounds();
        PointF c = new PointF(b2.X + b2.Width / 2f, b2.Y + b2.Height / 2f);
        using (Matrix a = new Matrix())
        {
            switch (s.Anim)
            {
                case "pop":
                    float k = (float)Interp(t, new[] { 0, .35, 1 }, new[] { 1, 1.4, 1 });
                    a.Translate(c.X, c.Y); a.Scale(k, k); a.Translate(-c.X, -c.Y);
                    break;
                case "bounce":
                    a.Translate(0, (float)(Interp(t, new[] { 0, .3, .55, .75, 1 }, new[] { 0, -.35, 0, -.12, 0 }) * b2.Height));
                    break;
                case "shake":
                    a.Translate((float)(Interp(t, new[] { 0, .15, .3, .45, .6, .75, .9, 1 },
                        new[] { 0, -.08, .08, -.06, .06, -.03, .03, 0 }) * b2.Width), 0);
                    break;
                case "slide":
                    double e = 1 - (1 - t) * (1 - t);
                    a.Translate(0, (float)((1 - e) * 0.6 * b2.Height));
                    alpha = (int)(255 * e);
                    break;
                case "flash":
                    fill = Lerp(s.FlashColor, s.TextColor, 1 - (1 - t) * (1 - t));
                    break;
            }
            num.Transform(a);
        }
    }

    static void Paint(Graphics g, GraphicsPath p, Color fill, Style s, float penW, float shadowPx, int alpha)
    {
        if (p == null) return;
        if (shadowPx > 0)
        {
            using (GraphicsPath sp = (GraphicsPath)p.Clone())
            {
                Move(sp, 0, shadowPx);
                Color sc = Color.FromArgb(alpha * 120 / 255, 0, 0, 0);
                if (penW > 0) using (Pen pen = new Pen(sc, penW)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, sp); }
                using (Brush br = new SolidBrush(sc)) g.FillPath(br, sp);
            }
        }
        if (penW > 0)
            using (Pen pen = new Pen(Color.FromArgb(alpha, s.OutlineColor), penW)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
        using (Brush br = new SolidBrush(Color.FromArgb(alpha, fill))) g.FillPath(br, p);
    }
}

// Repaints its owner smoothly while an animation is playing.
public class AnimationClock
{
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    DateTime until = DateTime.MinValue;

    public AnimationClock(Control owner)
    {
        timer.Interval = 15;
        timer.Tick += delegate
        {
            if (DateTime.Now > until) timer.Stop();
            owner.Invalidate();
        };
    }

    public void Start(int ms) { until = DateTime.Now.AddMilliseconds(ms + 30); timer.Start(); }
    public void Dispose() { timer.Stop(); timer.Dispose(); }
}

// ============================================================================================
// Capture window (for LIVE Studio "Window capture")
// ============================================================================================

public class CaptureForm : Form
{
    readonly App app;
    readonly AnimationClock clock;
    public bool AllowClose;

    public CaptureForm(App app)
    {
        this.app = app;
        Text = "ClutchKit - Overlay";
        Icon = app.AppIcon;
        MinimizeBox = false;              // minimized windows can't be captured
        MaximizeBox = false;
        MinimumSize = new Size(200, 80);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        if (!app.S.CaptureBounds.IsEmpty && IsOnScreen(app.S.CaptureBounds))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = app.S.CaptureBounds;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(UI.D(560), UI.D(180));
        }
        clock = new AnimationClock(this);
        app.CountChanged += OnChange;
        app.SettingsChanged += OnChange;
        app.Animated += OnAnimated;
    }

    static bool IsOnScreen(Rectangle r)
    {
        foreach (Screen s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) return true;
        return false;
    }

    void OnChange(object sender, EventArgs e) { Invalidate(); }
    void OnAnimated(object sender, EventArgs e) { clock.Start(app.LongestAnimMs()); }

    public void RememberBounds()
    {
        if (WindowState == FormWindowState.Normal) app.S.CaptureBounds = Bounds;
    }

    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); RememberBounds(); app.S.Save(app.SettingsFile); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        RememberBounds();
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        app.CountChanged -= OnChange;
        app.SettingsChanged -= OnChange;
        app.Animated -= OnAnimated;
        clock.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(app.S.WindowBg);
        Renderer.Draw(e.Graphics, ClientRectangle, app.VisibleCounters(), app.Progress);
    }
}

// Live preview: every counter shown on stream, or just one counter.
public class PreviewPanel : Panel
{
    readonly App app;
    readonly Counter only;
    readonly AnimationClock clock;

    public PreviewPanel(App app, Counter only)
    {
        this.app = app;
        this.only = only;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        clock = new AnimationClock(this);
        app.CountChanged += OnChange;
        app.SettingsChanged += OnChange;
        app.Animated += OnAnimated;
    }

    void OnChange(object sender, EventArgs e) { Invalidate(); }
    void OnAnimated(object sender, EventArgs e) { clock.Start(app.LongestAnimMs()); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            app.CountChanged -= OnChange;
            app.SettingsChanged -= OnChange;
            app.Animated -= OnAnimated;
            clock.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int sq = UI.D(12);
        using (Brush a = new SolidBrush(Color.FromArgb(44, 47, 54)))
        using (Brush b = new SolidBrush(Color.FromArgb(34, 37, 43)))
        {
            for (int y = 0; y < Height; y += sq)
                for (int x = 0; x < Width; x += sq)
                    g.FillRectangle(((x / sq + y / sq) % 2 == 0) ? a : b, x, y, sq, sq);
        }
        List<Counter> list = only != null ? new List<Counter> { only } : app.VisibleCounters();
        if (list.Count == 0)
        {
            TextRenderer.DrawText(g, "No counter is set to show on stream.", UI.F(10f, FontStyle.Regular), ClientRectangle, UI.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        Renderer.Draw(g, ClientRectangle, list, app.Progress);
    }
}

// ============================================================================================
// Settings window
// ============================================================================================

public static class UI
{
    public static readonly Color Bg = Color.FromArgb(22, 24, 29);
    public static readonly Color Nav = Color.FromArgb(15, 17, 21);
    public static readonly Color Card = Color.FromArgb(31, 34, 41);
    public static readonly Color Line = Color.FromArgb(50, 54, 63);
    public static readonly Color Text = Color.FromArgb(237, 238, 240);
    public static readonly Color Muted = Color.FromArgb(150, 157, 170);
    public static readonly Color Accent = Color.FromArgb(229, 72, 77);
    public static readonly Color Input = Color.FromArgb(13, 15, 19);
    public static readonly Color Warn = Color.FromArgb(255, 180, 60);
    public static readonly Color Good = Color.FromArgb(46, 213, 115);

    static float dpi = -1;

    public static int D(int v)
    {
        if (dpi < 0) using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
        return (int)Math.Round(v * dpi / 96f);
    }

    public static Font F(float pt, FontStyle st) { return new Font("Segoe UI", pt, st); }

    public static Label Lbl(Control parent, string text, int x, int y, int w, float pt, Color color, FontStyle st)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = F(pt, st);
        l.ForeColor = color;
        l.BackColor = Color.Transparent;
        l.Location = new Point(D(x), D(y));
        l.AutoSize = false;
        l.Size = new Size(D(w), D((int)(pt * 2.4f) + 4));
        parent.Controls.Add(l);
        return l;
    }

    public static Label Para(Control parent, string text, int x, int y, int w, int h)
    {
        Label l = Lbl(parent, text, x, y, w, 9.5f, Muted, FontStyle.Regular);
        l.Size = new Size(D(w), D(h));
        return l;
    }

    public static Button Btn(Control parent, string text, int x, int y, int w, int h, bool accent)
    {
        Button b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = accent ? Accent : Line;
        b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(240, 95, 100) : Color.FromArgb(44, 48, 57);
        b.BackColor = accent ? Accent : Card;
        b.ForeColor = Text;
        b.Font = F(10f, accent ? FontStyle.Bold : FontStyle.Regular);
        b.Location = new Point(D(x), D(y));
        b.Size = new Size(D(w), D(h));
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
        parent.Controls.Add(b);
        return b;
    }

    public static void Style(Control c)
    {
        c.BackColor = Input;
        c.ForeColor = Text;
        c.Font = F(10f, FontStyle.Regular);
    }

    public static ComboBox Combo(Control p, int x, int y, int w)
    {
        ComboBox c = new ComboBox();
        Style(c);
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.FlatStyle = FlatStyle.Flat;
        c.Location = new Point(D(x), D(y));
        c.Size = new Size(D(w), D(30));
        c.MaxDropDownItems = 16;
        p.Controls.Add(c);
        return c;
    }

    public static CheckBox Check(Control p, string text, int x, int y, bool value)
    {
        CheckBox c = new CheckBox();
        c.Text = text;
        c.Checked = value;
        c.ForeColor = Text;
        c.Font = F(10f, FontStyle.Regular);
        c.Location = new Point(D(x), D(y));
        c.AutoSize = true;
        p.Controls.Add(c);
        return c;
    }

    public static NumericUpDown Number(Control p, int x, int y, int min, int max, int value, int step)
    {
        NumericUpDown n = new NumericUpDown();
        Style(n);
        n.Minimum = min;
        n.Maximum = max;
        n.Increment = step;
        n.Value = Math.Max(min, Math.Min(max, value));
        n.Location = new Point(D(x), D(y));
        n.Size = new Size(D(100), D(30));
        p.Controls.Add(n);
        return n;
    }

    public static void PaintSwatch(Button b, Color c)
    {
        b.BackColor = c;
        b.FlatAppearance.MouseOverBackColor = c;
        b.ForeColor = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;
        b.Text = Settings.ToHex(c).ToUpperInvariant();
    }

    public static Button ColorButton(Control p, int x, int y, Color c, Action<Color> set)
    {
        Button b = Btn(p, "", x, y, 140, 32, false);
        PaintSwatch(b, c);
        b.Click += delegate
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.FullOpen = true;
                d.Color = b.BackColor;
                if (d.ShowDialog(p.FindForm()) == DialogResult.OK) { PaintSwatch(b, d.Color); set(d.Color); }
            }
        };
        return b;
    }

    public static Label FieldLabel(Control p, string text, int x, int y)
    {
        return Lbl(p, text, x, y + 4, 160, 10f, Muted, FontStyle.Regular);
    }
}

// The look of one counter: font, colors, outline, shadow and animation, with a live preview.
public class StyleForm : Form
{
    readonly App app;
    readonly Counter c;
    bool loading = true;

    public StyleForm(App app, Counter counter)
    {
        this.app = app;
        c = counter;
        Style s = c.Style;
        Text = "Appearance - " + c.DisplayName;
        Icon = app.AppIcon;
        BackColor = UI.Bg;
        ForeColor = UI.Text;
        Font = UI.F(10f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(UI.D(660), UI.D(640));

        UI.Lbl(this, "Appearance: " + c.DisplayName, 28, 20, 600, 16f, UI.Text, FontStyle.Bold);
        UI.Para(this, "How this counter looks on stream. Changes show up in LIVE Studio right away.", 30, 60, 600, 24);

        // look
        UI.FieldLabel(this, "Font", 30, 100);
        ComboBox fontBox = UI.Combo(this, 190, 100, 250);
        using (InstalledFontCollection fonts = new InstalledFontCollection())
            foreach (FontFamily f in fonts.Families) fontBox.Items.Add(f.Name);
        int fi = fontBox.Items.IndexOf(s.Font);
        if (fi < 0) { fontBox.Items.Insert(0, s.Font); fi = 0; }
        fontBox.SelectedIndex = fi;
        fontBox.SelectedIndexChanged += delegate { s.Font = (string)fontBox.SelectedItem; Changed(); };
        CheckBox bold = UI.Check(this, "Bold", 456, 103, s.Bold);
        CheckBox italic = UI.Check(this, "Italic", 526, 103, s.Italic);
        bold.CheckedChanged += delegate { s.Bold = bold.Checked; Changed(); };
        italic.CheckedChanged += delegate { s.Italic = italic.Checked; Changed(); };

        UI.FieldLabel(this, "Text color", 30, 142);
        UI.ColorButton(this, 190, 142, s.TextColor, delegate (Color col) { s.TextColor = col; Changed(); });

        UI.FieldLabel(this, "Outline", 30, 184);
        UI.ColorButton(this, 190, 184, s.OutlineColor, delegate (Color col) { s.OutlineColor = col; Changed(); });
        UI.Lbl(this, "Thickness", 342, 188, 80, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown outline = UI.Number(this, 424, 184, 0, 20, s.OutlineWidth, 1);
        outline.Size = new Size(UI.D(70), UI.D(30));
        outline.ValueChanged += delegate { s.OutlineWidth = (int)outline.Value; Changed(); };
        CheckBox shadow = UI.Check(this, "Drop shadow", 510, 187, s.Shadow);
        shadow.CheckedChanged += delegate { s.Shadow = shadow.Checked; Changed(); };

        // animation
        UI.FieldLabel(this, "Animation", 30, 236);
        ComboBox animBox = UI.Combo(this, 190, 236, 250);
        animBox.Items.AddRange(Renderer.AnimNames);
        animBox.SelectedIndex = Math.Max(0, Array.IndexOf(Renderer.AnimKeys, s.Anim));
        animBox.SelectedIndexChanged += delegate { s.Anim = Renderer.AnimKeys[animBox.SelectedIndex]; Changed(); if (!loading) app.PreviewAnimation(c); };

        UI.FieldLabel(this, "Duration (ms)", 30, 278);
        NumericUpDown dur = UI.Number(this, 190, 278, 100, 3000, s.AnimMs, 50);
        dur.ValueChanged += delegate { s.AnimMs = (int)dur.Value; Changed(); };
        UI.Lbl(this, "Flash color", 342, 282, 90, 10f, UI.Muted, FontStyle.Regular);
        UI.ColorButton(this, 434, 278, s.FlashColor, delegate (Color col) { s.FlashColor = col; Changed(); });

        Button play = UI.Btn(this, "Preview animation", 30, 326, 190, 36, false);
        play.Click += delegate { app.PreviewAnimation(c); };

        PreviewPanel pv = new PreviewPanel(app, c);
        pv.Location = new Point(UI.D(30), UI.D(378));
        pv.Size = new Size(UI.D(600), UI.D(150));
        Controls.Add(pv);

        Button all = UI.Btn(this, "Use this look for all counters", 30, 552, 270, 40, false);
        all.Visible = app.S.Counters.Count > 1;
        Label done = UI.Para(this, "", 310, 562, 180, 24);
        all.Click += delegate
        {
            foreach (Counter o in app.S.Counters) if (o != c) o.Style = c.Style.Clone();
            app.ApplySettings();
            done.Text = "Copied to all counters.";
            done.ForeColor = UI.Good;
        };
        Button close = UI.Btn(this, "Done", 510, 552, 120, 40, true);
        close.Click += delegate { Close(); };
        AcceptButton = close;
        loading = false;
        Shown += delegate { ActiveControl = close; };
    }

    void Changed() { if (!loading) app.ApplySettings(); }
}

public partial class MainForm : Form
{
    readonly App app;
    readonly Panel nav, content;
    readonly List<Button> navButtons = new List<Button>();
    readonly List<Panel> pages = new List<Panel>();
    readonly int navTop;
    public bool AllowClose;
    bool loading;

    // Counters page: one card per counter
    class Card
    {
        public Counter C;
        public Label CountLbl;
        public NumericUpDown SetBox;
        public Button[] KeyBtns = new Button[3];
    }
    readonly List<Card> cards = new List<Card>();
    Panel cardsHost;
    Label keyMessage;
    Counter capCounter;
    int capAction = -1;

    RadioButton linkRadio, windowRadio;

    public MainForm(App app)
    {
        this.app = app;
        Text = "ClutchKit";
        Icon = app.AppIcon;
        BackColor = UI.Bg;
        ForeColor = UI.Text;
        Font = UI.F(10f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(UI.D(900), UI.D(660));

        content = new Panel();
        content.Dock = DockStyle.Fill;
        content.BackColor = UI.Bg;
        nav = new Panel();
        nav.Dock = DockStyle.Left;
        nav.Width = UI.D(200);
        nav.BackColor = UI.Nav;
        Controls.Add(content);
        Controls.Add(nav);
        navTop = BuildBrand();

        // Menu order: what you track > how it gets on stream > app options
        loading = true;
        AddPage("Counters", BuildCountersPage());
        AddPage("Gift actions", BuildActionsPage());
        AddPage("TikTok LIVE", BuildTikTokPage());
        AddPage("Overlay", BuildOverlayPage());
        AddPage("Settings", BuildSettingsPage());
        loading = false;
        SelectPage(0);

        app.CountChanged += OnCount;
        app.SettingsChanged += OnSettings;
        Shown += delegate { ActiveControl = null; };   // don't start with a text box selected
    }

    // Logo + name + version, laid out from the real text sizes so they never overlap.
    int BuildBrand()
    {
        Label title = new Label();
        title.Text = "ClutchKit";
        title.Font = UI.F(14f, FontStyle.Bold);
        title.ForeColor = UI.Text;
        title.AutoSize = true;
        Label ver = new Label();
        ver.Text = "Version " + Program.Version;
        ver.Font = UI.F(8.5f, FontStyle.Regular);
        ver.ForeColor = UI.Muted;
        ver.AutoSize = true;

        int logoSize = UI.D(36);
        int textHeight = title.PreferredHeight + ver.PreferredHeight;
        int top = UI.D(22);
        PictureBox logo = new PictureBox();
        logo.Image = AppIcons.Image(logoSize);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.Size = new Size(logoSize, logoSize);
        logo.Location = new Point(UI.D(20), top + Math.Max(0, (textHeight - logoSize) / 2));
        int textX = logo.Right + UI.D(10);
        title.Location = new Point(textX, top);
        ver.Location = new Point(textX + UI.D(2), top + title.PreferredHeight);
        nav.Controls.Add(logo);
        nav.Controls.Add(title);
        nav.Controls.Add(ver);
        return top + Math.Max(textHeight, logoSize) + UI.D(26);
    }

    // ---------------- page plumbing (add a page here for new features) ----------------

    void AddPage(string title, Panel page)
    {
        int index = pages.Count;
        page.Dock = DockStyle.Fill;
        page.BackColor = UI.Bg;
        page.AutoScroll = true;
        page.Visible = false;
        content.Controls.Add(page);
        pages.Add(page);

        Button b = new Button();
        b.Text = "   " + title;
        b.TextAlign = ContentAlignment.MiddleLeft;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 33, 40);
        b.BackColor = UI.Nav;
        b.ForeColor = UI.Muted;
        b.Font = UI.F(10.5f, FontStyle.Regular);
        b.Location = new Point(UI.D(10), navTop + index * UI.D(44));
        b.Size = new Size(UI.D(180), UI.D(38));
        b.Cursor = Cursors.Hand;
        b.Click += delegate { SelectPage(index); };
        nav.Controls.Add(b);
        navButtons.Add(b);
    }

    void SelectPage(int index)
    {
        CancelCapture();
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].Visible = i == index;
            navButtons[i].BackColor = i == index ? UI.Card : UI.Nav;
            navButtons[i].ForeColor = i == index ? UI.Text : UI.Muted;
            navButtons[i].Font = UI.F(10.5f, i == index ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    Panel NewPage(string title, string subtitle)
    {
        Panel p = new Panel();
        UI.Lbl(p, title, 36, 24, 600, 18f, UI.Text, FontStyle.Bold);
        UI.Para(p, subtitle, 38, 76, 620, 40);
        return p;
    }

    void Changed()
    {
        if (!loading) app.ApplySettings();
    }

    // ---------------- Counters ----------------

    Panel BuildCountersPage()
    {
        Panel p = NewPage("Counters", "Track anything - deaths, wins, kills, attempts. Each counter has its own hotkeys and its own look, " +
                                      "and every counter set to \"Show on stream\" appears in the same overlay.");
        keyMessage = UI.Para(p, "Click a key button, then press the key you want (Backspace = no key, Esc = cancel).", 38, 116, 620, 24);
        cardsHost = new Panel();
        cardsHost.BackColor = UI.Bg;
        cardsHost.Location = new Point(UI.D(38), UI.D(146));
        cardsHost.Width = UI.D(620);
        p.Controls.Add(cardsHost);
        RebuildCards();
        return p;
    }

    void RebuildCards()
    {
        CancelCapture();
        cardsHost.SuspendLayout();
        while (cardsHost.Controls.Count > 0) cardsHost.Controls[0].Dispose();
        cards.Clear();
        int y = 0;
        foreach (Counter c in app.S.Counters)
        {
            Panel card = BuildCard(c);
            card.Location = new Point(0, y);
            cardsHost.Controls.Add(card);
            y += card.Height + UI.D(14);
        }
        if (app.S.Counters.Count < Settings.MaxCounters)
        {
            Button add = UI.Btn(cardsHost, "+  Add counter", 0, 0, 200, 40, true);
            add.Location = new Point(0, y);
            add.Click += delegate
            {
                Counter c = app.AddCounter();
                RebuildCards();
                if (c != null) Message("Added \"" + c.DisplayName + "\". Rename it, set its keys and its look.", UI.Good);
            };
            y += add.Height + UI.D(20);
        }
        else
        {
            Label max = UI.Para(cardsHost, "That's the maximum of " + Settings.MaxCounters + " counters.", 0, 0, 400, 24);
            max.Location = new Point(0, y);
            y += max.Height + UI.D(20);
        }
        cardsHost.Height = y;
        cardsHost.ResumeLayout();
    }

    Panel BuildCard(Counter c)
    {
        Card ui = new Card();
        ui.C = c;
        Panel card = new Panel();
        card.BackColor = UI.Card;
        card.Size = new Size(UI.D(620), UI.D(218));

        // row 1: name, icon, show on stream
        TextBox name = new TextBox();
        UI.Style(name);
        name.BorderStyle = BorderStyle.FixedSingle;
        name.Text = c.Name;
        name.Location = new Point(UI.D(16), UI.D(16));
        name.Size = new Size(UI.D(220), UI.D(28));
        name.TextChanged += delegate { c.Name = name.Text.Trim(); Changed(); };
        card.Controls.Add(name);

        ComboBox icon = UI.Combo(card, 246, 16, 170);
        icon.Items.AddRange(Renderer.IconNames);
        int ii = Array.IndexOf(Renderer.IconKeys, c.Icon);
        icon.SelectedIndex = ii >= 0 ? ii : Renderer.IconKeys.Length - 1;
        icon.SelectedIndexChanged += delegate { c.Icon = Renderer.IconKeys[icon.SelectedIndex]; Changed(); };

        CheckBox show = UI.Check(card, "Show on stream", 432, 19, c.Visible);
        show.BackColor = UI.Card;
        show.CheckedChanged += delegate { c.Visible = show.Checked; Changed(); };

        // row 2: the number and its buttons
        ui.CountLbl = UI.Lbl(card, c.Count.ToString(), 12, 52, 170, 24f, UI.Text, FontStyle.Bold);
        ui.CountLbl.Size = new Size(UI.D(170), UI.D(56));
        ui.CountLbl.TextAlign = ContentAlignment.MiddleLeft;
        Button minus = UI.Btn(card, "-1", 190, 60, 56, 40, false);
        Button plus = UI.Btn(card, "+1", 252, 60, 70, 40, true);
        Button reset = UI.Btn(card, "Reset", 328, 60, 76, 40, false);
        minus.Click += delegate { app.Add(c, -1); };
        plus.Click += delegate { app.Add(c, 1); };
        reset.Click += delegate
        {
            if (MessageBox.Show(this, "Reset \"" + c.DisplayName + "\" to 0?", "ClutchKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                app.Reset(c);
        };
        ui.SetBox = UI.Number(card, 420, 66, 0, 999999, c.Count, 1);
        Button set = UI.Btn(card, "Set", 526, 64, 78, 32, false);
        set.Click += delegate { app.SetCount(c, (int)ui.SetBox.Value); };

        // row 3: hotkeys
        for (int i = 0; i < 3; i++)
        {
            int action = i;
            UI.Lbl(card, Counter.ActionNames[i], 16 + i * 200, 126, 50, 10f, UI.Muted, FontStyle.Regular);
            Button kb = UI.Btn(card, c.Keys[i].ToString(), 66 + i * 200, 118, 138, 36, false);
            kb.BackColor = UI.Input;
            kb.Click += delegate { StartCapture(c, action); };
            ui.KeyBtns[i] = kb;
        }

        // row 4: this counter's look, remove
        Button look = UI.Btn(card, "Appearance...", 16, 168, 160, 36, false);
        look.Click += delegate
        {
            CancelCapture();
            using (StyleForm f = new StyleForm(app, c)) f.ShowDialog(this);
        };
        if (app.S.Counters.Count > 1)
        {
            Button remove = UI.Btn(card, "Remove", 516, 168, 88, 36, false);
            remove.Click += delegate
            {
                if (MessageBox.Show(this, "Remove the counter \"" + c.DisplayName + "\" (count " + c.Count + ")?", "ClutchKit",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                app.RemoveCounter(c);
                RebuildCards();
            };
        }
        cards.Add(ui);
        return card;
    }

    void Message(string text, Color color) { keyMessage.Text = text; keyMessage.ForeColor = color; }

    void OnCount(object sender, EventArgs e)
    {
        foreach (Card ui in cards)
        {
            ui.CountLbl.Text = ui.C.Count.ToString();
            if (!ui.SetBox.Focused) ui.SetBox.Value = Math.Min(ui.SetBox.Maximum, ui.C.Count);
        }
    }

    void RefreshKeys()
    {
        foreach (Card ui in cards)
            for (int i = 0; i < 3; i++)
            {
                bool active = ui.C == capCounter && i == capAction;
                ui.KeyBtns[i].Text = active ? "Press a key..." : ui.C.Keys[i].ToString();
                ui.KeyBtns[i].ForeColor = active ? UI.Warn : (ui.C.Keys[i].IsSet ? UI.Text : UI.Muted);
            }
    }

    void StartCapture(Counter c, int action)
    {
        if (capCounter == c && capAction == action) { CancelCapture(); Message("Cancelled - nothing changed.", UI.Muted); return; }
        capCounter = c;
        capAction = action;
        app.CapturingHotkey = true;   // so the key you press here doesn't also change a count
        Message("Press the key for \"" + c.DisplayName + " " + Counter.ActionNames[action] + "\"  (Backspace = no key, Esc = cancel).", UI.Warn);
        RefreshKeys();
        ActiveControl = null;
        Focus();
    }

    void CancelCapture()
    {
        CancelKeyCapture();
        if (capAction < 0) return;
        capCounter = null;
        capAction = -1;
        app.CapturingHotkey = false;
        RefreshKeys();
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyCapBtn != null) return KeyCaptureKey(keyData);   // gift action keys (ActionsPage.cs)
        if (capAction < 0) return base.ProcessCmdKey(ref msg, keyData);
        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin ||
            key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
            key == Keys.LMenu || key == Keys.RMenu)
            return true;   // wait for the main key
        int mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Hotkey.Ctrl;
        if ((keyData & Keys.Alt) != 0) mods |= Hotkey.Alt;
        if ((keyData & Keys.Shift) != 0) mods |= Hotkey.Shift;
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0) mods |= Hotkey.Win;
        Counter c = capCounter;
        int action = capAction;
        string what = "\"" + c.DisplayName + " " + Counter.ActionNames[action] + "\"";

        if (key == Keys.Escape && mods == 0) { CancelCapture(); Message("Cancelled - nothing changed.", UI.Muted); return true; }
        if (key == Keys.Back && mods == 0)
        {
            c.Keys[action] = new Hotkey(0, 0);
            app.ApplySettings();
            CancelCapture();
            Message(what + " has no key now.", UI.Muted);
            return true;
        }

        Hotkey hk = new Hotkey(mods, (int)key);
        string owner = app.KeyOwner(hk, c, action);
        if (owner != null)
        {
            Message(hk + " is already used for \"" + owner + "\". Press a different key.", UI.Warn);
            return true;
        }
        bool typingKey = mods == 0 && ((key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) || key == Keys.Space ||
                                       key == Keys.Enter || key == Keys.Tab);
        if (typingKey &&
            MessageBox.Show(this, hk + " will change the count every time you type it anywhere (chat, Discord, ...).\n\nUse it anyway?",
                "ClutchKit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            CancelCapture();
            Message("Not changed. Try a key you don't type with, like a numpad or F key.", UI.Muted);
            return true;
        }
        c.Keys[action] = hk;
        app.ApplySettings();
        CancelCapture();
        Message("Saved! " + hk + " now does " + what + ".", UI.Good);
        return true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (keyCapBtn != null && !ContainsFocus) CancelKeyCapture();
        if (capAction >= 0 && !ContainsFocus) { CancelCapture(); Message("Cancelled (you clicked away) - nothing changed.", UI.Muted); }
    }

    // ---------------- Overlay ----------------

    Panel BuildOverlayPage()
    {
        Panel p = NewPage("Overlay", "How your counters get into LIVE Studio. Pick one of the two ways.");
        Settings s = app.S;

        linkRadio = new RadioButton();
        linkRadio.Text = "Link overlay (recommended)";
        linkRadio.Font = UI.F(11f, FontStyle.Bold);
        linkRadio.ForeColor = UI.Text;
        linkRadio.Location = new Point(UI.D(38), UI.D(122));
        linkRadio.AutoSize = true;
        linkRadio.Checked = s.Mode != "window";
        p.Controls.Add(linkRadio);
        UI.Para(p, "See-through text on stream. In LIVE Studio: Add source > Link > paste the link once. " +
                   "It works no matter if LIVE Studio or ClutchKit starts first, and everything you change here updates on stream automatically.",
                   60, 152, 600, 62);
        Button copy = UI.Btn(p, "Copy LIVE Studio link", 60, 218, 220, 38, true);
        Label linkStatus = UI.Para(p, "", 292, 227, 360, 24);
        copy.Click += delegate { app.CopyLink(); linkStatus.Text = "Copied! Paste it in LIVE Studio with Ctrl+V."; linkStatus.ForeColor = UI.Good; };

        windowRadio = new RadioButton();
        windowRadio.Text = "Capture window";
        windowRadio.Font = UI.F(11f, FontStyle.Bold);
        windowRadio.ForeColor = UI.Text;
        windowRadio.Location = new Point(UI.D(38), UI.D(282));
        windowRadio.AutoSize = true;
        windowRadio.Checked = s.Mode == "window";
        p.Controls.Add(windowRadio);
        UI.Para(p, "Opens a small window showing the counters. In LIVE Studio: Add source > Window capture > pick \"ClutchKit - Overlay\". " +
                   "Keep that window open (it can be behind your game, but not minimized).",
                   60, 312, 600, 62);
        UI.Lbl(p, "Background", 60, 384, 120, 10f, UI.Muted, FontStyle.Regular);
        Button bgColorBtn = UI.ColorButton(p, 180, 380, s.WindowBg, delegate (Color c) { s.WindowBg = c; Changed(); });
        Button black = UI.Btn(p, "Black", 330, 380, 80, 32, false);
        Button green = UI.Btn(p, "Green screen", 418, 380, 130, 32, false);
        black.Click += delegate { s.WindowBg = Color.Black; UI.PaintSwatch(bgColorBtn, s.WindowBg); Changed(); };
        green.Click += delegate { s.WindowBg = Color.FromArgb(0, 255, 0); UI.PaintSwatch(bgColorBtn, s.WindowBg); Changed(); };
        Button showWin = UI.Btn(p, "Show capture window", 60, 424, 220, 38, false);
        showWin.Click += delegate { windowRadio.Checked = true; app.ShowCapture(); };

        UI.Lbl(p, "Preview of your overlay", 38, 486, 300, 10f, UI.Muted, FontStyle.Regular);
        PreviewPanel pv = new PreviewPanel(app, null);
        pv.Location = new Point(UI.D(38), UI.D(512));
        pv.Size = new Size(UI.D(620), UI.D(130));
        p.Controls.Add(pv);

        linkRadio.CheckedChanged += delegate { if (!loading && linkRadio.Checked) app.SetMode("link"); };
        windowRadio.CheckedChanged += delegate { if (!loading && windowRadio.Checked) app.SetMode("window"); };
        return p;
    }

    // ---------------- Settings ----------------

    Panel BuildSettingsPage()
    {
        Panel p = NewPage("Settings", "App options.");
        CheckBox trayBox = UI.Check(p, "Start in the tray (don't open this window when ClutchKit starts)", 38, 126, app.S.StartInTray);
        trayBox.CheckedChanged += delegate { app.S.StartInTray = trayBox.Checked; Changed(); };
        UI.Para(p, "Closing this window keeps ClutchKit running by the clock. Double-click the red icon to come back, " +
                   "right-click it to exit.", 38, 166, 620, 44);
        Button folder = UI.Btn(p, "Open ClutchKit folder", 38, 224, 240, 38, false);
        folder.Click += delegate { app.OpenFolder(); };
        UI.Para(p, "counts.txt holds the counts and settings.ini holds these settings.", 290, 233, 380, 24);
        Button exit = UI.Btn(p, "Exit ClutchKit", 38, 286, 240, 38, false);
        exit.Click += delegate { app.ExitApp(); };
        if (Installer.IsInstalledCopy(Application.ExecutablePath))
        {
            Button uninstall = UI.Btn(p, "Uninstall ClutchKit...", 290, 286, 240, 38, false);
            uninstall.Click += delegate
            {
                try { Process.Start(Installer.InstalledExe, "/uninstall"); } catch { return; }
                app.ExitApp();
            };
        }
        UI.Para(p, "ClutchKit " + Program.Version + "   \u00b7   Overlay port: " + app.S.Port +
                   (app.ServerError != null ? "  (in use by another program - the overlay can't connect)" : ""), 38, 348, 620, 24);
        return p;
    }

    // ---------------- window ----------------

    void OnSettings(object sender, EventArgs e) { RefreshKeys(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CancelCapture();
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            app.MainHidden();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        app.CountChanged -= OnCount;
        app.SettingsChanged -= OnSettings;
        UnhookActionPages();
        base.OnFormClosed(e);
    }
}
