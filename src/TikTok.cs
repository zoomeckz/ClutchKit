// ClutchKit - TikTok LIVE connection: reads gifts, follows, shares, joins and likes from your LIVE so they can trigger actions.
// ----------------------------------------------------------------------------------
// TikTok has no official API for LIVE events. ClutchKit uses Euler Stream's WebSocket service
// (wss://ws.eulerstream.com, free account + API key), which connects to TikTok and passes the
// LIVE events on as JSON. Only the TikTok username and the API key are sent, and only after the
// user presses Connect (or turns on "connect automatically").
// TikTok can change things on their side at any time, so this part can stop working until updated.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public class TikTokLive
{
    public enum State { Off, Connecting, Connected, Waiting, Error }

    public const string ServiceUrl = "wss://ws.eulerstream.com";
    public const string SignupUrl = "https://www.eulerstream.com";

    readonly App app;
    readonly object gate = new object();
    Thread thread;
    ManualResetEvent stopEvent;
    ClientWebSocket ws;
    CancellationTokenSource cts;
    bool warnedFormat;

    public State Current { get; private set; }
    public string Detail { get; private set; }
    public string User { get; private set; }
    public event EventHandler StatusChanged;   // raised on the UI thread

    public TikTokLive(App app) { this.app = app; Detail = "Not connected."; }

    public bool Running { get { return Current != State.Off && Current != State.Error; } }

    public static string CleanUser(string s)
    {
        s = (s ?? "").Trim();
        s = s.Replace("https://www.tiktok.com/", "").Replace("/live", "").Replace("@", "").Trim().Trim('/');
        return s;
    }

    public void Start(string user, string apiKey)
    {
        Stop();
        user = CleanUser(user);
        apiKey = (apiKey ?? "").Trim();
        if (user.Length == 0) { SetStatus(State.Error, "Enter your TikTok username first."); return; }
        if (apiKey.Length == 0) { SetStatus(State.Error, "Enter your Euler Stream API key first (it's free - see the link)."); return; }
        lock (gate)
        {
            User = user;
            ManualResetEvent stop = new ManualResetEvent(false);
            stopEvent = stop;
            Thread t = new Thread(delegate () { Loop(user, apiKey, stop); });
            t.IsBackground = true;
            t.Name = "TikTok LIVE";
            thread = t;
            t.Start();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (stopEvent != null) stopEvent.Set();
            stopEvent = null;
            thread = null;
            try { if (cts != null) cts.Cancel(); } catch { }
            try { if (ws != null) ws.Abort(); } catch { }
        }
        SetStatus(State.Off, "Not connected.");
    }

    // Status from the connection thread: ignored once that connection was stopped or replaced.
    void Report(State s, string detail)
    {
        lock (gate) { if (Thread.CurrentThread != thread) return; }
        SetStatus(s, detail);
    }

    void SetStatus(State s, string detail)
    {
        Current = s;
        Detail = detail;
        app.Ui(delegate { if (StatusChanged != null) StatusChanged(this, EventArgs.Empty); });
    }

    // Keeps the connection up: reconnects after drops and waits while you're not live.
    void Loop(string user, string apiKey, ManualResetEvent stop)
    {
        int backoff = 5;
        while (!stop.WaitOne(0))
        {
            Report(State.Connecting, "Connecting to @" + user + "...");
            int code = -1;
            string reason = "";
            bool gotData = false;
            try { RunOnce(user, apiKey, stop, out code, out reason, ref gotData); }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e.InnerException != null) e = e.InnerException;
                reason = e.Message;
                if (reason.Contains("401")) code = 4401;
                else if (reason.Contains("403")) code = 4403;
                else if (reason.Contains("429")) code = 4429;
            }
            if (stop.WaitOne(0)) return;
            if (gotData) backoff = 5;

            int wait;
            switch (code)
            {
                case 4401:
                    Fail("Euler Stream says the API key isn't valid. Copy it again from your Euler Stream account.", stop); return;
                case 4400:
                    Fail("Euler Stream didn't accept the username \"" + user + "\". Check the spelling (no @ needed).", stop); return;
                case 4403:
                    Fail("Euler Stream says this key isn't allowed to read @" + user + "'s LIVE.", stop); return;
                case 4404:
                case 4005:
                    wait = 30;
                    Report(State.Waiting, "@" + user + " isn't live right now. Checking again every " + wait + " s - start your LIVE and it connects by itself.");
                    break;
                case 4429:
                    wait = 60;
                    Report(State.Waiting, "Too many connections on this Euler Stream key right now. Trying again in " + wait + " s.");
                    break;
                case 4555:
                    wait = 1;   // Euler Stream ends connections after 8 hours; just reconnect
                    break;
                default:
                    wait = backoff;
                    backoff = Math.Min(60, backoff * 2);
                    Report(State.Waiting, "Connection lost" + (reason.Length > 0 ? " (" + reason + ")" : "") + ". Reconnecting in " + wait + " s...");
                    break;
            }
            if (stop.WaitOne(wait * 1000)) return;
        }
    }

    void Fail(string message, ManualResetEvent stop)
    {
        lock (gate) { if (stopEvent != stop) return; }
        app.Log("TikTok LIVE: " + message);
        Report(State.Error, message);
        lock (gate) { if (stopEvent == stop) { stopEvent = null; thread = null; } }
    }

    void RunOnce(string user, string apiKey, ManualResetEvent stop, out int code, out string reason, ref bool gotData)
    {
        code = -1;
        reason = "";
        ClientWebSocket socket = new ClientWebSocket();
        CancellationTokenSource source = new CancellationTokenSource();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        lock (gate)
        {
            if (stop.WaitOne(0)) return;
            ws = socket;
            cts = source;
        }
        try
        {
            string url = ServiceUrl + "?uniqueId=" + Uri.EscapeDataString(user) + "&apiKey=" + Uri.EscapeDataString(apiKey);
            socket.ConnectAsync(new Uri(url), source.Token).Wait();
            Report(State.Connecting, "Reached Euler Stream, waiting for @" + user + "'s LIVE...");

            byte[] buffer = new byte[64 * 1024];
            MemoryStream message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult r = socket.ReceiveAsync(new ArraySegment<byte>(buffer), source.Token).Result;
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    code = r.CloseStatus.HasValue ? (int)r.CloseStatus.Value : -1;
                    reason = r.CloseStatusDescription ?? "";
                    try { socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(2000); } catch { }
                    break;
                }
                message.Write(buffer, 0, r.Count);
                if (message.Length > 16 * 1024 * 1024) message.SetLength(0);   // something is wrong; don't eat memory
                if (!r.EndOfMessage) continue;
                byte[] data = message.ToArray();
                message.SetLength(0);
                if (!gotData)
                {
                    // the first message means TikTok is really connected (not live = closed right away instead)
                    gotData = true;
                    Report(State.Connected, "Connected - listening to @" + user + "'s LIVE.");
                    app.Log("TikTok LIVE: connected to @" + user + ".");
                }
                Handle(data);
            }
            if (code == -1 && socket.CloseStatus.HasValue)
            {
                code = (int)socket.CloseStatus.Value;
                reason = socket.CloseStatusDescription ?? "";
            }
        }
        finally
        {
            lock (gate)
            {
                if (ws == socket) ws = null;
                if (cts == source) cts = null;
            }
            try { socket.Dispose(); } catch { }
            source.Dispose();
        }
    }

    // ---------------- messages ----------------

    void Handle(byte[] data)
    {
        int i = 0;
        while (i < data.Length && data[i] <= 32) i++;
        if (i >= data.Length) return;
        if (data[i] != '{' && data[i] != '[')
        {
            if (!warnedFormat)
            {
                warnedFormat = true;
                app.Log("TikTok LIVE: got data in a format ClutchKit doesn't read (" + data.Length + " bytes). Gifts may not arrive - please report this.");
            }
            return;
        }
        object root;
        try
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            js.RecursionLimit = 256;
            root = js.DeserializeObject(Encoding.UTF8.GetString(data, i, data.Length - i));
        }
        catch { return; }
        foreach (KeyValuePair<string, Dictionary<string, object>> ev in Events(root)) Dispatch(ev.Key, ev.Value);
    }

    // Finds {"type": ..., "data": {...}} events anywhere in a message (single or bundled).
    public static List<KeyValuePair<string, Dictionary<string, object>>> Events(object root)
    {
        List<KeyValuePair<string, Dictionary<string, object>>> list = new List<KeyValuePair<string, Dictionary<string, object>>>();
        Collect(root, list, 0);
        return list;
    }

    static void Collect(object o, List<KeyValuePair<string, Dictionary<string, object>>> list, int depth)
    {
        if (o == null || depth > 4) return;
        IDictionary<string, object> d = o as IDictionary<string, object>;
        if (d != null)
        {
            object t, data;
            if (d.TryGetValue("type", out t) && t is string && d.TryGetValue("data", out data) && data is Dictionary<string, object>)
            {
                list.Add(new KeyValuePair<string, Dictionary<string, object>>((string)t, (Dictionary<string, object>)data));
                return;
            }
            foreach (string k in new[] { "messages", "events", "data" })
                if (d.TryGetValue(k, out data)) Collect(data, list, depth + 1);
            return;
        }
        IEnumerable e = o as IEnumerable;
        if (e != null && !(o is string)) foreach (object x in e) Collect(x, list, depth + 1);
    }

    void Dispatch(string type, Dictionary<string, object> data)
    {
        switch (type)
        {
            case "WebcastGiftMessage":
                Send(ParseGift(data));
                break;
            case "WebcastSocialMessage":
                Send(ParseSocial(data));
                break;
            case "WebcastMemberMessage":
                Send(ParseJoin(data));
                break;
            case "WebcastLikeMessage":
                Send(ParseLikes(data));
                break;
            case "room.status":
                string state = Str(data, "state");
                if (state == "connected") Report(State.Connected, "Connected - listening to @" + User + "'s LIVE.");
                else if (state == "offline" || state == "ended") Report(State.Waiting, "@" + User + "'s LIVE is " + state + ".");
                else if (state == "reconnecting") Report(State.Connecting, "Euler Stream is reconnecting to TikTok...");
                else if (state == "error" && Str(data, "message").Length > 0) app.Log("TikTok LIVE: " + Str(data, "message"));
                break;
            case "WebcastControlMessage":
                if (Num(data, "action") == 3) { app.Log("TikTok LIVE: the LIVE ended."); Report(State.Waiting, "@" + User + "'s LIVE ended."); }
                break;
            case "decodeError":
                app.Log("TikTok LIVE: Euler Stream couldn't read a message (" + Str(data, "message") + ").");
                break;
        }
    }

    // Reads a gift in any of Euler Stream's schema versions (v1 "giftDetails", v2/v3 "gift").
    public static LiveEvent ParseGift(Dictionary<string, object> data)
    {
        LiveEvent g = new LiveEvent();
        Dictionary<string, object> details = Obj(data, "giftDetails"), gift = Obj(data, "gift"), user = Obj(data, "user");
        g.GiftName = First(Str(details, "giftName"), Str(gift, "name"), Str(details, "describe"));
        g.Coins = (int)First(Num(details, "diamondCount"), Num(gift, "diamondCount"));
        long type = First(Num(details, "giftType"), Num(gift, "type"));
        g.Streakable = type == 1 || Bool(gift, "combo");
        g.GiftId = First(Str(data, "giftId"), Str(gift, "id"));
        g.GroupId = Str(data, "groupId");
        g.RepeatCount = (int)Math.Max(1, Math.Min(100000, Num(data, "repeatCount")));
        g.RepeatEnd = Num(data, "repeatEnd") != 0;
        g.User = First(Str(user, "uniqueId"), Str(user, "displayId"));
        g.Nick = Str(user, "nickname");
        g.UserId = First(Str(user, "userId"), Str(user, "id"));
        return g;
    }

    void Send(LiveEvent e)
    {
        if (e != null) app.Ui(delegate { app.Engine.OnEvent(e); });
    }

    static LiveEvent WithUser(Dictionary<string, object> data, string type)
    {
        LiveEvent e = new LiveEvent();
        e.Type = type;
        Dictionary<string, object> user = Obj(data, "user");
        e.User = First(Str(user, "uniqueId"), Str(user, "displayId"));
        e.Nick = Str(user, "nickname");
        e.UserId = First(Str(user, "userId"), Str(user, "id"));
        return e;
    }

    // Follow or share (both arrive as a "social" message; the display type tells them apart).
    public static LiveEvent ParseSocial(Dictionary<string, object> data)
    {
        Dictionary<string, object> common = Obj(data, "common"), shown = Obj(common, "displayText");
        string hint = (Str(Obj(Obj(data, "event"), "eventDetails"), "displayType") + " " + Str(shown, "key") + " " +
                       Str(shown, "defaultPattern") + " " + Str(data, "action")).ToLowerInvariant();
        if (hint.Contains("follow") || (Num(data, "followCount") > 0 && Num(data, "shareCount") == 0 && Str(data, "shareType").Length == 0))
            return WithUser(data, LiveEvent.Follow);
        if (hint.Contains("share") || Num(data, "shareCount") > 0 || Str(data, "shareType").Length > 0)
            return WithUser(data, LiveEvent.Share);
        return null;
    }

    // Someone joined the LIVE (member messages with other actions are ignored).
    public static LiveEvent ParseJoin(Dictionary<string, object> data)
    {
        long actionId = Num(data, "actionId");
        string action = Str(data, "action");
        bool join = actionId <= 1 && (action.Length == 0 || action == "0" || action == "1" || action.ToUpperInvariant().Contains("JOIN"));
        return join ? WithUser(data, LiveEvent.Join) : null;
    }

    public static LiveEvent ParseLikes(Dictionary<string, object> data)
    {
        LiveEvent e = WithUser(data, LiveEvent.Likes);
        e.RepeatCount = (int)Math.Max(1, Math.Min(100000, First(Num(data, "likeCount"), Num(data, "count"))));
        return e;
    }

    static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        if (d == null || !d.TryGetValue(key, out v) || v == null || v is IDictionary || (v is IEnumerable && !(v is string))) return "";
        return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    static long Num(Dictionary<string, object> d, string key)
    {
        long n;
        string s = Str(d, key);
        if (long.TryParse(s, out n)) return n;
        decimal m;
        return decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m) ? (long)m : 0;
    }

    static bool Bool(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
    }

    static string First(params string[] values)
    {
        foreach (string v in values) if (!string.IsNullOrEmpty(v)) return v;
        return "";
    }

    static long First(params long[] values)
    {
        foreach (long v in values) if (v != 0) return v;
        return 0;
    }
}
