// ClutchKit - Twitch: reads chat, bits, subs, gifted subs and raids of a channel, and its viewer count.
// ----------------------------------------------------------------------------------
// Chat comes from Twitch's own chat server (wss://irc-ws.chat.twitch.tv), read anonymously like a
// logged-out viewer: no login, no key, and ClutchKit never writes in the chat. The viewer count comes
// from decapi.me (a free service), which only gets the channel name. Follows need a Twitch login, so
// they aren't available here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

public class TwitchChat
{
    public enum State { Off, Connecting, Connected, Waiting, Error }

    public const string ChatUrl = "wss://irc-ws.chat.twitch.tv:443";
    const string ViewerUrl = "https://decapi.me/twitch/viewercount/";

    readonly App app;
    readonly object gate = new object(), sendGate = new object();
    Thread thread;
    ManualResetEvent stopEvent;
    ClientWebSocket ws;
    CancellationTokenSource cts;
    Timer viewerTimer;

    public State Current { get; private set; }
    public string Detail { get; private set; }
    public string Channel { get; private set; }
    public event EventHandler StatusChanged;   // raised on the UI thread

    public TwitchChat(App app) { this.app = app; Detail = L.T("Not connected."); Channel = ""; }

    public bool Running { get { return Current != State.Off && Current != State.Error; } }

    // "#name", "@name", "Name" or "https://www.twitch.tv/name" -> "name"
    public static string CleanChannel(string s)
    {
        s = (s ?? "").Trim();
        int t = s.IndexOf("twitch.tv/", StringComparison.OrdinalIgnoreCase);
        if (t >= 0) s = s.Substring(t + "twitch.tv/".Length);
        s = s.TrimStart('#', '@');
        int end = s.IndexOfAny(new[] { '/', '?', '#', ' ' });
        if (end >= 0) s = s.Substring(0, end);
        StringBuilder b = new StringBuilder();
        foreach (char c in s.ToLowerInvariant()) if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') b.Append(c);
        return b.ToString();
    }

    public void Start(string channel)
    {
        Stop();
        channel = CleanChannel(channel);
        if (channel.Length == 0) { SetStatus(State.Error, L.T("Enter your Twitch channel name first.")); return; }
        lock (gate)
        {
            Channel = channel;
            ManualResetEvent stop = new ManualResetEvent(false);
            stopEvent = stop;
            Thread th = new Thread(delegate () { Loop(channel, stop); });
            th.IsBackground = true;
            th.Name = "Twitch";
            thread = th;
            th.Start();
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
            if (viewerTimer != null) { viewerTimer.Dispose(); viewerTimer = null; }
        }
        app.Ui(delegate { app.W.SetViewers(Brand.Twitch, -1); });
        SetStatus(State.Off, L.T("Not connected."));
    }

    void Report(State s, string detail)
    {
        lock (gate) { if (Thread.CurrentThread != thread) return; }
        SetStatus(s, detail);
    }

    void SetStatus(State s, string detail)
    {
        Current = s;
        Detail = detail;
        Diag.Write("twitch", "status " + s + ": " + detail);
        app.Ui(delegate { if (StatusChanged != null) StatusChanged(this, EventArgs.Empty); });
    }

    void Loop(string channel, ManualResetEvent stop)
    {
        int backoff = 3;
        while (!stop.WaitOne(0))
        {
            Report(State.Connecting, L.F("Connecting to {0}'s Twitch chat...", channel));
            bool joined = false;
            string reason = "";
            Diag.Count("twitch.connects");
            try { RunOnce(channel, stop, ref joined, out reason); }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e.InnerException != null) e = e.InnerException;
                reason = e.Message;
                if (!stop.WaitOne(0)) Diag.Write("twitch", e.GetType().Name + ": " + e.Message);
            }
            if (stop.WaitOne(0)) return;
            Diag.Write("twitch", "connection ended: " + reason);
            if (reason == "suspended")
            {
                lock (gate) { if (stopEvent != stop) return; }
                string msg = L.F("Twitch says the channel \"{0}\" doesn't exist or is suspended. Check the spelling.", channel);
                app.Log("Twitch: " + msg);
                Report(State.Error, msg);
                lock (gate) { if (stopEvent == stop) { stopEvent = null; thread = null; } }
                return;
            }
            if (joined) { backoff = 3; app.Log("Twitch: " + L.T("connection lost.")); }
            int wait = backoff;
            backoff = Math.Min(60, backoff * 2);
            Report(State.Waiting, L.T("Connection lost") + (reason.Length > 0 && reason != "reconnect" ? " (" + reason + ")" : "") + ". " +
                                  L.F("Reconnecting in {0} s...", wait));
            if (stop.WaitOne(wait * 1000)) return;
        }
    }

    void RunOnce(string channel, ManualResetEvent stop, ref bool joined, out string reason)
    {
        reason = "";
        ClientWebSocket socket = new ClientWebSocket();
        CancellationTokenSource source = new CancellationTokenSource();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        lock (gate)
        {
            if (stop.WaitOne(0)) return;
            ws = socket;
            cts = source;
        }
        try
        {
            if (!socket.ConnectAsync(new Uri(ChatUrl), source.Token).Wait(15000)) throw new TimeoutException("no answer");
            string nick = "justinfan" + new Random().Next(10000, 99999);   // Twitch's name for logged-out viewers
            Send(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands");
            Send(socket, "PASS SCHMOOPIIE");
            Send(socket, "NICK " + nick);
            Send(socket, "JOIN #" + channel);

            byte[] buffer = new byte[32 * 1024];
            MemoryStream message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult r = socket.ReceiveAsync(new ArraySegment<byte>(buffer), source.Token).Result;
                if (r.MessageType == WebSocketMessageType.Close) { reason = r.CloseStatusDescription ?? ""; break; }
                message.Write(buffer, 0, r.Count);
                if (message.Length > 4 * 1024 * 1024) message.SetLength(0);
                if (!r.EndOfMessage) continue;
                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string end = Handle(socket, line, channel, ref joined);
                    if (end != null) { reason = end; return; }
                }
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

    void Send(ClientWebSocket socket, string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        lock (sendGate) socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000);
    }

    // One chat line. Returns a reason to end the connection, or null.
    string Handle(ClientWebSocket socket, string line, string channel, ref bool joined)
    {
        Dictionary<string, string> tags;
        string prefix, command, trailing;
        List<string> args;
        Parse(line, out tags, out prefix, out command, out args, out trailing);
        switch (command)
        {
            case "PING":
                Send(socket, "PONG :" + trailing);
                return null;
            case "RECONNECT":
                return "reconnect";   // Twitch restarts a server; connect again
            case "ROOMSTATE":
            case "366":   // end of the viewer list = joined
                if (!joined)
                {
                    joined = true;
                    Diag.Count("twitch.connected");
                    Report(State.Connected, L.F("Connected - reading {0}'s Twitch chat.", channel));
                    app.Log("Twitch: " + L.F("connected to {0}.", channel));
                    StartViewerCount(channel);
                }
                return null;
            case "NOTICE":
                {
                    string id = Tag(tags, "msg-id");
                    Diag.Write("twitch", "notice " + id + ": " + trailing);
                    if (id == "msg_channel_suspended" || id == "msg_banned") return "suspended";
                    return null;
                }
            case "PRIVMSG":
                OnChat(tags, prefix, trailing);
                return null;
            case "USERNOTICE":
                OnUserNotice(tags, prefix);
                return null;
        }
        return null;
    }

    static string Nick(string prefix)
    {
        int bang = prefix.IndexOf('!');
        return bang > 0 ? prefix.Substring(0, bang) : prefix;
    }

    static string Who(Dictionary<string, string> tags, string prefix)
    {
        string name = Tag(tags, "display-name");
        if (name.Length == 0) name = Tag(tags, "login");
        if (name.Length == 0) name = Nick(prefix);
        return name;
    }

    void OnChat(Dictionary<string, string> tags, string prefix, string text)
    {
        Diag.Count("twitch.msg.chat");
        string who = Who(tags, prefix);
        int bits;
        if (int.TryParse(Tag(tags, "bits"), out bits) && bits > 0)
        {
            Diag.Count("twitch.msg.bits");
            Alert(AlertKind.Bits, bits, L.F("{0} cheered {1} bits", who, bits.ToString("N0")));
        }
        LiveEvent e = new LiveEvent();
        e.Type = LiveEvent.Chat;
        e.Platform = Brand.Twitch;
        e.User = Nick(prefix);
        e.Nick = who;
        e.UserId = Tag(tags, "user-id").Length > 0 ? "twitch:" + Tag(tags, "user-id") : "";
        e.Comment = text.Trim();
        if (Diag.Verbose) Diag.Write("twitch", "chat from " + who + ": " + (e.Comment.Length > 80 ? e.Comment.Substring(0, 80) + "..." : e.Comment));
        if (e.Comment.Length > 0) app.Ui(delegate { app.Engine.OnEvent(e); });
    }

    void OnUserNotice(Dictionary<string, string> tags, string prefix)
    {
        string id = Tag(tags, "msg-id"), who = Who(tags, prefix);
        Diag.Count("twitch.msg." + (id.Length > 0 ? id : "usernotice"));
        int n;
        switch (id)
        {
            case "sub":
            case "resub":
                int.TryParse(Tag(tags, "msg-param-cumulative-months"), out n);
                Alert(AlertKind.Subs, 1, n > 1 ? L.F("{0} subscribed ({1} months)", who, n) : L.F("{0} subscribed", who));
                break;
            case "subgift":
                if (Tag(tags, "msg-param-community-gift-id").Length > 0) break;   // part of "gifted N subs", shown once
                Alert(AlertKind.Subs, 1, L.F("{0} gifted a sub to {1}", who, Tag(tags, "msg-param-recipient-display-name")));
                break;
            case "submysterygift":
                int.TryParse(Tag(tags, "msg-param-mass-gift-count"), out n);
                Alert(AlertKind.Subs, Math.Max(1, n), L.F("{0} gifted {1} subs", who, Math.Max(1, n)));
                break;
            case "raid":
                int.TryParse(Tag(tags, "msg-param-viewerCount"), out n);
                string raider = Tag(tags, "msg-param-displayName");
                Alert(AlertKind.Raids, n, L.F("{0} raided with {1} viewers", raider.Length > 0 ? raider : who, n.ToString("N0")));
                break;
        }
    }

    void Alert(string kind, long amount, string text)
    {
        app.Log("Twitch: " + text);
        app.Ui(delegate { app.W.Alert(Brand.Twitch, kind, amount, text, false); });
    }

    // ---------------- viewer count (decapi.me) ----------------

    void StartViewerCount(string channel)
    {
        lock (gate)
        {
            if (viewerTimer != null) viewerTimer.Dispose();
            viewerTimer = new Timer(delegate { FetchViewers(channel); }, null, 0, 60000);
        }
    }

    void FetchViewers(string channel)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ViewerUrl + Uri.EscapeDataString(channel));
            req.UserAgent = "ClutchKit";
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            string text;
            using (WebResponse res = req.GetResponse())
            using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                text = rd.ReadToEnd().Trim();
            int n;
            if (int.TryParse(text.Replace(",", ""), out n)) { }
            else if (text.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0) n = 0;
            else { Diag.Write("twitch", "viewer count: unexpected answer: " + (text.Length > 120 ? text.Substring(0, 120) : text)); return; }
            Diag.Count("twitch.viewers");
            Diag.Note("twitch.viewers", n + " at " + DateTime.Now.ToString("HH:mm:ss"));
            lock (gate) { if (!Running || Channel != channel) return; }
            app.Ui(delegate { app.W.SetViewers(Brand.Twitch, n); });
        }
        catch (Exception ex) { Diag.Write("twitch", "viewer count: " + ex.Message); }
    }

    // ---------------- chat lines ----------------

    // "@a=1;b=2 :nick!nick@host PRIVMSG #chan :hello" -> tags, prefix, command, args, trailing
    static void Parse(string line, out Dictionary<string, string> tags, out string prefix, out string command, out List<string> args, out string trailing)
    {
        tags = new Dictionary<string, string>();
        prefix = command = trailing = "";
        args = new List<string>();
        int i = 0;
        if (line.StartsWith("@"))
        {
            int sp = line.IndexOf(' ');
            if (sp < 0) return;
            foreach (string kv in line.Substring(1, sp - 1).Split(';'))
            {
                int eq = kv.IndexOf('=');
                if (eq > 0) tags[kv.Substring(0, eq)] = Unescape(kv.Substring(eq + 1));
            }
            i = sp + 1;
        }
        if (i < line.Length && line[i] == ':')
        {
            int sp = line.IndexOf(' ', i);
            if (sp < 0) return;
            prefix = line.Substring(i + 1, sp - i - 1);
            i = sp + 1;
        }
        int colon = line.IndexOf(" :", i, StringComparison.Ordinal);
        string middle = colon >= 0 ? line.Substring(i, colon - i) : line.Substring(i);
        if (colon >= 0) trailing = line.Substring(colon + 2);
        string[] parts = middle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0) command = parts[0].ToUpperInvariant();
        for (int k = 1; k < parts.Length; k++) args.Add(parts[k]);
    }

    static string Unescape(string v)
    {
        if (v.IndexOf('\\') < 0) return v;
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] != '\\' || i + 1 >= v.Length) { b.Append(v[i]); continue; }
            char c = v[++i];
            b.Append(c == 's' ? ' ' : c == ':' ? ';' : c == 'r' ? '\r' : c == 'n' ? '\n' : c);
        }
        return b.ToString();
    }

    static string Tag(Dictionary<string, string> tags, string key)
    {
        string v;
        return tags.TryGetValue(key, out v) ? v : "";
    }
}
