// ClutchKit - actions: when something happens on your LIVE (a gift, follow, share, join, likes or a
// chat command like !boom), play a sound, press keys in your game, type text or change a counter.
// ----------------------------------------------------------------------------------
// LiveEvent   - one thing that happened on the LIVE (or a test event)
// GiftRule    - one "when this happens, do this" rule (saved in settings.ini as rule.<id>.*)
// Profile     - a saved set of topics that are on (e.g. one per game)
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
    public const string Gift = "gift", Follow = "follow", Share = "share", Join = "join", Likes = "likes", Chat = "chat";

    public string Type = Gift;
    public string User = "", Nick = "", UserId = "";
    public string Comment = "";  // chat: what the viewer wrote
    public string GiftName = "", GiftId = "", GroupId = "";
    public int Coins;            // coins per single gift
    public int RepeatCount = 1;  // gifts: how many so far in this combo; likes: how many likes in this message
    public bool RepeatEnd = true;
    public bool Streakable;      // combo gift: TikTok sends updates while the combo runs, then a last one
    public bool Test;

    public string Who { get { return User.Length > 0 ? "@" + User : (Nick.Length > 0 ? Nick : L.T("someone")); } }

    // One viewer, the same across messages (for per-viewer cooldowns and the top lists).
    public string ViewerKey { get { return UserId.Length > 0 ? UserId : User.ToLowerInvariant(); } }

    // The first word of a chat message ("!boom please" -> "!boom").
    public string FirstWord
    {
        get
        {
            string c = Comment.Trim();
            int sp = c.IndexOfAny(new[] { ' ', '\t', ' ', '　' });
            return sp >= 0 ? c.Substring(0, sp) : c;
        }
    }
}

// A profile: which topics are on, e.g. "Minecraft" = the Minecraft and Sounds topics, the rest off.
public class Profile
{
    public int Id;
    public string Name = "";
    public List<int> On = new List<int>();

    public static Profile Read(Dictionary<string, string> kv, int id)
    {
        Profile p = new Profile();
        p.Id = id;
        string pre = "profile." + id + ".", v;
        if (kv.TryGetValue(pre + "name", out v)) p.Name = v.Trim();
        if (kv.TryGetValue(pre + "on", out v))
            foreach (string part in v.Split(','))
            {
                int g;
                if (int.TryParse(part.Trim(), out g) && g > 0 && !p.On.Contains(g)) p.On.Add(g);
            }
        if (p.Name.Length == 0) p.Name = L.F("Profile {0}", id);
        return p;
    }

    public void Write(StringBuilder b)
    {
        string pre = "profile." + Id + ".";
        List<string> ids = new List<string>();
        foreach (int g in On) ids.Add(g.ToString());
        b.AppendLine(pre + "name=" + Name.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "on=" + string.Join(",", ids.ToArray()));
    }
}

// A topic: a named, collapsible box of actions (e.g. "Minecraft", "Sounds") that can be switched on/off as a whole.
public class RuleGroup
{
    public int Id;
    public string Name = "";
    public bool Enabled = true;
    public bool Collapsed = false;

    public static RuleGroup Read(Dictionary<string, string> kv, int id)
    {
        RuleGroup g = new RuleGroup();
        g.Id = id;
        string pre = "group." + id + ".", v;
        if (kv.TryGetValue(pre + "name", out v)) g.Name = v.Trim();
        if (kv.TryGetValue(pre + "on", out v)) g.Enabled = Settings.ParseBool(v, true);
        if (kv.TryGetValue(pre + "collapsed", out v)) g.Collapsed = Settings.ParseBool(v, false);
        if (g.Name.Length == 0) g.Name = L.F("Topic {0}", id);
        return g;
    }

    public void Write(StringBuilder b)
    {
        string pre = "group." + Id + ".";
        b.AppendLine(pre + "name=" + Name.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "on=" + Enabled);
        b.AppendLine(pre + "collapsed=" + Collapsed);
    }
}

// One rule: when <trigger> happens, do <kind> (press a key / type text / play a sound / change a counter).
public class GiftRule
{
    public const string KindKey = "key", KindText = "text", KindSound = "sound", KindCounter = "counter", KindObs = "obs";
    public static readonly string[] Kinds = { KindKey, KindText, KindSound, KindCounter, KindObs };
    public static readonly string[] KindNames = { L.N("Press a key"), L.N("Type text"), L.N("Play a sound"), L.N("Change a counter"), L.N("Control OBS") };
    public static readonly string[] Triggers = { LiveEvent.Gift, LiveEvent.Follow, LiveEvent.Share, LiveEvent.Join, LiveEvent.Likes, LiveEvent.Chat };
    public static readonly string[] TriggerNames = { L.N("Gift"), L.N("Follow"), L.N("Share"), L.N("Viewer joins"), L.N("Likes"), L.N("Chat command") };
    public const string OpAdd = "add", OpSubtract = "sub", OpSet = "set", OpCoins = "coins";
    public static readonly string[] Ops = { OpAdd, OpSubtract, OpSet, OpCoins };
    public static readonly string[] OpNames = { L.N("add"), L.N("subtract"), L.N("set to"), L.N("add the coins") };
    public const string ObsScene = "scene", ObsShow = "show", ObsHide = "hide", ObsReplay = "replay",
                        ObsRecStart = "recstart", ObsRecStop = "recstop", ObsMute = "mute", ObsUnmute = "unmute";
    public static readonly string[] ObsDos = { ObsScene, ObsShow, ObsHide, ObsReplay, ObsRecStart, ObsRecStop, ObsMute, ObsUnmute };
    public static readonly string[] ObsDoNames = { L.N("switch scene"), L.N("show source"), L.N("hide source"), L.N("save replay"),
                                                   L.N("start recording"), L.N("stop recording"), L.N("mute"), L.N("unmute") };

    // The name of a counter by id (set by the app; "change a counter" texts show it).
    public static Func<int, string> CounterName = delegate (int id) { return "#" + id; };

    public int Id;
    public int GroupId;
    public bool Enabled = true;
    public bool Collapsed = false;     // shown as one line in the list
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
    public string Command = "!boom";   // chat: the command(s) viewers type, comma separated
    public int UserCooldownSec = 30;   // chat: each viewer can use it once every this many seconds
    public int CounterId = 0;          // change a counter: which one
    public string Op = OpAdd;          // change a counter: add / subtract / set to / add the gift's coins
    public int Amount = 1;
    public string ObsDo = ObsScene;    // control OBS: what to do
    public string ObsTarget = "";      // the scene / source / audio source
    public string ObsInScene = "";     // show/hide: the scene the source is in ("" = the scene on stream)
    public int ObsSecs = 0;            // undo it after this many seconds (0 = stays)

    // these need a scene or source name
    public bool ObsNeedsTarget { get { return ObsDo == ObsScene || ObsDo == ObsShow || ObsDo == ObsHide || ObsDo == ObsMute || ObsDo == ObsUnmute; } }
    // these can be undone after a while
    public bool ObsTimed { get { return ObsNeedsTarget; } }

    public GiftRule Clone() { return (GiftRule)MemberwiseClone(); }

    public bool AnyGift { get { string g = Gifts.Trim(); return g.Length == 0 || g == "*"; } }

    static string[] Split(string s)
    {
        List<string> l = new List<string>();
        foreach (string part in s.Split(',')) { string t = part.Trim(); if (t.Length > 0) l.Add(t); }
        return l.ToArray();
    }

    // The gift names and commands, split once per change (not for every event).
    string giftsParsed, commandsParsed;
    string[] giftList, commandList;
    string[] GiftList
    {
        get
        {
            if (!ReferenceEquals(giftsParsed, Gifts)) { giftList = Split(Gifts); giftsParsed = Gifts; }
            return giftList;
        }
    }
    string[] CommandList
    {
        get
        {
            if (!ReferenceEquals(commandsParsed, Command)) { commandList = Split(Command); commandsParsed = Command; }
            return commandList;
        }
    }

    public bool Matches(LiveEvent e)
    {
        if (!Enabled || e.Type != Trigger) return false;
        if (Trigger == LiveEvent.Chat)
        {
            string word = e.FirstWord;
            if (word.Length == 0) return false;
            foreach (string c in CommandList)
                if (string.Equals(c, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        if (Trigger != LiveEvent.Gift) return true;
        if (MinCoins > 0 && e.Coins < MinCoins) return false;
        if (AnyGift) return true;
        string name = e.GiftName.Trim();
        foreach (string g in GiftList)
            if (string.Equals(g, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public bool CanRun
    {
        get
        {
            if (Trigger == LiveEvent.Chat && CommandList.Length == 0) return false;
            if (Kind == KindText) return Text.Length > 0;
            if (Kind == KindSound) return SoundFile.Length > 0;
            if (Kind == KindCounter) return CounterId > 0;
            if (Kind == KindObs) return !ObsNeedsTarget || ObsTarget.Trim().Length > 0;
            return Key.IsSet;
        }
    }

    // A sound or key action waits in a queue; changing a counter or controlling OBS happens right away.
    public bool Instant { get { return Kind == KindCounter || Kind == KindObs; } }

    // What triggers it, in the user's language ("Rose (100+ coins)", "Follow", "Every 1000 likes").
    public string TriggerText
    {
        get
        {
            switch (Trigger)
            {
                case LiveEvent.Follow: return L.T("Follow");
                case LiveEvent.Share: return L.T("Share");
                case LiveEvent.Join: return L.T("Viewer joins");
                case LiveEvent.Likes: return L.F("Every {0} likes", EveryLikes);
                case LiveEvent.Chat: return Command.Trim().Length > 0 ? L.F("Chat {0}", Command.Trim()) : L.T("Chat command");
            }
            string t = AnyGift ? L.T("Any gift") : Gifts.Trim();
            if (MinCoins > 0) t += " (" + L.F("{0}+ coins", MinCoins) + ")";
            return t;
        }
    }

    // What it does, in the user's language.
    public string ActionText
    {
        get
        {
            string times = Times > 1 ? " x" + Times : "";
            if (Kind == KindCounter)
            {
                string name = CounterId > 0 ? CounterName(CounterId) : L.T("(no counter picked)");
                if (Op == OpSet) return L.F("set {0} to {1}", name, Amount);
                if (Op == OpCoins) return L.F("{0} + the gift's coins", name) + times;
                return name + (Op == OpSubtract ? " -" : " +") + Amount + times;
            }
            if (Kind == KindObs)
            {
                string t = ObsTarget.Trim().Length > 0 ? ObsTarget.Trim() : L.T("(nothing picked)");
                string forSecs = ObsTimed && ObsSecs > 0 ? " " + L.F("for {0} s", ObsSecs) : "";
                switch (ObsDo)
                {
                    case ObsShow: return L.F("OBS: show {0}", t) + forSecs;
                    case ObsHide: return L.F("OBS: hide {0}", t) + forSecs;
                    case ObsMute: return L.F("OBS: mute {0}", t) + forSecs;
                    case ObsUnmute: return L.F("OBS: unmute {0}", t) + forSecs;
                    case ObsReplay: return L.T("OBS: save replay");
                    case ObsRecStart: return L.T("OBS: start recording");
                    case ObsRecStop: return L.T("OBS: stop recording");
                }
                return L.F("OBS: scene {0}", t) + forSecs;
            }
            if (Kind == KindSound) return L.F("play {0}", SoundFile.Length > 0 ? Path.GetFileName(SoundFile) : L.T("(no sound picked)")) + times;
            if (Kind == KindText)
                return (Key.IsSet ? L.F("{0}, then type \"{1}\"", Key, Text) : L.F("type \"{0}\"", Text)) + (Enter ? " + Enter" : "") + times;
            return L.F("press {0}", Key) + times;
        }
    }

    // One line for the collapsed view and the search: "Rose  ->  play rose.mp3"
    public string Summary { get { return TriggerText + "  →  " + ActionText; } }

    public static GiftRule Read(Dictionary<string, string> kv, int id)
    {
        GiftRule r = new GiftRule();
        r.Id = id;
        r.Collapsed = true;   // saved actions start folded up, so long lists stay tidy
        string pre = "rule." + id + ".", v;
        if (kv.TryGetValue(pre + "group", out v)) r.GroupId = Settings.ParseInt(v, 0, 0, int.MaxValue);
        if (kv.TryGetValue(pre + "collapsed", out v)) r.Collapsed = Settings.ParseBool(v, true);
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
        if (kv.TryGetValue(pre + "command", out v)) r.Command = v.Trim();
        if (kv.TryGetValue(pre + "usercooldown", out v)) r.UserCooldownSec = Settings.ParseInt(v, r.UserCooldownSec, 0, 3600);
        if (kv.TryGetValue(pre + "counter", out v)) r.CounterId = Settings.ParseInt(v, 0, 0, int.MaxValue);
        if (kv.TryGetValue(pre + "op", out v) && Array.IndexOf(Ops, v.Trim()) >= 0) r.Op = v.Trim();
        if (kv.TryGetValue(pre + "amount", out v)) r.Amount = Settings.ParseInt(v, 1, 0, 1000000);
        if (kv.TryGetValue(pre + "obsdo", out v) && Array.IndexOf(ObsDos, v.Trim()) >= 0) r.ObsDo = v.Trim();
        if (kv.TryGetValue(pre + "obstarget", out v)) r.ObsTarget = v.Trim();
        if (kv.TryGetValue(pre + "obsscene", out v)) r.ObsInScene = v.Trim();
        if (kv.TryGetValue(pre + "obssecs", out v)) r.ObsSecs = Settings.ParseInt(v, 0, 0, 3600);
        return r;
    }

    public void Write(StringBuilder b)
    {
        string pre = "rule." + Id + ".";
        b.AppendLine(pre + "group=" + GroupId);
        b.AppendLine(pre + "collapsed=" + Collapsed);
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
        b.AppendLine(pre + "command=" + Command.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "userCooldown=" + UserCooldownSec);
        b.AppendLine(pre + "counter=" + CounterId);
        b.AppendLine(pre + "op=" + Op);
        b.AppendLine(pre + "amount=" + Amount);
        b.AppendLine(pre + "obsDo=" + ObsDo);
        b.AppendLine(pre + "obsTarget=" + ObsTarget.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "obsScene=" + ObsInScene.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "obsSecs=" + ObsSecs);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

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
        uint sent = SendInput((uint)a.Length, a, Marshal.SizeOf(typeof(INPUT)));
        if (sent == a.Length) return true;
        // error 5 = access denied: the program in front runs as administrator (or is protected)
        Diag.Error("keys", "Windows took " + sent + " of " + a.Length + " key events (error " + Marshal.GetLastWin32Error() + ")");
        return false;
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
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return SlowName(pid);   // e.g. protected by anti-cheat
        string name = "";
        try
        {
            // the name straight from the process handle (Process.GetProcessById lists every process on the PC)
            StringBuilder path = new StringBuilder(1024);
            int size = path.Capacity;
            name = QueryFullProcessImageName(h, 0, path, ref size) ? Path.GetFileNameWithoutExtension(path.ToString()) : SlowName(pid);
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

    static string SlowName(uint pid)
    {
        try { using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName; } catch { return ""; }
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
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return L.F("the sound file isn't there anymore ({0})", path);
        if (path.IndexOf('"') >= 0) return L.T("the file name can't contain quotes");
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
    class Job { public GiftRule Rule; public LiveEvent Event; public int Gen; }
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
    readonly Dictionary<string, DateTime> viewerRun = new Dictionary<string, DateTime>();   // chat commands: "<rule>|<viewer>"
    volatile int generation;   // goes up on "Stop and clear": jobs from before stop (also the one running)
    int queuePosted;           // 1 = a queue update is already on its way to the UI thread
    DateTime lastSweep = DateTime.UtcNow;
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

    // Empties both queues and stops what is running (the sound that plays, the rest of a x10 key action).
    public void Clear()
    {
        lock (gate) { keys.Jobs.Clear(); sounds.Jobs.Clear(); generation++; }
        RaiseQueue();
    }

    // A new LIVE starts counting likes from 0 (for "every N likes").
    public void ResetLikes() { likeTotal = 0; }

    bool Cancelled(Job j) { return j.Gen != generation; }

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
        if ((now - lastSweep).TotalSeconds > 60)
        {
            // forget combos that never got their closing message
            lastSweep = now;
            List<string> old = new List<string>();
            foreach (KeyValuePair<string, Streak> kv in streaks) if ((now - kv.Value.Seen).TotalMinutes > 5) old.Add(kv.Key);
            foreach (string k in old) streaks.Remove(k);
        }

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
        Diag.Count(e.Test ? "event.test" : "event." + e.Type);
        switch (e.Type)
        {
            case LiveEvent.Gift: OnGift(e); break;
            case LiveEvent.Likes: OnLikes(e); break;
            case LiveEvent.Chat: OnChat(e); break;
            default: OnSimple(e); break;
        }
    }

    void OnGift(LiveEvent g)
    {
        bool first;
        int added = NewGifts(g, out first);
        if (added == 0 && !first) return;   // the closing message of a combo we already counted
        app.W.Count(g, added);              // goals, timer and top lists count even while actions are paused

        string what = L.F("{0} sent {1}", g.Who, g.GiftName.Length > 0 ? g.GiftName : L.T("a gift")) + (added > 1 ? " x" + added : "") +
                      (g.Coins > 0 ? " (" + L.F("{0} coins", g.Coins * Math.Max(1, added)) + ")" : "") + (g.Test ? " [" + L.T("test") + "]" : "");
        if (!app.S.ActionsOn) { app.Log(what + " - " + L.T("actions are paused")); return; }
        string result = RunRules(g, delegate (GiftRule r)
        {
            if (r.Kind == GiftRule.KindCounter && r.Op == GiftRule.OpCoins) return added;   // the coins of every gift in the combo
            return r.PerGift && r.Kind != GiftRule.KindSound && r.Kind != GiftRule.KindObs ? added : (first ? 1 : 0);   // sounds and OBS: once per send
        });
        app.Log(what + " - " + (result ?? L.T("no action for it")));
    }

    void OnSimple(LiveEvent e)
    {
        app.W.Count(e, 1);
        string what = (e.Type == LiveEvent.Follow ? L.F("{0} followed you", e.Who) : e.Type == LiveEvent.Share ? L.F("{0} shared your LIVE", e.Who) : L.F("{0} joined", e.Who)) +
                      (e.Test ? " [" + L.T("test") + "]" : "");
        if (!app.S.ActionsOn) { if (e.Type != LiveEvent.Join) app.Log(what + " - " + L.T("actions are paused")); return; }
        string result = RunRules(e, delegate (GiftRule r) { return 1; });
        if (result != null) app.Log(what + " - " + result);
        else if (e.Type != LiveEvent.Join) app.Log(what);   // joins are too many to log one by one
    }

    void OnLikes(LiveEvent e)
    {
        long before = likeTotal;
        likeTotal += Math.Max(1, e.RepeatCount);
        long now = likeTotal;
        app.W.Count(e, Math.Max(1, e.RepeatCount));
        if (!app.S.ActionsOn) return;
        string result = RunRules(e, delegate (GiftRule r)
        {
            long every = Math.Max(1, r.EveryLikes);
            long n = now / every - before / every;
            return (int)Math.Min(r.Instant ? 1000000 : 100, n);   // a counter can take any number at once
        });
        if (result != null) app.Log(L.F("{0} likes", now) + (e.Test ? " [" + L.T("test") + "]" : "") + " - " + result);
    }

    // Chat: only messages that are a command (!boom) do anything, and only those are logged.
    void OnChat(LiveEvent e)
    {
        if (!app.S.ActionsOn || e.FirstWord.Length == 0) return;
        string result = RunRules(e, delegate (GiftRule r) { return 1; });
        if (result != null) app.Log(L.F("{0} wrote {1}", e.Who, e.FirstWord) + (e.Test ? " [" + L.T("test") + "]" : "") + " - " + result);
    }

    // Queues every matching rule runsFor(rule) times (cooldowns applied). Returns a log text, or null if nothing ran.
    string RunRules(LiveEvent e, Func<GiftRule, int> runsFor)
    {
        int queued = 0, dropped = 0, cooling = 0, changed = 0, obs = 0;
        DateTime now = DateTime.UtcNow;
        foreach (GiftRule r in app.S.Rules)
        {
            if (!r.Matches(e) || !r.CanRun || !app.S.GroupOn(r.GroupId)) continue;
            int runs = runsFor(r);
            if (runs <= 0) continue;
            DateTime last;
            if (r.CooldownSec > 0)
            {
                if (lastRun.TryGetValue(r.Id, out last) && (now - last).TotalSeconds < r.CooldownSec) { cooling++; continue; }
                if (!(r.Instant && r.Op == GiftRule.OpCoins)) runs = 1;
            }
            string viewer = null;
            if (e.Type == LiveEvent.Chat && r.UserCooldownSec > 0 && e.ViewerKey.Length > 0)
            {
                viewer = r.Id + "|" + e.ViewerKey;
                if (viewerRun.TryGetValue(viewer, out last) && (now - last).TotalSeconds < r.UserCooldownSec) { cooling++; continue; }
            }
            int ran = 0;
            if (r.Kind == GiftRule.KindObs) { app.Obs.Run(r, Source(new Job { Rule = r, Event = e })); ran = 1; obs++; }
            else if (r.Instant) { if (RunCounter(r, e, runs)) { ran = 1; changed++; } }
            else
                for (int i = 0; i < runs; i++)
                {
                    if (Enqueue(r, e)) ran++; else dropped++;
                }
            if (ran > 0)
            {
                lastRun[r.Id] = now;   // a skipped action (queue full) doesn't start its cooldown
                if (viewer != null) RememberViewer(viewer, now);
            }
            if (!r.Instant) queued += ran;
        }
        if (queued > 0) Diag.Count("actions.queued", queued);
        if (dropped > 0) Diag.Count("actions.dropped", dropped);
        if (cooling > 0) Diag.Count("actions.cooling", cooling);
        if (queued == 0 && dropped == 0 && changed == 0 && obs == 0)
            return cooling > 0 && e.Type != LiveEvent.Join ? L.T("waiting for cooldown") : null;
        List<string> parts = new List<string>();
        if (changed > 0) parts.Add(changed == 1 ? L.T("1 counter changed") : L.F("{0} counters changed", changed));
        if (obs > 0) parts.Add(obs == 1 ? L.T("1 OBS action") : L.F("{0} OBS actions", obs));
        if (queued > 0) parts.Add(queued == 1 ? L.T("1 action queued") : L.F("{0} actions queued", queued));
        if (dropped > 0) parts.Add(L.F("{0} skipped (queue full)", dropped));
        return string.Join(", ", parts.ToArray());
    }

    void RememberViewer(string key, DateTime now)
    {
        if (viewerRun.Count > 5000)
        {
            // forget viewers whose cooldown is long over
            List<string> old = new List<string>();
            foreach (KeyValuePair<string, DateTime> kv in viewerRun) if ((now - kv.Value).TotalHours > 1) old.Add(kv.Key);
            foreach (string k in old) viewerRun.Remove(k);
        }
        viewerRun[key] = now;
    }

    // "Change a counter": right away on the UI thread (no queue, no game needed). runs = how many
    // times it was triggered (e.g. 10 for a x10 combo). Returns false if the counter is gone.
    bool RunCounter(GiftRule r, LiveEvent e, int runs)
    {
        Counter c = app.S.Find(r.CounterId);
        if (c == null)
        {
            app.Log(L.F("Skipped \"{0}\": its counter was removed. Pick another one.", r.Summary));
            return false;
        }
        long times = (long)Math.Max(1, r.Times) * Math.Max(1, runs);
        long value = c.Count;
        switch (r.Op)
        {
            case GiftRule.OpSet: value = r.Amount; break;
            case GiftRule.OpSubtract: value -= r.Amount * times; break;
            case GiftRule.OpCoins: value += (long)Math.Max(e.Test ? 1 : 0, e.Coins) * times; break;
            default: value += r.Amount * times; break;
        }
        app.SetCount(c, (int)Math.Max(0, Math.Min(int.MaxValue, value)));
        Diag.Count("counters.changed");
        Diag.Write("counters", r.Summary + " x" + runs + " -> " + c.DisplayName + " = " + c.Count);
        return true;
    }

    // Test button on a rule: runs it once, whether or not actions are paused or cooling down.
    public void RunTest(GiftRule r)
    {
        LiveEvent e = new LiveEvent();
        e.Test = true;
        e.Type = r.Trigger;
        e.GiftName = r.Trigger == LiveEvent.Gift ? (r.AnyGift ? L.T("test gift") : r.Gifts.Split(',')[0].Trim()) : "";
        if (r.Kind == GiftRule.KindObs) { app.Obs.Run(r, Source(new Job { Rule = r, Event = e })); return; }
        if (r.Instant)
        {
            if (RunCounter(r, e, 1)) app.Log(L.F("Did: {0}", r.ActionText) + " (" + Source(new Job { Rule = r, Event = e }) + ")");
            return;
        }
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
            j.Gen = generation;
            lane.Jobs.Enqueue(j);
        }
        lane.Wake.Set();
        RaiseQueue();
        return true;
    }

    // Tells the window the queue changed - at most one update waits at a time, so a x100 combo
    // doesn't send the window 200 updates.
    void RaiseQueue()
    {
        if (Interlocked.Exchange(ref queuePosted, 1) == 1) return;
        app.Ui(delegate
        {
            Interlocked.Exchange(ref queuePosted, 0);
            if (QueueChanged != null) QueueChanged(this, EventArgs.Empty);
        });
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
                RaiseQueue();
                try
                {
                    if (lane == sounds) RunSound(j); else RunKeys(j);
                }
                catch (Exception ex)
                {
                    app.Log(L.F("Action failed: {0}", ex.Message));
                    Diag.Error("actions", ex.ToString());
                }
            }
        }
    }

    string Source(Job j) { return j.Event.Test ? j.Rule.TriggerText + ", " + L.T("test") : L.F("{0} from {1}", j.Rule.TriggerText, j.Event.Who); }

    void RunSound(Job j)
    {
        GiftRule r = j.Rule;
        if (!j.Event.Test && !app.S.ActionsOn) return;
        for (int i = 0; i < r.Times && !Cancelled(j); i++)
        {
            string error = SoundPlayer.Play(r.SoundFile, r.Volume, delegate { return Cancelled(j); });
            if (error != null)
            {
                app.Log(L.F("Couldn't play {0}: {1}", Path.GetFileName(r.SoundFile), error));
                Diag.Count("sounds.failed");
                Diag.Error("sounds", r.SoundFile + ": " + error);
                return;
            }
            Diag.Count("sounds.played");
        }
        app.Log((Cancelled(j) ? L.F("Stopped: {0}", Path.GetFileName(r.SoundFile)) : L.F("Played: {0}", Path.GetFileName(r.SoundFile))) + " (" + Source(j) + ")");
    }

    void RunKeys(Job j)
    {
        GiftRule r = j.Rule;
        if (!j.Event.Test && !app.S.ActionsOn) return;
        int elevated;
        string front = KeySender.Foreground(out elevated);
        string only = KeySender.CleanProcessName(app.S.OnlyProcess);
        Diag.Note("keys.front", front + (elevated == 1 ? " (runs as administrator)" : elevated == -1 ? " (couldn't check admin)" : ""));
        if (only.Length > 0 && !string.Equals(front, only, StringComparison.OrdinalIgnoreCase))
        {
            Diag.Count("keys.notfront");
            app.Log(L.F("Skipped \"{0}\": {1} isn't in front", r.ActionText, only) + (front.Length > 0 ? " (" + L.F("{0} is", front) + ")" : ""));
            Thread.Sleep(20);
            return;
        }
        if (elevated == 1 && !KeySender.SelfElevated && front != warnedProcess)
        {
            warnedProcess = front;
            app.Log(L.F("Warning: {0} runs as administrator, so Windows may block ClutchKit's keys. Fix: Actions > Restart as administrator.", front));
            app.Ui(delegate { if (ElevationProblem != null) ElevationProblem(front); });
        }

        bool ok = true;
        int gap = Math.Max(0, app.S.GapMs);
        if (r.Kind == GiftRule.KindText)
        {
            for (int i = 0; i < r.Times && !Cancelled(j); i++)
            {
                if (r.Key.IsSet) { ok &= KeySender.Press(r.Key, 40); Thread.Sleep(250); }   // e.g. T opens the chat
                ok &= KeySender.Type(r.Text, r.Enter);
                Thread.Sleep(gap);
            }
        }
        else
        {
            for (int i = 0; i < r.Times && !Cancelled(j); i++)
            {
                ok &= KeySender.Press(r.Key, r.HoldMs);
                Thread.Sleep(gap);
            }
        }
        Diag.Count(ok ? "keys.ok" : "keys.blocked");
        Diag.Write("keys", r.Kind + " " + r.Key.Serialize() + " x" + r.Times + " hold " + r.HoldMs + " ms -> " + front +
                           " (admin: " + elevated + ", ClutchKit admin: " + KeySender.SelfElevated + ") " + (ok ? "OK" : "BLOCKED"));
        app.Log((ok ? L.F("Did: {0}", r.ActionText) : L.F("Windows blocked: {0}", r.ActionText)) + " (" + Source(j) + ")" + (front.Length > 0 ? " -> " + front : ""));
    }
}
