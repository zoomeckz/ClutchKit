// ClutchKit - stream widgets: goal bars, a subathon timer and top gifter / liker lists.
// ----------------------------------------------------------------------------------
// Each widget is its own Link source in LIVE Studio (Overlay.ViewLink), so it can be placed anywhere;
// the same overlay script draws them from /api/state. What they show is saved in settings.ini;
// the live numbers in live.txt (goals, timer, this LIVE's top lists) and leaderboard.txt (all time).
// Everything here runs on the UI thread (events arrive through GiftEngine.OnEvent).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// A goal bar: "Like goal 450 / 1000".
public class Goal
{
    public static readonly string[] Sources = { "likes", "follows", "shares", "coins", "gifts", "joins" };
    public static readonly string[] SourceNames = { L.N("Likes"), L.N("Follows"), L.N("Shares"), L.N("Coins"), L.N("Gifts"), L.N("Viewer joins") };
    public static readonly string[] DefaultTitles = { L.N("Like goal"), L.N("Follower goal"), L.N("Share goal"), L.N("Coin goal"), L.N("Gift goal"), L.N("Viewer goal") };
    public const string FromCounter = "counter";

    public int Id;
    public string Title = "";
    public string Source = "likes";    // one of Sources, or "counter" (then CounterId)
    public int CounterId;
    public long Target = 1000;
    public long Step = 0;              // when reached, raise the goal by this much (0 = stay)
    public bool Restart = true;        // start over when a new LIVE starts
    public Color BarColor = Color.FromArgb(229, 72, 77);
    public bool Visible = true;

    // live (live.txt)
    public long Value;
    public bool Reached;

    public static string DefaultTitle(string source)
    {
        int i = Array.IndexOf(Sources, source);
        return i >= 0 ? L.T(DefaultTitles[i]) : L.T("Goal");
    }

    public static Goal Read(Dictionary<string, string> kv, int id)
    {
        Goal g = new Goal();
        g.Id = id;
        string pre = "goal." + id + ".", v;
        if (kv.TryGetValue(pre + "title", out v)) g.Title = v.Trim();
        if (kv.TryGetValue(pre + "source", out v))
        {
            v = v.Trim();
            if (Array.IndexOf(Sources, v) >= 0 || v == FromCounter) g.Source = v;
        }
        if (kv.TryGetValue(pre + "counter", out v)) g.CounterId = Settings.ParseInt(v, 0, 0, int.MaxValue);
        if (kv.TryGetValue(pre + "target", out v)) g.Target = Settings.ParseInt(v, 1000, 1, int.MaxValue);
        if (kv.TryGetValue(pre + "step", out v)) g.Step = Settings.ParseInt(v, 0, 0, int.MaxValue);
        if (kv.TryGetValue(pre + "restart", out v)) g.Restart = Settings.ParseBool(v, true);
        if (kv.TryGetValue(pre + "color", out v)) g.BarColor = Settings.ParseColor(v, g.BarColor);
        if (kv.TryGetValue(pre + "show", out v)) g.Visible = Settings.ParseBool(v, true);
        return g;
    }

    public void Write(StringBuilder b)
    {
        string pre = "goal." + Id + ".";
        b.AppendLine(pre + "title=" + Title.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "source=" + Source);
        b.AppendLine(pre + "counter=" + CounterId);
        b.AppendLine(pre + "target=" + Target);
        b.AppendLine(pre + "step=" + Step);
        b.AppendLine(pre + "restart=" + Restart);
        b.AppendLine(pre + "color=" + Settings.ToHex(BarColor));
        b.AppendLine(pre + "show=" + Visible);
    }
}

// The subathon timer's settings: gifts, follows, shares and likes add time.
public class TimerSetup
{
    public string Title;               // null = not set yet (filled in the user's language)
    public bool Visible = true;
    public int StartMin = 60;
    public int MaxHours = 0;           // never more than this on the clock (0 = no limit)
    public int PerCoin = 5, PerFollow = 30, PerShare = 0, Per100Likes = 0;   // seconds added

    public void Read(Dictionary<string, string> kv)
    {
        string v;
        if (kv.TryGetValue("timer.title", out v)) Title = v.Trim();
        if (kv.TryGetValue("timer.show", out v)) Visible = Settings.ParseBool(v, true);
        if (kv.TryGetValue("timer.startmin", out v)) StartMin = Settings.ParseInt(v, StartMin, 1, 100000);
        if (kv.TryGetValue("timer.maxhours", out v)) MaxHours = Settings.ParseInt(v, 0, 0, 10000);
        if (kv.TryGetValue("timer.percoin", out v)) PerCoin = Settings.ParseInt(v, PerCoin, 0, 86400);
        if (kv.TryGetValue("timer.perfollow", out v)) PerFollow = Settings.ParseInt(v, PerFollow, 0, 86400);
        if (kv.TryGetValue("timer.pershare", out v)) PerShare = Settings.ParseInt(v, PerShare, 0, 86400);
        if (kv.TryGetValue("timer.per100likes", out v)) Per100Likes = Settings.ParseInt(v, Per100Likes, 0, 86400);
    }

    public void Write(StringBuilder b)
    {
        b.AppendLine("timer.title=" + (Title ?? "").Replace("\r", "").Replace("\n", " "));
        b.AppendLine("timer.show=" + Visible);
        b.AppendLine("timer.startMin=" + StartMin);
        b.AppendLine("timer.maxHours=" + MaxHours);
        b.AppendLine("timer.perCoin=" + PerCoin);
        b.AppendLine("timer.perFollow=" + PerFollow);
        b.AppendLine("timer.perShare=" + PerShare);
        b.AppendLine("timer.per100Likes=" + Per100Likes);
    }
}

// A top list: the biggest gifters (coins) or likers, this LIVE or all time.
public class TopSetup
{
    public readonly string Key;        // "gifters" or "likers"
    public string Title;               // null = not set yet
    public bool Visible = true;
    public int Count = 5;
    public bool AllTime = false;

    public TopSetup(string key) { Key = key; }
    public bool ByCoins { get { return Key == "gifters"; } }
    public string DefaultTitle { get { return ByCoins ? L.T("Top gifters") : L.T("Top likers"); } }

    public void Read(Dictionary<string, string> kv)
    {
        string pre = "top." + Key + ".", v;
        if (kv.TryGetValue(pre + "title", out v)) Title = v.Trim();
        if (kv.TryGetValue(pre + "show", out v)) Visible = Settings.ParseBool(v, true);
        if (kv.TryGetValue(pre + "count", out v)) Count = Settings.ParseInt(v, 5, 1, 10);
        if (kv.TryGetValue(pre + "alltime", out v)) AllTime = Settings.ParseBool(v, false);
    }

    public void Write(StringBuilder b)
    {
        string pre = "top." + Key + ".";
        b.AppendLine(pre + "title=" + (Title ?? "").Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "show=" + Visible);
        b.AppendLine(pre + "count=" + Count);
        b.AppendLine(pre + "allTime=" + AllTime);
    }
}

// A viewer count on stream: "Twitch: 23", "TikTok: 44" or both added together.
public class ViewerCounter
{
    public const string FromTikTok = "tiktok", FromTwitch = "twitch", FromBoth = "total";
    public static readonly string[] Sources = { FromTikTok, FromTwitch, FromBoth };
    public static readonly string[] SourceNames = { L.N("TikTok"), L.N("Twitch"), L.N("TikTok + Twitch") };

    public int Id;
    public string Before = "TikTok:", After = "";   // text around the number
    public string Source = FromTikTok;
    public bool Visible = true;

    public static string DefaultBefore(string source)
    {
        return source == FromTwitch ? "Twitch:" : source == FromBoth ? L.T("Viewers:") : "TikTok:";
    }

    public static ViewerCounter Read(Dictionary<string, string> kv, int id)
    {
        ViewerCounter c = new ViewerCounter();
        c.Id = id;
        string pre = "viewers." + id + ".", v;
        if (kv.TryGetValue(pre + "source", out v) && Array.IndexOf(Sources, v.Trim()) >= 0) c.Source = v.Trim();
        c.Before = DefaultBefore(c.Source);
        if (kv.TryGetValue(pre + "before", out v)) c.Before = v.Trim();
        if (kv.TryGetValue(pre + "after", out v)) c.After = v.Trim();
        if (kv.TryGetValue(pre + "show", out v)) c.Visible = Settings.ParseBool(v, true);
        return c;
    }

    public void Write(StringBuilder b)
    {
        string pre = "viewers." + Id + ".";
        b.AppendLine(pre + "source=" + Source);
        b.AppendLine(pre + "before=" + Before.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "after=" + After.Replace("\r", "").Replace("\n", " "));
        b.AppendLine(pre + "show=" + Visible);
    }
}

// What the joint alerts overlay shows (TikTok and Twitch together).
public static class AlertKind
{
    public const string Gifts = "gift", Follows = "follow", Shares = "share", Subs = "sub", Bits = "bits", Raids = "raid";
}

public class AlertSetup
{
    public bool Visible = true;
    public int Secs = 5;               // how long each alert stays
    public bool Tag = true;            // "TikTok" / "Twitch" above the text
    public bool Gifts = true, Follows = true, Shares = false, Subs = true, Bits = true, Raids = true;
    public int MinCoins = 0;           // gifts: only from this many coins (the whole combo)

    public bool Wants(string kind, long amount)
    {
        switch (kind)
        {
            case AlertKind.Gifts: return Gifts && amount >= MinCoins;
            case AlertKind.Follows: return Follows;
            case AlertKind.Shares: return Shares;
            case AlertKind.Subs: return Subs;
            case AlertKind.Bits: return Bits;
            case AlertKind.Raids: return Raids;
        }
        return false;
    }

    public void Read(Dictionary<string, string> kv)
    {
        string v;
        if (kv.TryGetValue("alerts.show", out v)) Visible = Settings.ParseBool(v, true);
        if (kv.TryGetValue("alerts.secs", out v)) Secs = Settings.ParseInt(v, Secs, 1, 60);
        if (kv.TryGetValue("alerts.tag", out v)) Tag = Settings.ParseBool(v, true);
        if (kv.TryGetValue("alerts.gifts", out v)) Gifts = Settings.ParseBool(v, true);
        if (kv.TryGetValue("alerts.mincoins", out v)) MinCoins = Settings.ParseInt(v, 0, 0, 1000000);
        if (kv.TryGetValue("alerts.follows", out v)) Follows = Settings.ParseBool(v, true);
        if (kv.TryGetValue("alerts.shares", out v)) Shares = Settings.ParseBool(v, false);
        if (kv.TryGetValue("alerts.subs", out v)) Subs = Settings.ParseBool(v, true);
        if (kv.TryGetValue("alerts.bits", out v)) Bits = Settings.ParseBool(v, true);
        if (kv.TryGetValue("alerts.raids", out v)) Raids = Settings.ParseBool(v, true);
    }

    public void Write(StringBuilder b)
    {
        b.AppendLine("alerts.show=" + Visible);
        b.AppendLine("alerts.secs=" + Secs);
        b.AppendLine("alerts.tag=" + Tag);
        b.AppendLine("alerts.gifts=" + Gifts);
        b.AppendLine("alerts.minCoins=" + MinCoins);
        b.AppendLine("alerts.follows=" + Follows);
        b.AppendLine("alerts.shares=" + Shares);
        b.AppendLine("alerts.subs=" + Subs);
        b.AppendLine("alerts.bits=" + Bits);
        b.AppendLine("alerts.raids=" + Raids);
    }
}

// The live side of the widgets: counting, the running timer, the top lists, saving them.
public class Widgets
{
    public class Fan { public string Key = "", Name = ""; public long Coins, Likes; }
    public enum Clock { Idle, Running, Paused, Done }

    const int MaxFans = 50000;   // all-time list size; the smallest are forgotten beyond this

    readonly App app;
    readonly string liveFile, boardFile;
    readonly Dictionary<string, Fan> session = new Dictionary<string, Fan>(), allTime = new Dictionary<string, Fan>();
    readonly Timer second = new Timer(), refresh = new Timer(), save = new Timer();
    long leftMs;                 // paused / idle: time on the clock
    DateTime endsUtc;            // running: when it reaches 0
    int likeCarry;               // likes not yet worth a "per 100 likes" step
    DateTime lastEvent = DateTime.MinValue;
    bool liveEnded;              // the LIVE ended: this LIVE's lists start over when the next one starts
    bool liveDirty, boardDirty;
    int ticks;

    public Clock TimerState { get; private set; }
    public event EventHandler Changed;   // UI thread, at most a few times a second

    public Widgets(App app)
    {
        this.app = app;
        liveFile = Path.Combine(app.Folder, "live.txt");
        boardFile = Path.Combine(app.Folder, "leaderboard.txt");
        leftMs = StartMs;
        TikTokViewers = TwitchViewers = -1;
        Load();
        second.Interval = 1000;
        second.Tick += delegate { OnSecond(); };
        second.Start();
        refresh.Interval = 150;   // many events in a row = one overlay update
        refresh.Tick += delegate
        {
            refresh.Stop();
            app.RefreshState();
            if (Changed != null) Changed(this, EventArgs.Empty);
        };
        save.Interval = 3000;
        save.Tick += delegate { save.Stop(); SaveAll(); };
        app.CountChanged += delegate { CheckCounterGoals(); };
    }

    long StartMs { get { return (long)Math.Max(1, app.S.Timer.StartMin) * 60000; } }

    void MarkChanged(bool live, bool board)
    {
        liveDirty |= live;
        boardDirty |= board;
        if ((liveDirty || boardDirty) && !save.Enabled) save.Start();
        if (!refresh.Enabled) refresh.Start();
    }

    // ---------------- counting ----------------

    // Called for every gift send (amount = gifts in it), like message (amount = likes), follow, share and join.
    public void Count(LiveEvent e, int amount)
    {
        amount = Math.Max(1, amount);
        if (!e.Test) lastEvent = DateTime.UtcNow;
        long coins = e.Type == LiveEvent.Gift ? (long)Math.Max(0, e.Coins) * amount : 0;

        foreach (Goal g in app.S.Goals)
        {
            long add = 0;
            switch (g.Source)
            {
                case "likes": if (e.Type == LiveEvent.Likes) add = amount; break;
                case "follows": if (e.Type == LiveEvent.Follow) add = 1; break;
                case "shares": if (e.Type == LiveEvent.Share) add = 1; break;
                case "joins": if (e.Type == LiveEvent.Join) add = 1; break;
                case "gifts": if (e.Type == LiveEvent.Gift) add = amount; break;
                case "coins": add = coins; break;
            }
            if (add > 0) { g.Value += add; CheckGoal(g); }
        }

        // subathon: time is added while the timer is running or paused (not before it started or after it ended)
        if (TimerState == Clock.Running || TimerState == Clock.Paused)
        {
            TimerSetup t = app.S.Timer;
            long sec = 0;
            if (e.Type == LiveEvent.Gift) sec = coins * t.PerCoin;
            else if (e.Type == LiveEvent.Follow) sec = t.PerFollow;
            else if (e.Type == LiveEvent.Share) sec = t.PerShare;
            else if (e.Type == LiveEvent.Likes && t.Per100Likes > 0)
            {
                likeCarry += amount;
                sec = (long)(likeCarry / 100) * t.Per100Likes;
                likeCarry %= 100;
            }
            if (sec > 0) AddTime(sec * 1000, false);
        }

        // top lists
        bool board = false;
        if ((e.Type == LiveEvent.Gift && coins > 0) || e.Type == LiveEvent.Likes)
        {
            long likes = e.Type == LiveEvent.Likes ? amount : 0;
            AddFan(session, e, coins, likes);
            if (!e.Test) { AddFan(allTime, e, coins, likes); board = true; }
        }
        MarkChanged(true, board);
    }

    void AddFan(Dictionary<string, Fan> list, LiveEvent e, long coins, long likes)
    {
        string key = e.ViewerKey;
        if (key.Length == 0) return;
        Fan f;
        if (!list.TryGetValue(key, out f))
        {
            if (list == allTime && allTime.Count >= MaxFans) Prune();
            f = new Fan();
            f.Key = key;
            list[key] = f;
        }
        string name = e.Nick.Trim().Length > 0 ? e.Nick.Trim() : (e.User.Length > 0 ? "@" + e.User : "");
        if (name.Length > 0) f.Name = name;   // people change their names; show the newest
        f.Coins += coins;
        f.Likes += likes;
    }

    // Forgets the smallest tenth of the all-time list.
    void Prune()
    {
        List<Fan> all = new List<Fan>(allTime.Values);
        all.Sort(delegate (Fan a, Fan b) { return (a.Coins * 100 + a.Likes).CompareTo(b.Coins * 100 + b.Likes); });
        for (int i = 0; i < all.Count / 10; i++) allTime.Remove(all[i].Key);
    }

    void CheckGoal(Goal g)
    {
        long value = GoalValue(g);
        if (g.Target <= 0 || value < g.Target) { g.Reached = false; return; }
        if (!g.Reached || g.Step > 0)
        {
            app.Log(L.F("Goal reached: {0} ({1})!", g.Title.Length > 0 ? g.Title : Goal.DefaultTitle(g.Source), g.Target));
            Diag.Count("goals.reached");
        }
        if (g.Step > 0)
        {
            while (g.Target <= value) g.Target += g.Step;   // the bar keeps going with the next step
            g.Reached = false;
            app.SaveSettingsSoon();
        }
        else g.Reached = true;
    }

    void CheckCounterGoals()
    {
        foreach (Goal g in app.S.Goals) if (g.Source == Goal.FromCounter) CheckGoal(g);
        MarkChanged(false, false);
    }

    public long GoalValue(Goal g)
    {
        if (g.Source != Goal.FromCounter) return g.Value;
        Counter c = app.S.Find(g.CounterId);
        return c != null ? c.Count : 0;
    }

    public void SetGoalValue(Goal g, long value)
    {
        if (g.Source == Goal.FromCounter) return;
        g.Value = Math.Max(0, value);
        g.Reached = false;
        CheckGoal(g);
        MarkChanged(true, false);
    }

    // ---------------- new LIVE ----------------

    // TikTok says the LIVE ended. The lists stay on screen until the next LIVE starts.
    public void LiveEnded()
    {
        liveEnded = true;
        MarkChanged(true, false);
    }

    // The connection got its first message (= you're live). Starts over if the last LIVE ended,
    // or if nothing happened for 2 hours (ClutchKit wasn't running when it ended).
    public void OnLiveStarted()
    {
        bool stale = lastEvent != DateTime.MinValue && (DateTime.UtcNow - lastEvent).TotalHours > 2;
        if (liveEnded || stale) NewLive();
    }

    // This LIVE's top lists and goals (those set to start over) start from 0; so do like milestones.
    public void NewLive()
    {
        session.Clear();
        foreach (Goal g in app.S.Goals)
            if (g.Restart && g.Source != Goal.FromCounter) { g.Value = 0; g.Reached = false; }
        likeCarry = 0;
        liveEnded = false;
        app.Engine.ResetLikes();
        app.Log(L.T("New LIVE: this LIVE's top lists and goals start over."));
        MarkChanged(true, false);
    }

    // ---------------- subathon timer ----------------

    public long TimeLeftMs
    {
        get { return TimerState == Clock.Running ? Math.Max(0, (long)(endsUtc - DateTime.UtcNow).TotalMilliseconds) : leftMs; }
    }

    long Cap(long ms)
    {
        int max = app.S.Timer.MaxHours;
        return max > 0 ? Math.Min(ms, (long)max * 3600000) : ms;
    }

    public void StartTimer()
    {
        if (TimerState == Clock.Running) return;
        if (TimerState == Clock.Idle || TimerState == Clock.Done || leftMs <= 0) leftMs = Cap(StartMs);
        endsUtc = DateTime.UtcNow.AddMilliseconds(leftMs);
        TimerState = Clock.Running;
        app.Log(L.F("Subathon timer started ({0} on the clock).", ClockText(leftMs)));
        MarkChanged(true, false);
    }

    public void PauseTimer()
    {
        if (TimerState != Clock.Running) return;
        leftMs = TimeLeftMs;
        TimerState = Clock.Paused;
        app.Log(L.T("Subathon timer paused."));
        MarkChanged(true, false);
    }

    public void ResetTimer()
    {
        TimerState = Clock.Idle;
        leftMs = Cap(StartMs);
        likeCarry = 0;
        MarkChanged(true, false);
    }

    // Adds (or with a negative number takes away) time. Manual changes also work before the start
    // and after the end; gifts only while it's running or paused.
    public void AddTime(long ms, bool manual)
    {
        if (TimerState == Clock.Running)
        {
            long left = Cap(Math.Max(0, TimeLeftMs + ms));
            endsUtc = DateTime.UtcNow.AddMilliseconds(left);
        }
        else
        {
            if (TimerState == Clock.Done && manual && ms > 0) TimerState = Clock.Paused;
            leftMs = Cap(Math.Max(0, leftMs + ms));
        }
        MarkChanged(true, false);
    }

    // The start time changed on the Widgets page: an idle timer shows the new time.
    public void StartTimeChanged()
    {
        if (TimerState == Clock.Idle) leftMs = Cap(StartMs);
        MarkChanged(true, false);
    }

    void OnSecond()
    {
        ticks++;
        if (TimerState != Clock.Running) return;
        if (TimeLeftMs <= 0)
        {
            TimerState = Clock.Done;
            leftMs = 0;
            app.Log(L.T("Subathon timer: time's up!"));
            MarkChanged(true, false);
            return;
        }
        if (ticks % 10 == 0) liveDirty = true;   // save now and then, so a crash doesn't lose much
        if (liveDirty && !save.Enabled) save.Start();
        if (Changed != null) Changed(this, EventArgs.Empty);   // the clock on the Widgets page
    }

    public static string ClockText(long ms)
    {
        long s = Math.Max(0, (ms + 999) / 1000), h = s / 3600, m = s % 3600 / 60, x = s % 60;
        return h > 0 ? h + ":" + m.ToString("00") + ":" + x.ToString("00") : m + ":" + x.ToString("00");
    }

    // ---------------- top lists ----------------

    // The biggest N, biggest first (only people with more than 0).
    public List<Fan> Top(TopSetup t)
    {
        Dictionary<string, Fan> src = t.AllTime ? allTime : session;
        int n = Math.Max(1, Math.Min(10, t.Count));
        List<Fan> best = new List<Fan>(n + 1);
        foreach (Fan f in src.Values)
        {
            long v = t.ByCoins ? f.Coins : f.Likes;
            if (v <= 0) continue;
            int at = best.Count;
            while (at > 0 && (t.ByCoins ? best[at - 1].Coins : best[at - 1].Likes) < v) at--;
            if (at >= n) continue;
            best.Insert(at, f);
            if (best.Count > n) best.RemoveAt(n);
        }
        return best;
    }

    public int SessionFans { get { return session.Count; } }
    public int AllTimeFans { get { return allTime.Count; } }

    public void ClearTop(bool allTimeList)
    {
        if (allTimeList) { allTime.Clear(); MarkChanged(false, true); }
        else { session.Clear(); MarkChanged(true, false); }
    }

    // ---------------- overlay state ----------------

    static long Epoch(DateTime utc) { return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

    static string Short(string name)
    {
        name = (name ?? "").Trim();
        return name.Length > 24 ? name.Substring(0, 23) + "…" : name;
    }

    // Adds the widgets to the overlay's /api/state JSON.
    public void AppendState(StringBuilder b)
    {
        Settings s = app.S;
        Style look = s.Counters.Count > 0 ? s.Counters[0].Style : new Style();
        b.Append(",\"look\":{\"font\":").Append(App.Js(Settings.CleanFont(look.Font)))
         .Append(",\"bold\":").Append(look.Bold ? "true" : "false")
         .Append(",\"italic\":").Append(look.Italic ? "true" : "false")
         .Append(",\"shadow\":").Append(look.Shadow ? "true" : "false")
         .Append(",\"color\":").Append(App.Js(Settings.ToHex(look.TextColor)))
         .Append(",\"outline\":").Append(App.Js(Settings.ToHex(look.OutlineColor)))
         .Append(",\"outlineWidth\":").Append(look.OutlineWidth).Append('}');

        b.Append(",\"goals\":[");
        for (int i = 0; i < s.Goals.Count; i++)
        {
            Goal g = s.Goals[i];
            if (i > 0) b.Append(',');
            b.Append("{\"id\":").Append(g.Id)
             .Append(",\"show\":").Append(g.Visible ? "true" : "false")
             .Append(",\"title\":").Append(App.Js(g.Title))
             .Append(",\"value\":").Append(GoalValue(g))
             .Append(",\"target\":").Append(g.Target)
             .Append(",\"color\":").Append(App.Js(Settings.ToHex(g.BarColor))).Append('}');
        }
        b.Append(']');

        TimerSetup t = s.Timer;
        string state = TimerState == Clock.Running ? "run" : TimerState == Clock.Paused ? "pause" : TimerState == Clock.Done ? "done" : "idle";
        b.Append(",\"timer\":{\"show\":").Append(t.Visible ? "true" : "false")
         .Append(",\"title\":").Append(App.Js(t.Title ?? ""))
         .Append(",\"state\":\"").Append(state).Append('"')
         .Append(",\"ends\":").Append(TimerState == Clock.Running ? Epoch(endsUtc) : 0)
         .Append(",\"left\":").Append(TimeLeftMs).Append('}');

        b.Append(",\"top\":{");
        AppendTop(b, s.TopGifters);
        b.Append(',');
        AppendTop(b, s.TopLikers);
        b.Append('}');

        b.Append(",\"viewers\":{\"tiktok\":").Append(TikTokViewers).Append(",\"twitch\":").Append(TwitchViewers).Append(",\"list\":[");
        for (int i = 0; i < s.Viewers.Count; i++)
        {
            ViewerCounter v = s.Viewers[i];
            if (i > 0) b.Append(',');
            b.Append("{\"id\":").Append(v.Id).Append(",\"show\":").Append(v.Visible ? "true" : "false")
             .Append(",\"text\":").Append(App.Js(ViewerText(v))).Append('}');
        }
        b.Append("]}");

        AlertSetup a = s.Alerts;
        b.Append(",\"alerts\":{\"show\":").Append(a.Visible ? "true" : "false").Append(",\"secs\":").Append(a.Secs)
         .Append(",\"tag\":").Append(a.Tag ? "true" : "false").Append(",\"items\":[");
        for (int i = 0; i < alerts.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append("{\"id\":").Append(alerts[i].Id).Append(",\"p\":").Append(App.Js(alerts[i].Platform))
             .Append(",\"t\":").Append(App.Js(alerts[i].Text)).Append('}');
        }
        b.Append("]}");
    }

    // ---------------- viewer counts ----------------

    // -1 = not known (not connected)
    public int TikTokViewers { get; private set; }
    public int TwitchViewers { get; private set; }

    public void SetViewers(string platform, int n)
    {
        if (platform == Brand.Twitch) { if (TwitchViewers == n) return; TwitchViewers = n; }
        else { if (TikTokViewers == n) return; TikTokViewers = n; }
        MarkChanged(false, false);
    }

    public long ViewerValue(ViewerCounter v)
    {
        long tt = Math.Max(0, TikTokViewers), tw = Math.Max(0, TwitchViewers);
        return v.Source == ViewerCounter.FromTwitch ? tw : v.Source == ViewerCounter.FromBoth ? tt + tw : tt;
    }

    // "Twitch: 23"
    public string ViewerText(ViewerCounter v)
    {
        List<string> parts = new List<string>();
        if (v.Before.Length > 0) parts.Add(v.Before);
        parts.Add(ViewerValue(v).ToString("N0"));
        if (v.After.Length > 0) parts.Add(v.After);
        return string.Join(" ", parts.ToArray());
    }

    // ---------------- joint alerts (TikTok + Twitch) ----------------

    class AlertItem { public long Id; public string Platform = "", Text = ""; }
    readonly List<AlertItem> alerts = new List<AlertItem>();
    long alertId = DateTime.UtcNow.Ticks / 10000;   // keeps growing across restarts, so open overlays never miss one

    // Shows an alert on the alerts overlay. force = always (the Test button), else only the kinds that are on.
    public void Alert(string platform, string kind, long amount, string text, bool force)
    {
        if (!force && !app.S.Alerts.Wants(kind, amount)) return;
        AlertItem it = new AlertItem();
        it.Id = ++alertId;
        it.Platform = platform;
        it.Text = text.Length > 120 ? text.Substring(0, 119) + "…" : text;
        alerts.Add(it);
        if (alerts.Count > 20) alerts.RemoveAt(0);
        Diag.Count("alerts.shown");
        MarkChanged(false, false);
    }

    void AppendTop(StringBuilder b, TopSetup t)
    {
        b.Append('"').Append(t.Key).Append("\":{\"show\":").Append(t.Visible ? "true" : "false")
         .Append(",\"title\":").Append(App.Js(t.Title ?? "")).Append(",\"rows\":[");
        List<Fan> top = Top(t);
        for (int i = 0; i < top.Count; i++)
        {
            if (i > 0) b.Append(',');
            b.Append('[').Append(App.Js(Short(top[i].Name))).Append(',').Append(t.ByCoins ? top[i].Coins : top[i].Likes).Append(']');
        }
        b.Append("]}");
    }

    // ---------------- saving ----------------

    static string Clean(string s) { return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }

    static string FanLine(Fan f) { return Clean(f.Key) + "\t" + Clean(f.Name) + "\t" + f.Coins + "\t" + f.Likes; }

    static Fan ParseFan(string line)
    {
        string[] p = line.Split('\t');
        long coins, likes;
        if (p.Length < 4 || p[0].Length == 0 || !long.TryParse(p[2], out coins) || !long.TryParse(p[3], out likes)) return null;
        Fan f = new Fan();
        f.Key = p[0];
        f.Name = p[1];
        f.Coins = Math.Max(0, coins);
        f.Likes = Math.Max(0, likes);
        return f;
    }

    void Load()
    {
        try
        {
            if (File.Exists(liveFile))
            {
                string state = "";
                foreach (string line in File.ReadAllLines(liveFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1);
                    long n;
                    if (k == "fan") { Fan f = ParseFan(v); if (f != null) session[f.Key] = f; }
                    else if (k == "last" && long.TryParse(v, out n) && n > 0) lastEvent = new DateTime(n, DateTimeKind.Utc);
                    else if (k == "ended") liveEnded = Settings.ParseBool(v, false);
                    else if (k == "timer.state") state = v.Trim();
                    else if (k == "timer.left" && long.TryParse(v, out n)) leftMs = Math.Max(0, n);
                    else if (k == "timer.likes" && long.TryParse(v, out n)) likeCarry = (int)Math.Max(0, Math.Min(99, n));
                    else if (k.StartsWith("goal.") && long.TryParse(v, out n))
                    {
                        int id;
                        if (!int.TryParse(k.Substring(5), out id)) continue;
                        foreach (Goal g in app.S.Goals) if (g.Id == id) g.Value = Math.Max(0, n);
                    }
                }
                // a timer that was running when ClutchKit closed waits, paused, until you press Start
                if (state == "run" || state == "pause") TimerState = leftMs > 0 ? Clock.Paused : Clock.Done;
                else if (state == "done") TimerState = Clock.Done;
                if (state == "run" && leftMs > 0) app.Log(L.F("The subathon timer was running when ClutchKit closed. It's paused at {0} - press Start to go on.", ClockText(leftMs)));
                if (TimerState == Clock.Idle) leftMs = Cap(StartMs);
            }
            if (File.Exists(boardFile))
                foreach (string line in File.ReadAllLines(boardFile))
                {
                    Fan f = ParseFan(line);
                    if (f != null && allTime.Count < MaxFans) allTime[f.Key] = f;
                }
        }
        catch (Exception ex) { Diag.Error("widgets", "couldn't read live.txt / leaderboard.txt: " + ex.Message); }
        foreach (Goal g in app.S.Goals) g.Reached = g.Target > 0 && GoalValue(g) >= g.Target;
    }

    public void SaveAll()
    {
        save.Stop();
        if (liveDirty)
        {
            liveDirty = false;
            StringBuilder b = new StringBuilder();
            b.Append("# ClutchKit - live numbers: goals, subathon timer, this LIVE's top lists\r\n");
            if (lastEvent != DateTime.MinValue) b.Append("last=").Append(lastEvent.Ticks).Append("\r\n");
            b.Append("ended=").Append(liveEnded).Append("\r\n");
            string state = TimerState == Clock.Running ? "run" : TimerState == Clock.Paused ? "pause" : TimerState == Clock.Done ? "done" : "idle";
            b.Append("timer.state=").Append(state).Append("\r\n");
            b.Append("timer.left=").Append(TimeLeftMs).Append("\r\n");
            b.Append("timer.likes=").Append(likeCarry).Append("\r\n");
            foreach (Goal g in app.S.Goals) b.Append("goal.").Append(g.Id).Append('=').Append(g.Value).Append("\r\n");
            foreach (Fan f in session.Values) b.Append("fan=").Append(FanLine(f)).Append("\r\n");
            Settings.WriteFile(liveFile, b.ToString());
        }
        if (boardDirty)
        {
            boardDirty = false;
            StringBuilder b = new StringBuilder();
            foreach (Fan f in allTime.Values) b.Append(FanLine(f)).Append("\r\n");
            Settings.WriteFile(boardFile, b.ToString());
        }
    }

    // Saves everything on exit (also the time left of a running timer).
    public void Shutdown()
    {
        liveDirty = true;
        SaveAll();
        second.Stop();
        refresh.Stop();
    }
}
