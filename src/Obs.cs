// ClutchKit - OBS Studio: browser source links for the overlays, and actions that control OBS
// (switch scene, show/hide a source, save the replay buffer, record, mute).
// ----------------------------------------------------------------------------------
// OBS 28 and newer have a WebSocket server built in (Tools > WebSocket Server Settings). ClutchKit
// speaks its protocol (version 5, JSON) to the address you enter - normally this PC (127.0.0.1).
// Nothing else is sent anywhere.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public class ObsLink
{
    public enum State { Off, Connecting, Connected, Waiting, Error }

    class Pending
    {
        public readonly ManualResetEvent Done = new ManualResetEvent(false);
        public Dictionary<string, object> Data;
        public string Error;
    }

    // things to undo later ("for 10 s"): the original state is kept when the same thing is triggered again
    class Revert { public Timer Timer; public object Before; public Action<object> Undo; }

    const int RpcVersion = 1;
    const int EventsScenes = 1 << 2, EventsInputs = 1 << 3;

    readonly App app;
    readonly object gate = new object(), sendGate = new object();
    readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
    readonly Dictionary<string, Revert> reverts = new Dictionary<string, Revert>();
    Thread thread;
    ManualResetEvent stopEvent;
    ClientWebSocket ws;
    CancellationTokenSource cts;
    int nextId;
    int listsBusy;

    public State Current { get; private set; }
    public string Detail { get; private set; }
    public string Version { get; private set; }   // OBS version, once connected
    public string[] Scenes { get; private set; }    // scene names, once connected
    public string[] Sources { get; private set; }   // input (source) names, once connected
    public string[] Audio { get; private set; }     // inputs that have audio (for mute)
    public event EventHandler StatusChanged;        // raised on the UI thread
    public event EventHandler ListsChanged;         // raised on the UI thread

    public ObsLink(App app)
    {
        this.app = app;
        Detail = L.T("Not connected.");
        Version = "";
        Scenes = Sources = Audio = new string[0];
    }

    public bool Running { get { return Current != State.Off && Current != State.Error; } }
    public bool Connected { get { return Current == State.Connected; } }

    public void Start(string host, int port, string password)
    {
        Stop();
        host = CleanHost(host);
        if (host.Length == 0) { SetStatus(State.Error, L.T("Enter the OBS address first (127.0.0.1 if OBS runs on this PC).")); return; }
        Uri uri;
        if (!Uri.TryCreate("ws://" + host + ":" + port + "/", UriKind.Absolute, out uri))
        {
            SetStatus(State.Error, L.F("\"{0}\" isn't an address ClutchKit can use.", host));
            return;
        }
        lock (gate)
        {
            ManualResetEvent stop = new ManualResetEvent(false);
            stopEvent = stop;
            string pw = password ?? "";
            Thread t = new Thread(delegate () { Loop(uri, pw, stop); });
            t.IsBackground = true;
            t.Name = "OBS";
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
        FailAll(L.T("not connected"));
        lock (reverts)
        {
            foreach (Revert r in reverts.Values) r.Timer.Dispose();
            reverts.Clear();
        }
        SetStatus(State.Off, L.T("Not connected."));
    }

    // "ws://localhost:4455/" or "127.0.0.1" -> "127.0.0.1" / "localhost"
    public static string CleanHost(string s)
    {
        s = (s ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s.Substring(scheme + 3);
        int end = s.IndexOfAny(new[] { '/', ':', ' ', '?', '#' });
        return (end >= 0 ? s.Substring(0, end) : s).Trim();
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
        Diag.Write("obs", "status " + s + ": " + detail);
        app.Ui(delegate { if (StatusChanged != null) StatusChanged(this, EventArgs.Empty); });
    }

    // Keeps the connection up: waits while OBS is closed and reconnects when it opens.
    void Loop(Uri uri, string password, ManualResetEvent stop)
    {
        int backoff = 3;
        while (!stop.WaitOne(0))
        {
            Report(State.Connecting, L.F("Connecting to OBS at {0}...", uri.Host + ":" + uri.Port));
            int code = -1;
            string reason = "";
            bool identified = false;
            Diag.Count("obs.connects");
            try { RunOnce(uri, password, stop, out code, out reason, ref identified); }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e.InnerException != null) e = e.InnerException;
                reason = e.Message;
                if (!stop.WaitOne(0)) Diag.Write("obs", e.GetType().Name + ": " + e.Message);
            }
            FailAll(L.T("the connection to OBS was lost"));
            if (stop.WaitOne(0)) return;
            Diag.Write("obs", "connection ended: code " + code + " " + reason);
            if (identified)
            {
                backoff = 3;
                app.Log("OBS: " + L.T("connection lost."));
                lock (gate) { if (stopEvent == stop) Version = ""; }
            }

            switch (code)
            {
                case 4009:   // AuthenticationFailed
                    Fail(password.Length == 0
                        ? L.T("OBS asks for a password. Copy it from OBS (Tools > WebSocket Server Settings > Show Connect Info).")
                        : L.T("OBS says the password is wrong. Copy it again from OBS (Tools > WebSocket Server Settings > Show Connect Info)."), stop);
                    return;
                case 4010:   // UnsupportedRpcVersion
                    Fail(L.T("This OBS version is too old or too new for ClutchKit. OBS 28 or newer is needed."), stop);
                    return;
            }
            int wait = backoff;
            backoff = Math.Min(30, backoff * 2);
            Report(State.Waiting, identified
                ? L.T("Connection lost") + ". " + L.F("Reconnecting in {0} s...", wait)
                : L.F("OBS isn't answering. Is OBS open, with the WebSocket server turned on? Trying again in {0} s...", wait));
            if (stop.WaitOne(wait * 1000)) return;
        }
    }

    void Fail(string message, ManualResetEvent stop)
    {
        lock (gate) { if (stopEvent != stop) return; }
        app.Log("OBS: " + message);
        Report(State.Error, message);
        lock (gate) { if (stopEvent == stop) { stopEvent = null; thread = null; } }
    }

    void RunOnce(Uri uri, string password, ManualResetEvent stop, out int code, out string reason, ref bool identified)
    {
        code = -1;
        reason = "";
        ClientWebSocket socket = new ClientWebSocket();
        CancellationTokenSource source = new CancellationTokenSource();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.AddSubProtocol("obswebsocket.json");
        lock (gate)
        {
            if (stop.WaitOne(0)) return;
            ws = socket;
            cts = source;
        }
        try
        {
            if (!socket.ConnectAsync(uri, source.Token).Wait(8000)) throw new TimeoutException("no answer");
            byte[] buffer = new byte[64 * 1024];
            MemoryStream message = new MemoryStream();
            JavaScriptSerializer json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;
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
                if (message.Length > 16 * 1024 * 1024) message.SetLength(0);
                if (!r.EndOfMessage) continue;
                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                Dictionary<string, object> m;
                try { m = json.DeserializeObject(text) as Dictionary<string, object>; }
                catch { Diag.Write("obs", "couldn't read: " + (text.Length > 200 ? text.Substring(0, 200) : text)); continue; }
                if (m == null) continue;
                int op = Int(m, "op");
                Dictionary<string, object> d = Obj(m, "d");
                if (d == null) continue;
                switch (op)
                {
                    case 0:   // Hello: answer with Identify (and the password, if OBS wants one)
                        {
                            Dictionary<string, object> id = new Dictionary<string, object>();
                            id["rpcVersion"] = RpcVersion;
                            id["eventSubscriptions"] = EventsScenes | EventsInputs;
                            Dictionary<string, object> auth = Obj(d, "authentication");
                            if (auth != null) id["authentication"] = Auth(password, Str(auth, "salt"), Str(auth, "challenge"));
                            string obsVersion = Str(d, "obsWebSocketVersion");
                            Diag.Write("obs", "hello: websocket " + obsVersion + (auth != null ? ", password needed" : ", no password"));
                            Send(socket, Envelope(1, id));
                            break;
                        }
                    case 2:   // Identified
                        identified = true;
                        Diag.Count("obs.connected");
                        Report(State.Connected, L.T("Connected to OBS."));
                        app.Log("OBS: " + L.T("connected."));
                        ThreadPool.QueueUserWorkItem(delegate { AfterConnect(); });
                        break;
                    case 5:   // Event: scenes or sources added, removed or renamed
                        {
                            string type = Str(d, "eventType");
                            if (type == "SceneCreated" || type == "SceneRemoved" || type == "SceneNameChanged" || type == "SceneListChanged" ||
                                type == "InputCreated" || type == "InputRemoved" || type == "InputNameChanged")
                                RefreshListsSoon();
                            break;
                        }
                    case 7:   // RequestResponse
                        {
                            Pending p;
                            string rid = Str(d, "requestId");
                            lock (pending) { if (pending.TryGetValue(rid, out p)) pending.Remove(rid); }
                            if (p == null) break;
                            Dictionary<string, object> status = Obj(d, "requestStatus");
                            if (status != null && !(status.ContainsKey("result") && status["result"] is bool && (bool)status["result"]))
                            {
                                string comment = Str(status, "comment");
                                p.Error = comment.Length > 0 ? comment : L.F("OBS error {0}", Int(status, "code"));
                            }
                            p.Data = Obj(d, "responseData") ?? new Dictionary<string, object>();
                            p.Done.Set();
                            break;
                        }
                }
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

    static string Auth(string password, string salt, string challenge)
    {
        using (SHA256 sha = SHA256.Create())
        {
            string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
        }
    }

    static string Envelope(int op, Dictionary<string, object> d)
    {
        Dictionary<string, object> m = new Dictionary<string, object>();
        m["op"] = op;
        m["d"] = d;
        return new JavaScriptSerializer().Serialize(m);
    }

    void Send(ClientWebSocket socket, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        lock (sendGate)   // one send at a time
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000);
    }

    void FailAll(string why)
    {
        lock (pending)
        {
            foreach (Pending p in pending.Values) { p.Error = why; p.Done.Set(); }
            pending.Clear();
        }
    }

    // Sends one request and waits for OBS's answer. Never call it on the UI thread or the receive thread.
    public Dictionary<string, object> Request(string type, Dictionary<string, object> data)
    {
        ClientWebSocket socket;
        lock (gate) socket = ws;
        if (socket == null || !Connected) throw new InvalidOperationException(L.T("OBS isn't connected"));
        Pending p = new Pending();
        string rid = "ck" + Interlocked.Increment(ref nextId);
        lock (pending) pending[rid] = p;
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["requestType"] = type;
        d["requestId"] = rid;
        if (data != null) d["requestData"] = data;
        Diag.Count("obs.requests");
        try { Send(socket, Envelope(6, d)); }
        catch (Exception ex)
        {
            lock (pending) pending.Remove(rid);
            throw new InvalidOperationException(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
        }
        if (!p.Done.WaitOne(5000))
        {
            lock (pending) pending.Remove(rid);
            throw new InvalidOperationException(L.T("OBS didn't answer"));
        }
        if (p.Error != null)
        {
            Diag.Count("obs.failed");
            Diag.Write("obs", type + " failed: " + p.Error);
            throw new InvalidOperationException(p.Error);
        }
        return p.Data;
    }

    static Dictionary<string, object> Args(params object[] kv)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    void AfterConnect()
    {
        try
        {
            Dictionary<string, object> v = Request("GetVersion", null);
            Version = Str(v, "obsVersion");
            if (Version.Length > 0 && Connected) SetStatus(State.Connected, L.F("Connected to OBS {0}.", Version));
        }
        catch (Exception ex) { Diag.Write("obs", "GetVersion: " + ex.Message); }
        RefreshLists();
    }

    void RefreshListsSoon()
    {
        if (Interlocked.Exchange(ref listsBusy, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            Thread.Sleep(300);   // renaming fires several events; fetch once
            Interlocked.Exchange(ref listsBusy, 0);
            RefreshLists();
        });
    }

    // The scene and source names, for the drop-downs in the actions.
    void RefreshLists()
    {
        try
        {
            List<string> scenes = new List<string>();
            foreach (object o in List(Request("GetSceneList", null), "scenes"))
            {
                Dictionary<string, object> s = o as Dictionary<string, object>;
                if (s != null && Str(s, "sceneName").Length > 0) scenes.Add(Str(s, "sceneName"));
            }
            scenes.Reverse();   // OBS lists the bottom scene first
            List<string> sources = new List<string>(), audio = new List<string>();
            foreach (object o in List(Request("GetInputList", null), "inputs"))
            {
                Dictionary<string, object> s = o as Dictionary<string, object>;
                if (s == null || Str(s, "inputName").Length == 0) continue;
                sources.Add(Str(s, "inputName"));
                string kind = Str(s, "inputKind").ToLowerInvariant();
                if (kind.Contains("audio") || kind.Contains("wasapi") || kind.Contains("ffmpeg") || kind.Contains("vlc") || kind.Contains("dshow"))
                    audio.Add(Str(s, "inputName"));
            }
            sources.Sort(StringComparer.OrdinalIgnoreCase);
            audio.Sort(StringComparer.OrdinalIgnoreCase);
            Scenes = scenes.ToArray();
            Sources = sources.ToArray();
            Audio = audio.ToArray();
            Diag.Note("obs.lists", Scenes.Length + " scenes, " + Sources.Length + " sources");
            app.Ui(delegate { if (ListsChanged != null) ListsChanged(this, EventArgs.Empty); });
        }
        catch (Exception ex) { Diag.Write("obs", "lists: " + ex.Message); }
    }

    // ---------------- actions ----------------

    // Runs an OBS action on a worker thread (OBS answers take a moment). source: for the log.
    public void Run(GiftRule r, string source)
    {
        GiftRule rule = r.Clone();
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                Do(rule);
                Diag.Count("obs.actions");
                app.Log(L.F("Did: {0}", rule.ActionText) + " (" + source + ")");
            }
            catch (Exception ex)
            {
                Diag.Count("obs.actions.failed");
                app.Log(L.F("Failed: {0} ({1})", rule.ActionText, ex.Message));
            }
        });
    }

    void Do(GiftRule r)
    {
        int secs = Math.Max(0, r.ObsSecs);
        string target = r.ObsTarget.Trim();
        switch (r.ObsDo)
        {
            case GiftRule.ObsScene:
                {
                    string before = Str(Request("GetCurrentProgramScene", null), "currentProgramSceneName");
                    Request("SetCurrentProgramScene", Args("sceneName", target));
                    if (secs > 0)
                        Later("scene", secs, before, delegate (object back)
                        {
                            // only switch back if nobody changed the scene in the meantime
                            string now = Str(Request("GetCurrentProgramScene", null), "currentProgramSceneName");
                            if (now == target && back is string && ((string)back).Length > 0) Request("SetCurrentProgramScene", Args("sceneName", back));
                        });
                    break;
                }
            case GiftRule.ObsShow:
            case GiftRule.ObsHide:
                {
                    bool show = r.ObsDo == GiftRule.ObsShow;
                    string scene = r.ObsInScene.Trim();
                    if (scene.Length == 0) scene = Str(Request("GetCurrentProgramScene", null), "currentProgramSceneName");
                    object idObj;
                    Request("GetSceneItemId", Args("sceneName", scene, "sourceName", target)).TryGetValue("sceneItemId", out idObj);
                    int id = Convert.ToInt32(idObj);
                    object wasObj;
                    Request("GetSceneItemEnabled", Args("sceneName", scene, "sceneItemId", id)).TryGetValue("sceneItemEnabled", out wasObj);
                    bool was = wasObj is bool && (bool)wasObj;
                    if (show && was && secs > 0)
                    {
                        // already showing: hide and show again, so a video or GIF starts over
                        Request("SetSceneItemEnabled", Args("sceneName", scene, "sceneItemId", id, "sceneItemEnabled", false));
                        Thread.Sleep(100);
                    }
                    Request("SetSceneItemEnabled", Args("sceneName", scene, "sceneItemId", id, "sceneItemEnabled", show));
                    if (secs > 0)
                        Later("item|" + scene + "|" + id, secs, was, delegate (object back)
                        {
                            Request("SetSceneItemEnabled", Args("sceneName", scene, "sceneItemId", id, "sceneItemEnabled", (bool)back));
                        });
                    break;
                }
            case GiftRule.ObsMute:
            case GiftRule.ObsUnmute:
                {
                    bool mute = r.ObsDo == GiftRule.ObsMute;
                    object wasObj;
                    Request("GetInputMute", Args("inputName", target)).TryGetValue("inputMuted", out wasObj);
                    bool was = wasObj is bool && (bool)wasObj;
                    Request("SetInputMute", Args("inputName", target, "inputMuted", mute));
                    if (secs > 0)
                        Later("mute|" + target, secs, was, delegate (object back)
                        {
                            Request("SetInputMute", Args("inputName", target, "inputMuted", (bool)back));
                        });
                    break;
                }
            case GiftRule.ObsReplay:
                Request("SaveReplayBuffer", null);
                break;
            case GiftRule.ObsRecStart:
                Request("StartRecord", null);
                break;
            case GiftRule.ObsRecStop:
                Request("StopRecord", null);
                break;
            default:
                throw new InvalidOperationException(r.ObsDo);
        }
    }

    // Undoes something after secs seconds. Triggered again before that: the timer starts over and
    // the state from before the first trigger is kept (so it goes back to how it really was).
    void Later(string key, int secs, object before, Action<object> undo)
    {
        lock (reverts)
        {
            Revert old;
            if (reverts.TryGetValue(key, out old))
            {
                old.Timer.Dispose();
                before = old.Before;
            }
            Revert rv = new Revert();
            rv.Before = before;
            rv.Undo = undo;
            rv.Timer = new Timer(delegate
            {
                lock (reverts)
                {
                    Revert cur;
                    if (!reverts.TryGetValue(key, out cur) || cur != rv) return;
                    reverts.Remove(key);
                    rv.Timer.Dispose();
                }
                try { rv.Undo(rv.Before); }
                catch (Exception ex) { app.Log(L.F("OBS couldn't undo it: {0}", ex.Message)); }
            }, null, secs * 1000L, Timeout.Infinite);
            reverts[key] = rv;
        }
    }

    // ---------------- JSON helpers ----------------

    static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
    {
        object o;
        return d != null && d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null;
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object o;
        return d != null && d.TryGetValue(key, out o) && o != null ? o.ToString() : "";
    }

    static int Int(Dictionary<string, object> d, string key)
    {
        object o;
        if (d == null || !d.TryGetValue(key, out o) || o == null) return -1;
        try { return Convert.ToInt32(o); } catch { return -1; }
    }

    static IEnumerable List(Dictionary<string, object> d, string key)
    {
        object o;
        if (d != null && d.TryGetValue(key, out o) && o is IEnumerable && !(o is string)) return (IEnumerable)o;
        return new object[0];
    }
}
