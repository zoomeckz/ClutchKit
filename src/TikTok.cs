// ClutchKit - TikTok LIVE connection: reads gifts, follows, shares, joins, likes and chat from your LIVE so they can trigger actions.
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

    public TikTokLive(App app) { this.app = app; Detail = L.T("Not connected."); }

    public bool Running { get { return Current != State.Off && Current != State.Error; } }

    // "@name", "name" or a pasted link like "https://www.tiktok.com/@name/live?lang=en" -> "name"
    public static string CleanUser(string s)
    {
        s = (s ?? "").Trim();
        int at = s.LastIndexOf('@');
        if (at >= 0) s = s.Substring(at + 1);
        else
        {
            int t = s.IndexOf("tiktok.com/", StringComparison.OrdinalIgnoreCase);
            if (t >= 0) s = s.Substring(t + "tiktok.com/".Length);
        }
        int end = s.IndexOfAny(new[] { '/', '?', '#', ' ' });
        return (end >= 0 ? s.Substring(0, end) : s).Trim();
    }

    public void Start(string user, string apiKey)
    {
        Stop();
        user = CleanUser(user);
        apiKey = (apiKey ?? "").Trim();
        if (user.Length == 0) { SetStatus(State.Error, L.T("Enter your TikTok username first.")); return; }
        if (apiKey.Length == 0) { SetStatus(State.Error, L.T("Enter your Euler Stream API key first (it's free - see the link).")); return; }
        app.Engine.ResetLikes();
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
        app.Ui(delegate { if (app.W != null) app.W.SetViewers(Brand.TikTok, -1); });
        SetStatus(State.Off, L.T("Not connected."));
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
        Diag.Write("tiktok", "status " + s + ": " + detail);
        app.Ui(delegate { if (StatusChanged != null) StatusChanged(this, EventArgs.Empty); });
    }

    // Keeps the connection up: reconnects after drops and waits while you're not live.
    void Loop(string user, string apiKey, ManualResetEvent stop)
    {
        int backoff = 5;
        while (!stop.WaitOne(0))
        {
            Report(State.Connecting, L.F("Connecting to @{0}...", user));
            int code = -1;
            string reason = "";
            bool gotData = false;
            Diag.Count("tiktok.connects");
            try { RunOnce(user, apiKey, stop, out code, out reason, ref gotData); }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e.InnerException != null) e = e.InnerException;
                reason = e.Message;
                if (reason.Contains("401")) code = 4401;
                else if (reason.Contains("403")) code = 4403;
                else if (reason.Contains("429")) code = 4429;
                if (!stop.WaitOne(0)) Diag.Error("tiktok", e.GetType().Name + ": " + e.Message);
            }
            if (stop.WaitOne(0)) return;
            Diag.Count("tiktok.close." + code);
            Diag.Note("tiktok.lastclose", code + (reason.Length > 0 ? " " + reason : "") + " at " + DateTime.Now.ToString("HH:mm:ss") +
                                           (gotData ? " (had data)" : " (no data)"));
            Diag.Write("tiktok", "connection ended: code " + code + " " + reason + (gotData ? ", had data" : ", no data"));
            if (gotData) backoff = 5;

            int wait;
            switch (code)
            {
                case 4401:
                    Fail(L.T("Euler Stream says the API key isn't valid. Copy it again from your Euler Stream account."), stop); return;
                case 4400:
                    Fail(L.F("Euler Stream didn't accept the username \"{0}\". Check the spelling (no @ needed).", user), stop); return;
                case 4403:
                    Fail(L.F("Euler Stream says this key isn't allowed to read @{0}'s LIVE.", user), stop); return;
                case 4404:
                case 4005:
                    wait = 30;
                    Report(State.Waiting, L.F("@{0} isn't live right now. Checking again every {1} s - start your LIVE and it connects by itself.", user, wait));
                    break;
                case 4429:
                    wait = 60;
                    Report(State.Waiting, L.F("Too many connections on this Euler Stream key right now. Trying again in {0} s.", wait));
                    break;
                case 4555:
                    wait = 1;   // Euler Stream ends connections after 8 hours; just reconnect
                    break;
                default:
                    wait = backoff;
                    backoff = Math.Min(60, backoff * 2);
                    Report(State.Waiting, L.T("Connection lost") + (reason.Length > 0 ? " (" + reason + ")" : "") + ". " + L.F("Reconnecting in {0} s...", wait));
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
            Report(State.Connecting, L.F("Reached Euler Stream, waiting for @{0}'s LIVE...", user));

            byte[] buffer = new byte[64 * 1024];
            MemoryStream message = new MemoryStream();
            JavaScriptSerializer json = new JavaScriptSerializer();   // one for the whole connection, not one per message
            json.MaxJsonLength = int.MaxValue;
            json.RecursionLimit = 256;
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
                byte[] data = message.GetBuffer();   // no copy; valid up to message.Length
                int length = (int)message.Length;
                if (!gotData)
                {
                    // the first message means TikTok is really connected (not live = closed right away instead)
                    gotData = true;
                    Diag.Count("tiktok.connected");
                    Report(State.Connected, L.F("Connected - listening to @{0}'s LIVE.", user));
                    app.Log("TikTok LIVE: " + L.F("connected to @{0}.", user));
                    app.Ui(delegate { app.W.OnLiveStarted(); });   // a new LIVE? its top lists and goals start over
                }
                Handle(data, length, json);
                message.SetLength(0);
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

    void Handle(byte[] data, int length, JavaScriptSerializer json)
    {
        int i = 0;
        while (i < length && data[i] <= 32) i++;
        if (i >= length) return;
        if (data[i] != '{' && data[i] != '[')
        {
            Diag.Count("tiktok.msg.(not JSON)");
            if (Diag.WantsSample("(not JSON)")) Diag.Sample("(not JSON)", length + " bytes, starts with: " + BitConverter.ToString(data, i, Math.Min(64, length - i)));
            if (!warnedFormat)
            {
                warnedFormat = true;
                app.Log("TikTok LIVE: " + L.F("got data in a format ClutchKit doesn't read ({0} bytes). Gifts may not arrive - please report this.", length));
            }
            return;
        }
        object root;
        string text = Encoding.UTF8.GetString(data, i, length - i);
        try { root = json.DeserializeObject(text); }
        catch (Exception ex)
        {
            Diag.Count("tiktok.msg.(broken JSON)");
            Diag.Error("tiktok", "couldn't read a message (" + ex.Message + "): " + (text.Length > 300 ? text.Substring(0, 300) + "..." : text));
            return;
        }
        List<KeyValuePair<string, Dictionary<string, object>>> events = Events(root);
        if (events.Count == 0)
        {
            // JSON, but not the {type, data} events we expect - exactly what's needed to fix the reader
            Diag.Count("tiktok.msg.(no events)");
            if (Diag.WantsSample("(no events)")) Diag.Sample("(no events)", text);
        }
        foreach (KeyValuePair<string, Dictionary<string, object>> ev in events)
        {
            Diag.Count("tiktok.msg." + ev.Key);
            if (Diag.WantsSample(ev.Key)) { try { Diag.Sample(ev.Key, json.Serialize(ev.Value)); } catch { } }
            Dispatch(ev.Key, ev.Value);
        }
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
                Send(ParseGift(data), type);
                break;
            case "WebcastSocialMessage":
                Send(ParseSocial(data), type);
                break;
            case "WebcastMemberMessage":
                Send(ParseJoin(data), type);
                break;
            case "WebcastLikeMessage":
                Send(ParseLikes(data), type);
                break;
            case "WebcastChatMessage":
                Send(ParseChat(data), type);
                break;
            case "WebcastRoomUserSeqMessage":   // how many people watch right now
                {
                    long viewers = First(Num(data, "viewerCount"), Num(data, "total"));
                    if (viewers > 0) app.Ui(delegate { app.W.SetViewers(Brand.TikTok, (int)Math.Min(int.MaxValue, viewers)); });
                    break;
                }
            case "room.status":
                string state = Str(data, "state");
                if (state == "connected") Report(State.Connected, L.F("Connected - listening to @{0}'s LIVE.", User));
                else if (state == "offline" || state == "ended") Report(State.Waiting, L.F("@{0}'s LIVE has ended or is offline.", User));
                else if (state == "reconnecting") Report(State.Connecting, L.T("Euler Stream is reconnecting to TikTok..."));
                else if (state == "error" && Str(data, "message").Length > 0) app.Log("TikTok LIVE: " + Str(data, "message"));
                break;
            case "WebcastControlMessage":
                if (Num(data, "action") == 3)
                {
                    app.Log("TikTok LIVE: " + L.T("the LIVE ended."));
                    app.Ui(delegate { app.Engine.ResetLikes(); app.W.LiveEnded(); app.W.SetViewers(Brand.TikTok, 0); });   // the next LIVE counts from 0
                    Report(State.Waiting, L.F("@{0}'s LIVE has ended or is offline.", User));
                }
                break;
            case "decodeError":
                app.Log("TikTok LIVE: " + L.F("Euler Stream couldn't read a message ({0}).", Str(data, "message")));
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

    void Send(LiveEvent e, string type)
    {
        if (e == null) { Diag.Count("tiktok.ignored." + type); return; }
        if (Diag.Verbose)
            Diag.Write("tiktok", type + " -> " + e.Type + " from " + e.Who +
                       (e.Type == LiveEvent.Gift ? ": \"" + e.GiftName + "\" id " + e.GiftId + ", " + e.Coins + " coins, repeat " + e.RepeatCount +
                                                   (e.RepeatEnd ? " (end)" : "") + (e.Streakable ? ", combo" : "") : "") +
                       (e.Type == LiveEvent.Likes ? ": " + e.RepeatCount + " likes" : "") +
                       (e.Type == LiveEvent.Chat ? ": " + (e.Comment.Length > 80 ? e.Comment.Substring(0, 80) + "..." : e.Comment) : ""));
        app.Ui(delegate { app.Engine.OnEvent(e); });
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

    // A chat message ("content" in TikTok's own format, "comment" in some versions).
    public static LiveEvent ParseChat(Dictionary<string, object> data)
    {
        LiveEvent e = WithUser(data, LiveEvent.Chat);
        e.Comment = First(Str(data, "content"), Str(data, "comment")).Trim();
        return e.Comment.Length > 0 ? e : null;
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
