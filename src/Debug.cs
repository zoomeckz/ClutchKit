// ClutchKit - diagnostics ("Debug" page): everything needed to find out why something doesn't work
// on someone else's PC.
// ----------------------------------------------------------------------------------
// - counters for what happened (TikTok messages by type, events, keys sent/blocked, sounds, overlay requests)
// - recent errors, and crash reports for anything that wasn't caught (debug\crash-*.txt)
// - detailed logging (debug mode): debug\debug-log.txt plus the first TikTok messages of each type
// - a report (debug\report-*.txt, also copied) with the system, settings summary, all of the above
// Nothing is ever sent anywhere. The Euler Stream API key and the Windows user folder are blanked out
// of everything written here. The report is in English on purpose: it's meant for whoever fixes the bug.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

public static class Diag
{
    public const int SamplesPerType = 3, SampleChars = 4000;

    public static volatile bool Verbose;          // detailed logging (debug mode)
    public static string Folder = Path.Combine(Path.GetTempPath(), "ClutchKit-debug");
    public static string LastLayoutCheck = "";    // result of the last layout check (for the report)

    static App app;
    static readonly object gate = new object();
    static readonly DateTime started = DateTime.Now;
    static readonly Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
    static readonly Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.Ordinal);
    static readonly Dictionary<string, DateTime> marks = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    static readonly Dictionary<string, List<string>> samples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    static readonly List<string> errors = new List<string>();
    static StreamWriter file;
    static bool crashShown;

    public static void Init(string dataFolder)
    {
        lock (gate) Folder = Path.Combine(dataFolder, "debug");
    }

    public static void Attach(App a) { app = a; }

    public static void SetVerbose(bool on)
    {
        Verbose = on;
        if (on) Write("debug", "detailed logging on - ClutchKit " + Program.Version);
        else lock (gate) { if (file != null) { try { file.Dispose(); } catch { } file = null; } }
    }

    // ---------------- recording ----------------

    public static void Count(string key) { Count(key, 1); }

    public static void Count(string key, long n)
    {
        lock (gate) { long v; counts.TryGetValue(key, out v); counts[key] = v + n; }
    }

    public static long Get(string key)
    {
        lock (gate) { long v; counts.TryGetValue(key, out v); return v; }
    }

    public static void Note(string key, string value) { lock (gate) notes[key] = value ?? ""; }
    public static void Mark(string key) { lock (gate) marks[key] = DateTime.UtcNow; }

    // "0.3 s ago", or "never"
    static string Ago(string key)
    {
        DateTime t;
        lock (gate) if (!marks.TryGetValue(key, out t)) return "never";
        double s = (DateTime.UtcNow - t).TotalSeconds;
        return s < 120 ? s.ToString("0.0") + " s ago" : ((int)(s / 60)) + " min ago";
    }

    // Only in debug mode.
    public static void Write(string area, string text)
    {
        if (!Verbose) return;
        ToFile(area, text);
    }

    // Always kept (last 50) and written to the debug log.
    public static void Error(string area, string text)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  [" + area + "] " + Redact(text);
        lock (gate)
        {
            errors.Add(line);
            if (errors.Count > 50) errors.RemoveAt(0);
        }
        Count("errors");
        ToFile(area, "ERROR " + text);
    }

    static void ToFile(string area, string text)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + area + "] " + Redact(text);
        lock (gate)
        {
            try
            {
                if (file == null)
                {
                    Directory.CreateDirectory(Folder);
                    string path = Path.Combine(Folder, "debug-log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                    {
                        string old = Path.Combine(Folder, "debug-log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    file = new StreamWriter(path, true, new UTF8Encoding(false));
                    file.AutoFlush = true;
                }
                file.WriteLine(line);
            }
            catch { }
        }
    }

    // The first few TikTok messages of each type, exactly as they arrived (debug mode only).
    public static bool WantsSample(string type)
    {
        if (!Verbose) return false;
        lock (gate) { List<string> l; return !samples.TryGetValue(type, out l) || l.Count < SamplesPerType; }
    }

    public static void Sample(string type, string json)
    {
        if (json.Length > SampleChars) json = json.Substring(0, SampleChars) + " ...(cut)";
        json = Redact(json);
        lock (gate)
        {
            List<string> l;
            if (!samples.TryGetValue(type, out l)) samples[type] = l = new List<string>();
            if (l.Count >= SamplesPerType) return;
            l.Add(json);
        }
        ToFile("tiktok-sample", type + " " + json);
    }

    // Blanks out the API key and the Windows user folder (it contains the Windows user name).
    public static string Redact(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        try
        {
            string key = app != null && app.S != null ? app.S.TikTokKey : "";
            if (key != null && key.Length >= 6) s = s.Replace(key, "<api key>");
            string obsPw = app != null && app.S != null ? app.S.ObsPassword : "";
            if (obsPw != null && obsPw.Length >= 4) s = s.Replace(obsPw, "<obs password>");
            s = Regex.Replace(s, @"(apiKey=)[^&\s""']+", "$1<api key>", RegexOptions.IgnoreCase);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3) s = Regex.Replace(s, Regex.Escape(home), "%USERPROFILE%", RegexOptions.IgnoreCase);
        }
        catch { }
        return s;
    }

    // ---------------- crashes ----------------

    public static void HookCrashes()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate (object o, System.Threading.ThreadExceptionEventArgs e) { Crash(e.Exception, "window", true); };
        AppDomain.CurrentDomain.UnhandledException += delegate (object o, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null) Crash(ex, "background", false);
        };
    }

    // Saves debug\crash-<time>.txt. A crash in the window shows a message once, and ClutchKit keeps running.
    public static void Crash(Exception ex, string where, bool canContinue)
    {
        string path = null;
        try
        {
            Directory.CreateDirectory(Folder);
            path = Path.Combine(Folder, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            StringBuilder b = new StringBuilder();
            b.AppendLine("ClutchKit " + Program.Version + " crash (" + where + ") at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine(WindowsVersion() + ", .NET " + DotNetVersion());
            b.AppendLine();
            b.AppendLine(ex.ToString());
            File.WriteAllText(path, Redact(b.ToString()), new UTF8Encoding(false));
        }
        catch { }
        Error("crash", where + ": " + ex.GetType().Name + ": " + ex.Message);
        if (canContinue && !crashShown)
        {
            crashShown = true;
            try
            {
                MessageBox.Show(L.T("ClutchKit ran into a problem and saved a crash report in its debug folder:") + "\n\n" +
                                (path ?? Folder) + "\n\n" + ex.Message, "ClutchKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }

    // ---------------- system info ----------------

    static string Reg(string key, string name)
    {
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
            {
                object v = k != null ? k.GetValue(name) : null;
                return v != null ? v.ToString() : "";
            }
        }
        catch { return ""; }
    }

    public static string WindowsVersion()
    {
        const string nt = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string name = Reg(nt, "ProductName"), disp = Reg(nt, "DisplayVersion"), build = Reg(nt, "CurrentBuild"), ubr = Reg(nt, "UBR");
        int b;
        if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");   // the registry still says 10 on 11
        return (name.Length > 0 ? name : Environment.OSVersion.VersionString) + (disp.Length > 0 ? " " + disp : "") +
               " (build " + build + (ubr.Length > 0 ? "." + ubr : "") + ")" + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit");
    }

    public static string DotNetVersion()
    {
        string rel = Reg(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release");
        int r;
        string v = !int.TryParse(rel, out r) ? "?" : r >= 533320 ? "4.8.1" : r >= 528040 ? "4.8" : r >= 461808 ? "4.7.2" : r >= 461308 ? "4.7.1" :
                   r >= 460798 ? "4.7" : r >= 394802 ? "4.6.2" : "4.6 or older";
        return v + " (release " + rel + ", CLR " + Environment.Version + ")";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WAVEOUTCAPS
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwFormats;
        public ushort wChannels, wReserved1;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")]
    static extern int waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int waveOutGetDevCaps(IntPtr id, ref WAVEOUTCAPS caps, int size);

    // Sound outputs (names are cut at 31 letters by Windows). The first one is usually the default.
    static string AudioOutputs()
    {
        try
        {
            List<string> names = new List<string>();
            int n = waveOutGetNumDevs();
            for (int i = 0; i < n && i < 16; i++)
            {
                WAVEOUTCAPS c = new WAVEOUTCAPS();
                if (waveOutGetDevCaps((IntPtr)i, ref c, Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0) names.Add(c.szPname);
            }
            return names.Count == 0 ? "none found" : string.Join(" | ", names.ToArray());
        }
        catch (Exception ex) { return "? (" + ex.Message + ")"; }
    }

    static string Screens()
    {
        List<string> l = new List<string>();
        foreach (Screen s in Screen.AllScreens) l.Add(s.Bounds.Width + "x" + s.Bounds.Height + (s.Primary ? " (main)" : ""));
        return string.Join(", ", l.ToArray());
    }

    static int Dpi()
    {
        try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX; } catch { return 96; }
    }

    static string YesNo(bool b) { return b ? "yes" : "no"; }

    // "WebcastGiftMessage 12, WebcastLikeMessage 300" for every counter starting with prefix
    static string CountsWith(string prefix)
    {
        List<string> l = new List<string>();
        lock (gate)
        {
            List<string> keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) if (k.StartsWith(prefix, StringComparison.Ordinal)) l.Add(k.Substring(prefix.Length) + " " + counts[k]);
        }
        return l.Count == 0 ? "none" : string.Join(", ", l.ToArray());
    }

    static string NoteOf(string key) { lock (gate) { string v; return notes.TryGetValue(key, out v) && v.Length > 0 ? v : "-"; } }

    // ---------------- status and report ----------------

    // A few lines for the Debug page, updated every second.
    public static string Status(App a)
    {
        StringBuilder b = new StringBuilder();
        b.AppendLine("TikTok: " + a.TikTok.Current + " - " + a.TikTok.Detail);
        b.AppendLine("TikTok messages: " + CountsWith("tiktok.msg."));
        b.AppendLine("OBS: " + a.Obs.Current + " - " + a.Obs.Detail + ", actions done " + Get("obs.actions") + ", failed " + Get("obs.actions.failed"));
        b.AppendLine("Events: " + CountsWith("event."));
        b.AppendLine("Actions queued " + Get("actions.queued") + ", skipped (queue full) " + Get("actions.dropped") + ", cooling down " + Get("actions.cooling") +
                     ", waiting now " + a.Engine.Waiting);
        b.AppendLine("Keys: OK " + Get("keys.ok") + ", blocked by Windows " + Get("keys.blocked") + ", skipped (game not in front) " + Get("keys.notfront"));
        b.AppendLine("Sounds: played " + Get("sounds.played") + ", failed " + Get("sounds.failed") + ", counters changed by actions " + Get("counters.changed"));
        b.AppendLine("Widgets: " + a.S.Goals.Count + " goal(s), timer " + a.W.TimerState + " " + Widgets.ClockText(a.W.TimeLeftMs) + ", top lists " + a.W.SessionFans + " people this LIVE");
        b.AppendLine("Overlay: " + Get("overlay.state") + " requests, last " + Ago("overlay") + (a.ServerError != null ? " - SERVER ERROR: " + a.ServerError : ""));
        b.AppendLine("Hotkeys: hook " + (a.KeysOk ? "OK" : "NOT WORKING") + ", pressed " + Get("hotkey.pressed"));
        b.Append("Errors: " + Get("errors") + (Verbose ? "   (detailed logging is on)" : ""));
        return b.ToString();
    }

    public static string Report(App a)
    {
        Settings s = a.S;
        StringBuilder b = new StringBuilder();
        TimeSpan up = DateTime.Now - started;
        b.AppendLine("ClutchKit debug report - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - ClutchKit " + Program.Version);
        b.AppendLine("(No API key in here. It does contain the activity log, which has viewer names from your LIVE.)");

        b.AppendLine();
        b.AppendLine("== System ==");
        b.AppendLine("Windows: " + WindowsVersion());
        b.AppendLine(".NET Framework: " + DotNetVersion());
        int dpi = Dpi();
        b.AppendLine("Display: " + dpi + " DPI (" + (dpi * 100 / 96) + "%), screens: " + Screens());
        b.AppendLine("Windows language: " + System.Globalization.CultureInfo.CurrentUICulture.Name + ", format: " + System.Globalization.CultureInfo.CurrentCulture.Name +
                     ", ClutchKit language: " + L.Code + (s.Language.Length == 0 ? " (automatic)" : " (chosen)"));
        b.AppendLine("Running as administrator: " + YesNo(KeySender.SelfElevated) + ", installed: " + YesNo(Installer.IsInstalledCopy(Application.ExecutablePath)) +
                     ", data folder: " + Redact(a.Folder));
        b.AppendLine("Running for: " + (int)up.TotalHours + " h " + up.Minutes + " min, detailed logging: " + (Verbose ? "on" : "off"));
        b.AppendLine("Sound outputs: " + AudioOutputs());

        b.AppendLine();
        b.AppendLine("== Counters, hotkeys, overlay ==");
        int shown = 0;
        foreach (Counter c in s.Counters) if (c.Visible) shown++;
        b.AppendLine("Counters: " + s.Counters.Count + " (" + shown + " shown on stream)");
        foreach (Counter c in s.Counters)
            b.AppendLine("  " + c.DisplayName + " = " + c.Count + ", keys " + c.Keys[0] + " / " + c.Keys[1] + " / " + c.Keys[2] + (c.Visible ? "" : ", hidden") +
                         ", font " + c.Style.Font + ", animation " + c.Style.Anim);
        b.AppendLine("Keyboard hook: " + (a.KeysOk ? "OK" : "NOT WORKING") + ", hotkeys pressed: " + Get("hotkey.pressed"));
        b.AppendLine("Overlay mode: " + s.Mode + ", port " + s.Port + (a.ServerError != null ? ", SERVER ERROR: " + a.ServerError : ", server OK"));
        b.AppendLine("Overlay requests: state " + Get("overlay.state") + ", script " + Get("overlay.script") + ", other " + Get("overlay.other") +
                     ", last " + Ago("overlay") + ", from: " + NoteOf("overlay.agent"));

        b.AppendLine();
        b.AppendLine("== TikTok LIVE ==");
        b.AppendLine("State: " + a.TikTok.Current + " - " + Redact(a.TikTok.Detail));
        b.AppendLine("Username: " + (s.TikTokUser.Length > 0 ? "@" + s.TikTokUser : "(none)") + ", API key: " +
                     (s.TikTokKey.Length > 0 ? "set (" + s.TikTokKey.Length + " characters)" : "not set") + ", connect automatically: " + YesNo(s.AutoConnect));
        b.AppendLine("Connection attempts: " + Get("tiktok.connects") + ", connected: " + Get("tiktok.connected") + ", last close: " + NoteOf("tiktok.lastclose"));
        b.AppendLine("Close codes: " + CountsWith("tiktok.close."));
        b.AppendLine("Messages by type: " + CountsWith("tiktok.msg."));
        b.AppendLine("Messages ignored: " + CountsWith("tiktok.ignored."));
        b.AppendLine("Events: " + CountsWith("event."));

        b.AppendLine();
        b.AppendLine("== OBS ==");
        b.AppendLine("State: " + a.Obs.Current + " - " + Redact(a.Obs.Detail) + (a.Obs.Version.Length > 0 ? ", OBS " + a.Obs.Version : ""));
        b.AppendLine("Address: " + Redact(s.ObsHost) + ":" + s.ObsPort + ", password: " + (s.ObsPassword.Length > 0 ? "set" : "not set") + ", connect automatically: " + YesNo(s.ObsAuto));
        b.AppendLine("Connection attempts: " + Get("obs.connects") + ", connected: " + Get("obs.connected") + ", lists: " + NoteOf("obs.lists"));
        b.AppendLine("Requests: " + Get("obs.requests") + ", failed " + Get("obs.failed") + "; actions done " + Get("obs.actions") + ", failed " + Get("obs.actions.failed"));
        b.AppendLine("Browser source pages loaded: " + Get("overlay.obspage"));

        b.AppendLine();
        b.AppendLine("== Actions ==");
        b.AppendLine("Actions on: " + YesNo(s.ActionsOn) + ", on/off key: " + s.PauseKey + ", only in: " + (s.OnlyProcess.Length > 0 ? s.OnlyProcess : "(any program)") +
                     ", wait between: " + s.GapMs + " ms, queue max: " + s.MaxQueue + ", waiting now: " + a.Engine.Waiting);
        int groupsOn = 0;
        foreach (RuleGroup g in s.Groups) if (g.Enabled) groupsOn++;
        Dictionary<string, int> byTrigger = new Dictionary<string, int>(), byKind = new Dictionary<string, int>();
        int rulesOn = 0;
        List<string> problems = new List<string>();
        foreach (GiftRule r in s.Rules)
        {
            if (r.Enabled) rulesOn++;
            int n;
            byTrigger.TryGetValue(r.Trigger, out n); byTrigger[r.Trigger] = n + 1;
            byKind.TryGetValue(r.Kind, out n); byKind[r.Kind] = n + 1;
            string name = "\"" + r.Summary + "\"";
            if (!r.CanRun) problems.Add(name + ": nothing to do yet (no key / text / sound / scene)");
            else if (r.Kind == GiftRule.KindSound && !File.Exists(r.SoundFile)) problems.Add(name + ": sound file is missing (" + Redact(r.SoundFile) + ")");
            else if (r.Kind == GiftRule.KindCounter && s.Find(r.CounterId) == null) problems.Add(name + ": its counter was removed");
            else if (r.Kind == GiftRule.KindObs && r.Enabled && !a.Obs.Connected) problems.Add(name + ": OBS isn't connected");
            if (r.Enabled && !s.GroupOn(r.GroupId)) problems.Add(name + ": its topic is switched off");
        }
        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in byTrigger) parts.Add(kv.Key + " " + kv.Value);
        foreach (KeyValuePair<string, int> kv in byKind) parts.Add(kv.Key + " " + kv.Value);
        b.AppendLine("Topics: " + s.Groups.Count + " (" + groupsOn + " on), actions: " + s.Rules.Count + " (" + rulesOn + " on) - " + string.Join(", ", parts.ToArray()));
        b.AppendLine("Queued " + Get("actions.queued") + ", skipped (queue full) " + Get("actions.dropped") + ", cooling down " + Get("actions.cooling"));
        b.AppendLine("Keys: OK " + Get("keys.ok") + ", blocked by Windows " + Get("keys.blocked") + ", skipped (game not in front) " + Get("keys.notfront") +
                     ", last program in front: " + NoteOf("keys.front"));
        b.AppendLine("Sounds: played " + Get("sounds.played") + ", failed " + Get("sounds.failed") + ", counters changed by actions: " + Get("counters.changed"));
        b.AppendLine("Profiles: " + s.Profiles.Count);
        foreach (string p in problems.Count > 40 ? problems.GetRange(0, 40) : problems) b.AppendLine("  ! " + p);

        b.AppendLine();
        b.AppendLine("== Widgets ==");
        foreach (Goal g in s.Goals)
            b.AppendLine("Goal " + g.Id + " \"" + g.Title + "\": " + g.Source + (g.Source == Goal.FromCounter ? " " + g.CounterId : "") + ", " + a.W.GoalValue(g) +
                         " / " + g.Target + (g.Step > 0 ? ", raise by " + g.Step : "") + (g.Restart ? ", starts over" : "") + (g.Visible ? "" : ", hidden"));
        if (s.Goals.Count == 0) b.AppendLine("Goals: none");
        b.AppendLine("Goals reached: " + Get("goals.reached"));
        b.AppendLine("Timer: " + a.W.TimerState + ", " + Widgets.ClockText(a.W.TimeLeftMs) + " left, starts at " + s.Timer.StartMin + " min, max " + s.Timer.MaxHours +
                     " h, seconds per coin " + s.Timer.PerCoin + " / follow " + s.Timer.PerFollow + " / share " + s.Timer.PerShare + " / 100 likes " + s.Timer.Per100Likes +
                     (s.Timer.Visible ? "" : ", hidden"));
        b.AppendLine("Top lists: " + a.W.SessionFans + " people this LIVE, " + a.W.AllTimeFans + " all time; gifters " + (s.TopGifters.AllTime ? "all time" : "this LIVE") +
                     " top " + s.TopGifters.Count + ", likers " + (s.TopLikers.AllTime ? "all time" : "this LIVE") + " top " + s.TopLikers.Count);

        b.AppendLine();
        b.AppendLine("== Errors (last 50) ==");
        lock (gate) { if (errors.Count == 0) b.AppendLine("none"); foreach (string e in errors) b.AppendLine(e); }

        b.AppendLine();
        b.AppendLine("== Layout check ==");
        b.AppendLine(LastLayoutCheck.Length > 0 ? LastLayoutCheck.TrimEnd() : "not run (Debug page > Check layout in all languages)");

        b.AppendLine();
        b.AppendLine("== Activity log ==");
        foreach (string line in a.LogLines) b.AppendLine(line);

        b.AppendLine();
        b.AppendLine("== TikTok message samples (detailed logging) ==");
        lock (gate)
        {
            if (samples.Count == 0) b.AppendLine(Verbose ? "none yet" : "turn on detailed logging and connect to collect them");
            foreach (KeyValuePair<string, List<string>> kv in samples)
                foreach (string j in kv.Value) b.AppendLine(kv.Key + ": " + j);
        }
        return Redact(b.ToString());
    }
}
