// ClutchKit - gift actions: when a viewer sends a gift, press keys in your game (or type text, e.g. a command).
// ----------------------------------------------------------------------------------
// GiftRule   - one "when this gift arrives, do this" rule (saved in settings.ini as rule.<id>.*)
// KeySender  - presses keys / types text with SendInput, the way a real keyboard does (scan codes)
// GiftEngine - matches gifts to rules (including gift combos) and runs them one after another from a queue

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

// One gift as it arrives from TikTok (or from the "Send test gift" button).
public class GiftEvent
{
    public string User = "", Nick = "", UserId = "";
    public string GiftName = "", GiftId = "", GroupId = "";
    public int Coins;            // coins per single gift
    public int RepeatCount = 1;  // how many so far in this combo
    public bool RepeatEnd = true;
    public bool Streakable;      // combo gift: TikTok sends updates while the combo runs, then a last one
    public bool Test;

    public string Who { get { return User.Length > 0 ? "@" + User : (Nick.Length > 0 ? Nick : "someone"); } }
}

public class GiftRule
{
    public const string KindKey = "key", KindText = "text";
    public static readonly string[] KindNames = { "Press a key", "Type text" };

    public int Id;
    public bool Enabled = true;
    public string Gifts = "Rose";      // gift names, comma separated; empty = any gift
    public int MinCoins = 0;           // only gifts worth at least this many coins each
    public string Kind = KindKey;
    public Hotkey Key = new Hotkey(0, 0);   // the key to press (text: optional key pressed first, e.g. T to open chat)
    public int HoldMs = 60;
    public string Text = "";
    public bool Enter = true;          // text: press Enter after typing
    public int Times = 1;              // how many times per trigger
    public bool PerGift = true;        // true: a x10 combo runs it 10 times; false: once per send

    public GiftRule Clone() { return (GiftRule)MemberwiseClone(); }

    public bool AnyGift { get { string g = Gifts.Trim(); return g.Length == 0 || g == "*"; } }

    public bool Matches(GiftEvent g)
    {
        if (!Enabled) return false;
        if (MinCoins > 0 && g.Coins < MinCoins) return false;
        if (AnyGift) return true;
        foreach (string part in Gifts.Split(','))
            if (part.Trim().Length > 0 && string.Equals(part.Trim(), g.GiftName.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public bool CanRun { get { return Kind == KindText ? Text.Length > 0 : Key.IsSet; } }

    public string TriggerText
    {
        get
        {
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
        if (kv.TryGetValue(pre + "gifts", out v)) r.Gifts = v.Trim();
        if (kv.TryGetValue(pre + "mincoins", out v)) r.MinCoins = Settings.ParseInt(v, 0, 0, 1000000);
        if (kv.TryGetValue(pre + "kind", out v)) r.Kind = v.Trim() == KindText ? KindText : KindKey;
        if (kv.TryGetValue(pre + "key", out v)) r.Key = Hotkey.Parse(v.Trim(), r.Key);
        if (kv.TryGetValue(pre + "holdms", out v)) r.HoldMs = Settings.ParseInt(v, r.HoldMs, 10, 10000);
        if (kv.TryGetValue(pre + "text", out v)) r.Text = v;
        if (kv.TryGetValue(pre + "enter", out v)) r.Enter = Settings.ParseBool(v, true);
        if (kv.TryGetValue(pre + "times", out v)) r.Times = Settings.ParseInt(v, 1, 1, 100);
        if (kv.TryGetValue(pre + "pergift", out v)) r.PerGift = Settings.ParseBool(v, true);
        return r;
    }

    public void Write(StringBuilder b)
    {
        string pre = "rule." + Id + ".";
        b.AppendLine(pre + "on=" + Enabled);
        b.AppendLine(pre + "gifts=" + Gifts.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "minCoins=" + MinCoins);
        b.AppendLine(pre + "kind=" + Kind);
        b.AppendLine(pre + "key=" + Key.Serialize());
        b.AppendLine(pre + "holdMs=" + HoldMs);
        b.AppendLine(pre + "text=" + Text.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "enter=" + Enter);
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

// Turns gifts into key presses. Gifts are matched on the UI thread; the keys are pressed on a
// worker thread, one action at a time, so a 99x combo can't flood the game in one frame.
public class GiftEngine
{
    class Job { public GiftRule Rule; public GiftEvent Gift; }
    class Streak { public int Count; public DateTime Seen; }

    readonly App app;
    readonly Queue<Job> queue = new Queue<Job>();
    readonly object gate = new object();
    readonly AutoResetEvent wake = new AutoResetEvent(false);
    readonly Dictionary<string, Streak> streaks = new Dictionary<string, Streak>();
    string warnedProcess = "";

    public event EventHandler QueueChanged;   // raised on the UI thread
    public event Action<string> ElevationProblem;   // the program in front runs as admin (UI thread)

    public GiftEngine(App app)
    {
        this.app = app;
        Thread t = new Thread(Worker);
        t.IsBackground = true;
        t.Name = "Gift actions";
        t.Start();
    }

    public int Waiting { get { lock (gate) return queue.Count; } }

    public void Clear()
    {
        lock (gate) queue.Clear();
        RaiseQueue();
    }

    // How many new gifts this event adds. Combo gifts arrive several times (x1, x2, ... and a final
    // message), so only the increase since the last message for that combo counts.
    public int NewGifts(GiftEvent g, out bool firstOfSend)
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

    // Called on the UI thread for every gift message.
    public void OnGift(GiftEvent g)
    {
        bool first;
        int added = NewGifts(g, out first);
        if (added == 0 && !first) return;   // the closing message of a combo we already counted

        string what = g.Who + " sent " + (g.GiftName.Length > 0 ? g.GiftName : "a gift") + (added > 1 ? " x" + added : "") +
                      (g.Coins > 0 ? " (" + g.Coins * Math.Max(1, added) + " coins)" : "") + (g.Test ? " [test]" : "");
        if (!app.S.ActionsOn) { app.Log(what + " - gift actions are paused"); return; }

        int queued = 0, dropped = 0;
        foreach (GiftRule r in app.S.Rules)
        {
            if (!r.Matches(g) || !r.CanRun) continue;
            int runs = r.PerGift ? added : (first ? 1 : 0);
            for (int i = 0; i < runs; i++)
            {
                if (Enqueue(r, g)) queued++; else dropped++;
            }
        }
        if (queued == 0 && dropped == 0) app.Log(what + " - no gift action for it");
        else app.Log(what + " - " + queued + " action" + (queued == 1 ? "" : "s") + " queued" +
                     (dropped > 0 ? ", " + dropped + " skipped (queue full)" : ""));
    }

    // "Test" button on a gift action: runs it once, whether or not gift actions are paused.
    public void RunTest(GiftRule r)
    {
        GiftEvent g = new GiftEvent();
        g.Test = true;
        g.GiftName = r.AnyGift ? "test gift" : r.Gifts.Split(',')[0].Trim();
        Enqueue(r, g);
    }

    bool Enqueue(GiftRule r, GiftEvent g)
    {
        lock (gate)
        {
            if (queue.Count >= Math.Max(1, app.S.MaxQueue)) return false;
            Job j = new Job();
            j.Rule = r.Clone();   // the settings window may change the rule while it waits
            j.Gift = g;
            queue.Enqueue(j);
        }
        wake.Set();
        RaiseQueue();
        return true;
    }

    void RaiseQueue()
    {
        app.Ui(delegate { if (QueueChanged != null) QueueChanged(this, EventArgs.Empty); });
    }

    void Worker()
    {
        while (true)
        {
            wake.WaitOne();
            while (true)
            {
                Job j;
                lock (gate)
                {
                    if (queue.Count == 0) break;
                    j = queue.Dequeue();
                }
                RaiseQueue();
                try { Run(j); } catch (Exception ex) { app.Log("Gift action failed: " + ex.Message); }
            }
        }
    }

    void Run(Job j)
    {
        GiftRule r = j.Rule;
        if (!j.Gift.Test && !app.S.ActionsOn) return;
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
                    "Fix: Gift actions > Restart as administrator.");
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
        app.Log((ok ? "Did: " : "Windows blocked: ") + r.ActionText + " (" + r.TriggerText + (j.Gift.Test ? ", test" : " from " + j.Gift.Who) + ")" +
                (front.Length > 0 ? " -> " + front : ""));
    }
}
