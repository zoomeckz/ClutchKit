// ClutchKit - actions: when something happens on your LIVE (a gift, follow, share, join or likes),
// play a sound, press keys in your game or type text (e.g. a command).
// ----------------------------------------------------------------------------------
// LiveEvent   - one thing that happened on the LIVE (or a test event)
// GiftRule    - one "when this happens, do this" rule (saved in settings.ini as rule.<id>.*)
// KeySender   - presses keys / types text with SendInput, the way a real keyboard does (scan codes)
// SoundPlayer - plays MP3/WAV files with Windows' own media player engine (MCI), with volume
// GiftEngine  - matches events to rules (incl. gift combos, like milestones, cooldowns) and runs them
//               from two queues: one for sounds, one for keys - so a sound never waits for key presses

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

// One thing that happened on the LIVE, as it arrives from TikTok (or from a test button).
public class LiveEvent
{
    public const string Gift = "gift", Follow = "follow", Share = "share", Join = "join", Likes = "likes";

    public string Type = Gift;
    public string User = "", Nick = "", UserId = "";
    public string GiftName = "", GiftId = "", GroupId = "";
    public int Coins;            // coins per single gift
    public int RepeatCount = 1;  // gifts: how many so far in this combo; likes: how many likes in this message
    public bool RepeatEnd = true;
    public bool Streakable;      // combo gift: TikTok sends updates while the combo runs, then a last one
    public bool Test;

    public string Who { get { return User.Length > 0 ? "@" + User : (Nick.Length > 0 ? Nick : "someone"); } }
}

// One rule: when <trigger> happens, do <kind> (press a key / type text / play a sound).
public class GiftRule
{
    public const string KindKey = "key", KindText = "text", KindSound = "sound";
    public static readonly string[] Kinds = { KindKey, KindText, KindSound };
    public static readonly string[] KindNames = { "Press a key", "Type text", "Play a sound" };
    public static readonly string[] Triggers = { LiveEvent.Gift, LiveEvent.Follow, LiveEvent.Share, LiveEvent.Join, LiveEvent.Likes };
    public static readonly string[] TriggerNames = { "Gift", "Follow", "Share", "Viewer joins", "Likes" };

    public int Id;
    public bool Enabled = true;
    public string Trigger = LiveEvent.Gift;
    public string Gifts = "Rose";      // gift names, comma separated; empty = any gift
    public int MinCoins = 0;           // only gifts worth at least this many coins each
    public int EveryLikes = 100;       // likes: run once every this many likes
    public int CooldownSec = 0;        // don't run again within this many seconds
    public string Kind = KindKey;
    public Hotkey Key = new Hotkey(0, 0);   // the key to press (text: optional key pressed first, e.g. T to open chat)
    public int HoldMs = 60;
    public string Text = "";
    public bool Enter = true;          // text: press Enter after typing
    public string SoundFile = "";
    public int Volume = 80;            // 0-100
    public int Times = 1;              // how many times per trigger
    public bool PerGift = true;        // gifts: true = a x10 combo runs it 10 times; false = once per send

    public GiftRule Clone() { return (GiftRule)MemberwiseClone(); }

    public bool AnyGift { get { string g = Gifts.Trim(); return g.Length == 0 || g == "*"; } }

    public bool Matches(LiveEvent e)
    {
        if (!Enabled || e.Type != Trigger) return false;
        if (Trigger != LiveEvent.Gift) return true;
        if (MinCoins > 0 && e.Coins < MinCoins) return false;
        if (AnyGift) return true;
        foreach (string part in Gifts.Split(','))
            if (part.Trim().Length > 0 && string.Equals(part.Trim(), e.GiftName.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public bool CanRun
    {
        get
        {
            if (Kind == KindText) return Text.Length > 0;
            if (Kind == KindSound) return SoundFile.Length > 0;
            return Key.IsSet;
        }
    }

    public string TriggerText
    {
        get
        {
            switch (Trigger)
            {
                case LiveEvent.Follow: return "follow";
                case LiveEvent.Share: return "share";
                case LiveEvent.Join: return "viewer joins";
                case LiveEvent.Likes: return "every " + EveryLikes + " likes";
            }
            string t = AnyGift ? "any gift" : Gifts.Trim();
            if (MinCoins > 0) t += " (" + MinCoins + "+ coins)";
            return t;
        }
    }

    public string ActionText
    {
        get
        {
            string times = Times > 1 ? " x" + Times : "";
            if (Kind == KindSound) return "play " + (SoundFile.Length > 0 ? Path.GetFileName(SoundFile) : "a sound") + times;
            if (Kind == KindText)
                return (Key.IsSet ? Key + ", then type \"" : "type \"") + Text + "\"" + (Enter ? " + Enter" : "") + times;
            return "press " + Key + times;
        }
    }

    public static GiftRule Read(Dictionary<string, string> kv, int id)
    {
        GiftRule r = new GiftRule();
        r.Id = id;
        string pre = "rule." + id + ".", v;
        if (kv.TryGetValue(pre + "on", out v)) r.Enabled = Settings.ParseBool(v, true);
        if (kv.TryGetValue(pre + "trigger", out v) && Array.IndexOf(Triggers, v.Trim()) >= 0) r.Trigger = v.Trim();
        if (kv.TryGetValue(pre + "gifts", out v)) r.Gifts = v.Trim();
        if (kv.TryGetValue(pre + "mincoins", out v)) r.MinCoins = Settings.ParseInt(v, 0, 0, 1000000);
        if (kv.TryGetValue(pre + "everylikes", out v)) r.EveryLikes = Settings.ParseInt(v, r.EveryLikes, 1, 10000000);
        if (kv.TryGetValue(pre + "cooldown", out v)) r.CooldownSec = Settings.ParseInt(v, 0, 0, 3600);
        if (kv.TryGetValue(pre + "kind", out v) && Array.IndexOf(Kinds, v.Trim()) >= 0) r.Kind = v.Trim();
        if (kv.TryGetValue(pre + "key", out v)) r.Key = Hotkey.Parse(v.Trim(), r.Key);
        if (kv.TryGetValue(pre + "holdms", out v)) r.HoldMs = Settings.ParseInt(v, r.HoldMs, 10, 10000);
        if (kv.TryGetValue(pre + "text", out v)) r.Text = v;
        if (kv.TryGetValue(pre + "enter", out v)) r.Enter = Settings.ParseBool(v, true);
        if (kv.TryGetValue(pre + "sound", out v)) r.SoundFile = v.Trim();
        if (kv.TryGetValue(pre + "volume", out v)) r.Volume = Settings.ParseInt(v, r.Volume, 0, 100);
        if (kv.TryGetValue(pre + "times", out v)) r.Times = Settings.ParseInt(v, 1, 1, 100);
        if (kv.TryGetValue(pre + "pergift", out v)) r.PerGift = Settings.ParseBool(v, true);
        return r;
    }

    public void Write(StringBuilder b)
    {
        string pre = "rule." + Id + ".";
        b.AppendLine(pre + "on=" + Enabled);
        b.AppendLine(pre + "trigger=" + Trigger);
        b.AppendLine(pre + "gifts=" + Gifts.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "minCoins=" + MinCoins);
        b.AppendLine(pre + "everyLikes=" + EveryLikes);
        b.AppendLine(pre + "cooldown=" + CooldownSec);
        b.AppendLine(pre + "kind=" + Kind);
        b.AppendLine(pre + "key=" + Key.Serialize());
        b.AppendLine(pre + "holdMs=" + HoldMs);
        b.AppendLine(pre + "text=" + Text.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "enter=" + Enter);
        b.AppendLine(pre + "sound=" + SoundFile);
        b.AppendLine(pre + "volume=" + Volume);
        b.AppendLine(pre + "times=" + Times);
        b.AppendLine(pre + "perGift=" + PerGift);
    }
}

// Presses keys like a real keyboard: hardware scan codes, which games read more reliably than
// "virtual" keys. Keys always go to the window that is in front.
public static class KeySender
{
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4, KEYEVENTF_SCANCODE = 0x8;
    public const int VK_RETURN = 0x0D;

    // Marks keys that ClutchKit pressed itself, so its own hotkeys can tell them apart.
    public static readonly IntPtr Tag = new IntPtr(0x434B);

    static bool IsExtended(int vk)
    {
        switch (vk)
        {
            case 0x21: case 0x22: case 0x23: case 0x24:             // Page Up/Down, End, Home
            case 0x25: case 0x26: case 0x27: case 0x28:             // arrows
            case 0x2C: case 0x2D: case 0x2E:                        // Print Screen, Insert, Delete
            case 0x5B: case 0x5C: case 0x5D:                        // Windows keys, menu key
            case 0x6F: case 0x90: case 0xA3: case 0xA5:             // Num /, Num Lock, right Ctrl, right Alt
                return true;
        }
        return false;
    }

    static INPUT KeyInput(int vk, bool up)
    {
        INPUT i = new INPUT();
        i.type = INPUT_KEYBOARD;
        uint scan = MapVirtualKey((uint)vk, 0);
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (scan != 0) flags |= KEYEVENTF_SCANCODE;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        i.u.ki.wVk = (ushort)vk;
        i.u.ki.wScan = (ushort)scan;
        i.u.ki.dwFlags = flags;
        i.u.ki.dwExtraInfo = Tag;
        return i;
    }

    static INPUT CharInput(char c, bool up)
    {
        INPUT i = new INPUT();
        i.type = INPUT_KEYBOARD;
        i.u.ki.wScan = c;
        i.u.ki.dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0);
        i.u.ki.dwExtraInfo = Tag;
        return i;
    }

    static bool Send(List<INPUT> list)
    {
        if (list.Count == 0) return true;
        INPUT[] a = list.ToArray();
        return SendInput((uint)a.Length, a, Marshal.SizeOf(typeof(INPUT))) == a.Length;
    }

    static List<int> ModKeys(Hotkey hk)
    {
        List<int> m = new List<int>();
        if ((hk.Mods & Hotkey.Ctrl) != 0) m.Add(0xA2);
        if ((hk.Mods & Hotkey.Alt) != 0) m.Add(0xA4);
        if ((hk.Mods & Hotkey.Shift) != 0) m.Add(0xA0);
        if ((hk.Mods & Hotkey.Win) != 0) m.Add(0x5B);
        return m;
    }

    // Press a key (with its Ctrl/Alt/Shift/Win) and hold it for holdMs - games often miss key presses shorter than a frame.
    public static bool Press(Hotkey hk, int holdMs)
    {
        if (!hk.IsSet) return false;
        List<int> mods = ModKeys(hk);
        List<INPUT> down = new List<INPUT>(), up = new List<INPUT>();
        foreach (int m in mods) down.Add(KeyInput(m, false));
        down.Add(KeyInput(hk.Vk, false));
        up.Add(KeyInput(hk.Vk, true));
        for (int i = mods.Count - 1; i >= 0; i--) up.Add(KeyInput(mods[i], true));
        bool ok = Send(down);
        Thread.Sleep(Math.Max(10, holdMs));
        return Send(up) && ok;
    }

    // Type text into whatever has focus (a chat box, a console), one character at a time.
    public static bool Type(string text, bool enter)
    {
        bool ok = true;
        foreach (char c in text ?? "")
        {
            if (c == '\r' || c == '\n') continue;
            List<INPUT> l = new List<INPUT>();
            l.Add(CharInput(c, false));
            l.Add(CharInput(c, true));
            ok &= Send(l);
            Thread.Sleep(12);
        }
        if (enter)
        {
            Thread.Sleep(60);
            ok &= Press(new Hotkey(0, VK_RETURN), 40);
        }
        return ok;
    }

    public static bool SelfElevated
    {
        get
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    // The program whose window is in front ("" if unknown), and whether it runs as administrator:
    // 1 = yes (or it's protected, e.g. by anti-cheat), 0 = no, -1 = unknown.
    public static string Foreground(out int elevated)
    {
        elevated = -1;
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        if (pid == 0) return "";
        string name = "";
        try { using (Process p = Process.GetProcessById((int)pid)) name = p.ProcessName; } catch { }
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return name;
        try
        {
            IntPtr token;
            if (!OpenProcessToken(h, 0x0008 /* TOKEN_QUERY */, out token)) { elevated = 1; return name; }
            try
            {
                int value, len;
                if (GetTokenInformation(token, 20 /* TokenElevation */, out value, 4, out len)) elevated = value != 0 ? 1 : 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(h); }
        return name;
    }

    public static string CleanProcessName(string s)
    {
        s = (s ?? "").Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
        return s;
    }
}


// Plays sound files (MP3, WAV, WMA, ...) through Windows' own media engine (MCI) - no extra
// software needed. The sound goes to your default speakers/headset, which LIVE Studio picks up
// as desktop audio. Call only from one worker thread.
public static class SoundPlayer
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder ret, int retLength, IntPtr callback);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool mciGetErrorString(int error, StringBuilder text, int length);

    public static readonly string[] Extensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };
    static int counter;

    static int Send(string cmd, StringBuilder ret) { return mciSendString(cmd, ret, ret == null ? 0 : ret.Capacity, IntPtr.Zero); }

    static string Error(int code)
    {
        StringBuilder b = new StringBuilder(256);
        return mciGetErrorString(code, b, b.Capacity) ? b.ToString() : "error " + code;
    }

    // Plays the file and returns when it has finished (or stop() says to stop). null = OK, else what went wrong.
    public static string Play(string path, int volume, Func<bool> stop)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "the sound file isn't there anymore (" + path + ")";
        if (path.IndexOf('"') >= 0) return "the file name can't contain quotes";
        string alias = "ck" + Interlocked.Increment(ref counter);
        int err = Send("open \"" + path + "\" type mpegvideo alias " + alias, null);
        if (err != 0) return Error(err);
        try
        {
            Send("setaudio " + alias + " volume to " + Math.Max(0, Math.Min(100, volume)) * 10, null);
            err = Send("play " + alias, null);
            if (err != 0) return Error(err);
            DateTime start = DateTime.UtcNow;
            StringBuilder mode = new StringBuilder(64);
            while (true)
            {
                Thread.Sleep(40);
                if (stop()) break;
                double ms = (DateTime.UtcNow - start).TotalMilliseconds;
                if (ms > 10 * 60 * 1000) break;   // safety: never block the queue longer than 10 minutes
                mode.Length = 0;
                if (Send("status " + alias + " mode", mode) != 0) break;
                string m = mode.ToString();
                if (m != "playing" && m != "seeking" && m != "not ready" && ms > 300) break;
            }
        }
        finally { Send("close " + alias, null); }
        return null;
    }
}

// Turns LIVE events into actions. Events are matched on the UI thread; the actions run on two
// worker threads (keys/text, sounds), one at a time each, so a 99x combo can't flood anything.
public class GiftEngine
{
    class Job { public GiftRule Rule; public LiveEvent Event; }
    class Streak { public int Count; public DateTime Seen; }

    // one queue + worker thread
    class Lane
    {
        public readonly Queue<Job> Jobs = new Queue<Job>();
        public readonly AutoResetEvent Wake = new AutoResetEvent(false);
    }

    readonly App app;
    readonly object gate = new object();
    readonly Lane keys = new Lane(), sounds = new Lane();
    readonly Dictionary<string, Streak> streaks = new Dictionary<string, Streak>();
    readonly Dictionary<int, DateTime> lastRun = new Dictionary<int, DateTime>();
    volatile bool stopSound;
    long likeTotal;
    string warnedProcess = "";

    public event EventHandler QueueChanged;   // raised on the UI thread
    public event Action<string> ElevationProblem;   // the program in front runs as admin (UI thread)

    public GiftEngine(App app)
    {
        this.app = app;
        StartWorker(keys, "Key actions");
        StartWorker(sounds, "Sound actions");
    }

    void StartWorker(Lane lane, string name)
    {
        Thread t = new Thread(delegate () { Worker(lane); });
        t.IsBackground = true;
        t.Name = name;
        t.Start();
    }

    public int Waiting { get { lock (gate) return keys.Jobs.Count + sounds.Jobs.Count; } }
    public long LikeTotal { get { return likeTotal; } }

    // Empties both queues and stops the sound that is playing.
    public void Clear()
    {
        lock (gate) { keys.Jobs.Clear(); sounds.Jobs.Clear(); }
        stopSound = true;
        sounds.Wake.Set();
        RaiseQueue();
    }

    // How many new gifts this event adds. Combo gifts arrive several times (x1, x2, ... and a final
    // message), so only the increase since the last message for that combo counts.
    public int NewGifts(LiveEvent g, out bool firstOfSend)
    {
        if (!g.Streakable)
        {
            firstOfSend = true;
            return Math.Max(1, g.RepeatCount);
        }
        string key = g.UserId + "|" + g.User + "|" + g.GiftId + "|" + g.GiftName + "|" + g.GroupId;
        DateTime now = DateTime.UtcNow;
        List<string> old = new List<string>();
        foreach (KeyValuePair<string, Streak> kv in streaks) if ((now - kv.Value.Seen).TotalMinutes > 5) old.Add(kv.Key);
        foreach (string k in old) streaks.Remove(k);

        Streak s;
        firstOfSend = !streaks.TryGetValue(key, out s);
        int last = firstOfSend ? 0 : s.Count;
        int added = Math.Max(0, g.RepeatCount - last);
        if (g.RepeatEnd) streaks.Remove(key);
        else
        {
            if (s == null) { s = new Streak(); streaks[key] = s; }
            s.Count = Math.Max(last, g.RepeatCount);
            s.Seen = now;
        }
        return added;
    }

    // Called on the UI thread for everything that happens on the LIVE.
    public void OnEvent(LiveEvent e)
    {
        switch (e.Type)
        {
            case LiveEvent.Gift: OnGift(e); break;
            case LiveEvent.Likes: OnLikes(e); break;
            default: OnSimple(e); break;
        }
    }

    void OnGift(LiveEvent g)
    {
        bool first;
        int added = NewGifts(g, out first);
        if (added == 0 && !first) return;   // the closing message of a combo we already counted

        string what = g.Who + " sent " + (g.GiftName.Length > 0 ? g.GiftName : "a gift") + (added > 1 ? " x" + added : "") +
                      (g.Coins > 0 ? " (" + g.Coins * Math.Max(1, added) + " coins)" : "") + (g.Test ? " [test]" : "");
        if (!app.S.ActionsOn) { app.Log(what + " - actions are paused"); return; }
        string result = RunRules(g, delegate (GiftRule r) { return r.PerGift && r.Kind != GiftRule.KindSound ? added : (first ? 1 : 0); });
        app.Log(what + " - " + (result ?? "no action for it"));
    }

    void OnSimple(LiveEvent e)
    {
        string what = e.Who + (e.Type == LiveEvent.Follow ? " followed you" : e.Type == LiveEvent.Share ? " shared your LIVE" : " joined") +
                      (e.Test ? " [test]" : "");
        if (!app.S.ActionsOn) { if (e.Type != LiveEvent.Join) app.Log(what + " - actions are paused"); return; }
        string result = RunRules(e, delegate (GiftRule r) { return 1; });
        if (result != null) app.Log(what + " - " + result);
        else if (e.Type != LiveEvent.Join) app.Log(what);   // joins are too many to log one by one
    }

    void OnLikes(LiveEvent e)
    {
        long before = likeTotal;
        likeTotal += Math.Max(1, e.RepeatCount);
        long now = likeTotal;
        if (!app.S.ActionsOn) return;
        string result = RunRules(e, delegate (GiftRule r)
        {
            long every = Math.Max(1, r.EveryLikes);
            return (int)Math.Min(100, now / every - before / every);
        });
        if (result != null) app.Log(now + " likes" + (e.Test ? " [test]" : "") + " - " + result);
    }

    // Queues every matching rule runsFor(rule) times (cooldowns applied). Returns a log text, or null if nothing ran.
    string RunRules(LiveEvent e, Func<GiftRule, int> runsFor)
    {
        int queued = 0, dropped = 0, cooling = 0;
        DateTime now = DateTime.UtcNow;
        foreach (GiftRule r in app.S.Rules)
        {
            if (!r.Matches(e) || !r.CanRun) continue;
            int runs = runsFor(r);
            if (runs <= 0) continue;
            DateTime last;
            if (r.CooldownSec > 0)
            {
                if (lastRun.TryGetValue(r.Id, out last) && (now - last).TotalSeconds < r.CooldownSec) { cooling++; continue; }
                runs = 1;
            }
            lastRun[r.Id] = now;
            for (int i = 0; i < runs; i++)
            {
                if (Enqueue(r, e)) queued++; else dropped++;
            }
        }
        if (queued == 0 && dropped == 0)
            return cooling > 0 && e.Type != LiveEvent.Join ? "waiting for cooldown" : null;
        return queued + " action" + (queued == 1 ? "" : "s") + " queued" + (dropped > 0 ? ", " + dropped + " skipped (queue full)" : "");
    }

    // Test button on a rule: runs it once, whether or not actions are paused or cooling down.
    public void RunTest(GiftRule r)
    {
        LiveEvent e = new LiveEvent();
        e.Test = true;
        e.Type = r.Trigger;
        e.GiftName = r.Trigger == LiveEvent.Gift ? (r.AnyGift ? "test gift" : r.Gifts.Split(',')[0].Trim()) : "";
        Enqueue(r, e);
    }

    bool Enqueue(GiftRule r, LiveEvent e)
    {
        Lane lane = r.Kind == GiftRule.KindSound ? sounds : keys;
        lock (gate)
        {
            if (lane.Jobs.Count >= Math.Max(1, app.S.MaxQueue)) return false;
            Job j = new Job();
            j.Rule = r.Clone();   // the settings window may change the rule while it waits
            j.Event = e;
            lane.Jobs.Enqueue(j);
        }
        lane.Wake.Set();
        RaiseQueue();
        return true;
    }

    void RaiseQueue()
    {
        app.Ui(delegate { if (QueueChanged != null) QueueChanged(this, EventArgs.Empty); });
    }

    void Worker(Lane lane)
    {
        while (true)
        {
            lane.Wake.WaitOne();
            while (true)
            {
                Job j;
                lock (gate)
                {
                    if (lane.Jobs.Count == 0) break;
                    j = lane.Jobs.Dequeue();
                }
                if (lane == sounds) stopSound = false;
                RaiseQueue();
                try
                {
                    if (lane == sounds) RunSound(j); else RunKeys(j);
                }
                catch (Exception ex) { app.Log("Action failed: " + ex.Message); }
            }
        }
    }

    string Source(Job j) { return j.Rule.TriggerText + (j.Event.Test ? ", test" : " from " + j.Event.Who); }

    void RunSound(Job j)
    {
        GiftRule r = j.Rule;
        if (!j.Event.Test && !app.S.ActionsOn) return;
        for (int i = 0; i < r.Times && !stopSound; i++)
        {
            string error = SoundPlayer.Play(r.SoundFile, r.Volume, delegate { return stopSound; });
            if (error != null) { app.Log("Couldn't play " + Path.GetFileName(r.SoundFile) + ": " + error); return; }
        }
        app.Log((stopSound ? "Stopped: " : "Played: ") + Path.GetFileName(r.SoundFile) + " (" + Source(j) + ")");
    }

    void RunKeys(Job j)
    {
        GiftRule r = j.Rule;
        if (!j.Event.Test && !app.S.ActionsOn) return;
        int elevated;
        string front = KeySender.Foreground(out elevated);
        string only = KeySender.CleanProcessName(app.S.OnlyProcess);
        if (only.Length > 0 && !string.Equals(front, only, StringComparison.OrdinalIgnoreCase))
        {
            app.Log("Skipped \"" + r.ActionText + "\": " + only + " isn't in front" + (front.Length > 0 ? " (" + front + " is)" : ""));
            Thread.Sleep(20);
            return;
        }
        if (elevated == 1 && !KeySender.SelfElevated && front != warnedProcess)
        {
            warnedProcess = front;
            app.Log("Warning: " + front + " runs as administrator, so Windows may block ClutchKit's keys. " +
                    "Fix: Actions > Restart as administrator.");
            app.Ui(delegate { if (ElevationProblem != null) ElevationProblem(front); });
        }

        bool ok = true;
        int gap = Math.Max(0, app.S.GapMs);
        if (r.Kind == GiftRule.KindText)
        {
            for (int i = 0; i < r.Times; i++)
            {
                if (r.Key.IsSet) { ok &= KeySender.Press(r.Key, 40); Thread.Sleep(250); }   // e.g. T opens the chat
                ok &= KeySender.Type(r.Text, r.Enter);
                Thread.Sleep(gap);
            }
        }
        else
        {
            for (int i = 0; i < r.Times; i++)
            {
                ok &= KeySender.Press(r.Key, r.HoldMs);
                Thread.Sleep(gap);
            }
        }
        app.Log((ok ? "Did: " : "Windows blocked: ") + r.ActionText + " (" + Source(j) + ")" + (front.Length > 0 ? " -> " + front : ""));
    }
}
