// ClutchKit - the "Gift actions" and "TikTok LIVE" pages of the settings window.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public partial class MainForm
{
    static readonly string[] CommonGifts = { "Rose", "TikTok", "GG", "Finger Heart", "Heart Me", "Ice Cream Cone", "Doughnut", "Perfume", "Hand Hearts" };

    Panel rulesHost;
    Label queueLbl, elevLbl, actionsMsg, ttStatus;
    Button elevBtn, pauseKeyBtn, connectBtn;
    CheckBox actionsOnBox;
    readonly List<ListBox> logBoxes = new List<ListBox>();
    readonly List<Timer> testTimers = new List<Timer>();

    // key capture for gift action keys and the on/off key
    Button keyCapBtn;
    Action<Hotkey> keyCapSet;
    Func<Hotkey> keyCapGet;
    bool keyCapIsPauseKey;

    // ---------------- Gift actions ----------------

    Panel BuildActionsPage()
    {
        Panel p = NewPage("Gift actions", "When a viewer sends a gift, ClutchKit presses keys in your game - or types text, like a Minecraft command. " +
                                          "Gifts come in through the TikTok LIVE page.");
        Settings s = app.S;

        actionsOnBox = UI.Check(p, "Gift actions on", 38, 124, s.ActionsOn);
        actionsOnBox.Font = UI.F(11f, FontStyle.Bold);
        actionsOnBox.CheckedChanged += delegate { if (!loading) app.SetActionsOn(actionsOnBox.Checked); };
        UI.Lbl(p, "On/off key", 250, 128, 100, 10f, UI.Muted, FontStyle.Regular);
        pauseKeyBtn = UI.Btn(p, s.PauseKey.ToString(), 350, 120, 160, 36, false);
        pauseKeyBtn.BackColor = UI.Input;
        pauseKeyBtn.Click += delegate
        {
            StartKeyCapture(pauseKeyBtn, delegate { return app.S.PauseKey; }, delegate (Hotkey hk) { app.S.PauseKey = hk; }, true);
        };
        UI.Para(p, "Turn gift actions off fast, e.g. when you're in a menu.", 520, 118, 150, 44);

        UI.FieldLabel(p, "Only press keys in", 38, 170);
        ComboBox only = UI.Combo(p, 190, 170, 220);
        only.DropDownStyle = ComboBoxStyle.DropDown;
        only.Text = s.OnlyProcess;
        only.DropDown += delegate { FillPrograms(only); };
        only.TextChanged += delegate { app.S.OnlyProcess = KeySender.CleanProcessName(only.Text); Changed(); };
        UI.Para(p, "Pick your game, so gifts never type into chat or Discord. Empty = whatever is in front.", 420, 166, 250, 44);

        queueLbl = UI.Para(p, "", 38, 222, 200, 24);
        Button clear = UI.Btn(p, "Clear queue", 240, 216, 130, 32, false);
        clear.Click += delegate { app.Engine.Clear(); app.Log("Queue cleared."); };
        UI.Lbl(p, "Wait between actions (ms)", 390, 222, 190, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown gap = UI.Number(p, 580, 216, 0, 5000, s.GapMs, 10);
        gap.Size = new Size(UI.D(80), UI.D(30));
        gap.ValueChanged += delegate { app.S.GapMs = (int)gap.Value; Changed(); };

        elevLbl = UI.Para(p, "", 38, 258, 420, 44);
        elevLbl.ForeColor = UI.Warn;
        elevBtn = UI.Btn(p, "Restart as administrator", 470, 262, 200, 36, false);
        elevBtn.Click += delegate { app.RestartAsAdmin(); };
        ShowElevation(null);

        // test gift
        UI.Lbl(p, "Test with a fake gift", 38, 312, 300, 11f, UI.Text, FontStyle.Bold);
        ComboBox testGift = GiftCombo(p, 38, 344, 180);
        testGift.Text = "Rose";
        UI.Lbl(p, "x", 226, 348, 16, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown testCount = UI.Number(p, 244, 344, 1, 999, 1, 1);
        testCount.Size = new Size(UI.D(70), UI.D(30));
        UI.Lbl(p, "coins each", 322, 348, 80, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown testCoins = UI.Number(p, 404, 344, 0, 100000, 1, 1);
        testCoins.Size = new Size(UI.D(80), UI.D(30));
        Button sendTest = UI.Btn(p, "Send in 3 s", 500, 340, 170, 38, true);
        sendTest.Click += delegate
        {
            string name = testGift.Text.Trim();
            int count = (int)testCount.Value, coins = (int)testCoins.Value;
            Countdown(sendTest, "Send in 3 s", delegate
            {
                GiftEvent g = new GiftEvent();
                g.Test = true;
                g.User = "test_viewer";
                g.GiftName = name;
                g.RepeatCount = count;
                g.Coins = coins;
                app.Engine.OnGift(g);
            });
        };
        UI.Para(p, "Acts exactly like a real gift. Click into your game during the countdown.", 38, 382, 620, 24);

        // rules
        UI.Lbl(p, "Your gift actions", 38, 420, 300, 11f, UI.Text, FontStyle.Bold);
        actionsMsg = UI.Para(p, "Click a key button, then press the key (Esc = cancel).", 38, 448, 620, 24);
        rulesHost = new Panel();
        rulesHost.BackColor = UI.Bg;
        rulesHost.Location = new Point(UI.D(38), UI.D(478));
        rulesHost.Width = UI.D(632);
        p.Controls.Add(rulesHost);
        RebuildRules();

        app.Engine.QueueChanged += OnQueue;
        app.Engine.ElevationProblem += OnElevation;
        app.ActionsToggled += OnActionsToggled;
        app.Logged += OnLogged;
        OnQueue(null, EventArgs.Empty);
        return p;
    }

    void RebuildRules()
    {
        CancelKeyCapture();
        rulesHost.SuspendLayout();
        while (rulesHost.Controls.Count > 0) rulesHost.Controls[0].Dispose();
        int y = 0;
        foreach (GiftRule r in app.S.Rules)
        {
            Panel card = BuildRuleCard(r);
            card.Location = new Point(0, y);
            rulesHost.Controls.Add(card);
            y += card.Height + UI.D(12);
        }
        if (app.S.Rules.Count < Settings.MaxRules)
        {
            Button add = UI.Btn(rulesHost, "+  Add gift action", 0, 0, 220, 40, true);
            add.Location = new Point(0, y);
            add.Click += delegate
            {
                GiftRule r = app.AddRule();
                RebuildRules();
                if (r != null) ActionsMessage("Added. Type the gift's name and pick the key it should press.", UI.Good);
            };
            y += add.Height + UI.D(16);
        }
        Label logTitle = UI.Lbl(rulesHost, "Activity", 0, 0, 300, 11f, UI.Text, FontStyle.Bold);
        logTitle.Location = new Point(0, y);
        y += UI.D(30);
        ListBox log = MakeLog(rulesHost, 0, 0, 632, 200);
        log.Location = new Point(0, y);
        y += log.Height + UI.D(24);
        rulesHost.Height = y;
        rulesHost.ResumeLayout();
    }

    Panel BuildRuleCard(GiftRule r)
    {
        Panel card = new Panel();
        card.BackColor = UI.Card;
        card.Size = new Size(UI.D(632), UI.D(156));

        // row 1: which gift
        CheckBox on = UI.Check(card, "On", 16, 18, r.Enabled);
        on.BackColor = UI.Card;
        on.CheckedChanged += delegate { r.Enabled = on.Checked; Changed(); };
        UI.Lbl(card, "Gift", 76, 18, 40, 10f, UI.Muted, FontStyle.Regular);
        ComboBox gift = GiftCombo(card, 116, 14, 220);
        gift.Text = r.Gifts;
        gift.TextChanged += delegate { r.Gifts = gift.Text.Trim(); Changed(); };
        UI.Lbl(card, "min. coins", 346, 18, 80, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown coins = UI.Number(card, 426, 14, 0, 1000000, r.MinCoins, 1);
        coins.Size = new Size(UI.D(80), UI.D(30));
        coins.ValueChanged += delegate { r.MinCoins = (int)coins.Value; Changed(); };
        Button remove = UI.Btn(card, "Remove", 528, 12, 88, 34, false);
        remove.Click += delegate
        {
            if (MessageBox.Show(this, "Remove the gift action for \"" + r.TriggerText + "\"?", "ClutchKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.RemoveRule(r);
            RebuildRules();
        };

        // row 2: what it does
        ComboBox kind = UI.Combo(card, 16, 60, 140);
        kind.Items.AddRange(GiftRule.KindNames);
        kind.SelectedIndex = r.Kind == GiftRule.KindText ? 1 : 0;
        Button key = UI.Btn(card, "", 166, 58, 160, 34, false);
        key.BackColor = UI.Input;
        Label holdLbl = UI.Lbl(card, "hold (ms)", 340, 64, 80, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown hold = UI.Number(card, 426, 60, 10, 10000, r.HoldMs, 10);
        hold.Size = new Size(UI.D(80), UI.D(30));
        hold.ValueChanged += delegate { r.HoldMs = (int)hold.Value; Changed(); };
        TextBox text = new TextBox();
        UI.Style(text);
        text.BorderStyle = BorderStyle.FixedSingle;
        text.Text = r.Text;
        text.Location = new Point(UI.D(336), UI.D(62));
        text.Size = new Size(UI.D(196), UI.D(28));
        text.TextChanged += delegate { r.Text = text.Text; Changed(); };
        card.Controls.Add(text);
        CheckBox enter = UI.Check(card, "+ Enter", 540, 64, r.Enter);
        enter.BackColor = UI.Card;
        enter.CheckedChanged += delegate { r.Enter = enter.Checked; Changed(); };

        MethodInvoker layout = delegate
        {
            bool isText = r.Kind == GiftRule.KindText;
            holdLbl.Visible = hold.Visible = !isText;
            text.Visible = enter.Visible = isText;
            if (key != keyCapBtn) key.Text = isText ? (r.Key.IsSet ? "First: " + r.Key : "First: no key") : r.Key.ToString();
            key.ForeColor = r.Key.IsSet ? UI.Text : UI.Muted;
        };
        kind.SelectedIndexChanged += delegate
        {
            r.Kind = kind.SelectedIndex == 1 ? GiftRule.KindText : GiftRule.KindKey;
            layout();
            Changed();
        };
        key.Click += delegate
        {
            StartKeyCapture(key, delegate { return r.Key; }, delegate (Hotkey hk) { r.Key = hk; layout(); }, false);
            if (keyCapBtn == key)
                ActionsMessage(r.Kind == GiftRule.KindText
                    ? "Press the key that opens the chat first (e.g. T in Minecraft), or Backspace for none. Esc = cancel."
                    : "Press the key the gift should press in your game (Backspace = no key, Esc = cancel).", UI.Warn);
        };
        layout();

        // row 3: how often, test
        UI.Lbl(card, "Times", 16, 110, 50, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown times = UI.Number(card, 70, 106, 1, 100, r.Times, 1);
        times.Size = new Size(UI.D(70), UI.D(30));
        times.ValueChanged += delegate { r.Times = (int)times.Value; Changed(); };
        CheckBox perGift = UI.Check(card, "For every gift in a combo (x10 = 10 times)", 156, 110, r.PerGift);
        perGift.BackColor = UI.Card;
        perGift.CheckedChanged += delegate { r.PerGift = perGift.Checked; Changed(); };
        Button test = UI.Btn(card, "Test in 3 s", 480, 104, 136, 36, false);
        test.Click += delegate
        {
            if (!r.CanRun) { ActionsMessage(r.Kind == GiftRule.KindText ? "Type the text first." : "Pick a key first.", UI.Warn); return; }
            Countdown(test, "Test in 3 s", delegate { app.Engine.RunTest(r); });
            ActionsMessage("Click into your game now - it runs in 3 seconds.", UI.Warn);
        };
        return card;
    }

    ComboBox GiftCombo(Control parent, int x, int y, int w)
    {
        ComboBox c = UI.Combo(parent, x, y, w);
        c.DropDownStyle = ComboBoxStyle.DropDown;
        c.Items.AddRange(CommonGifts);
        return c;
    }

    void FillPrograms(ComboBox c)
    {
        string current = c.Text;
        SortedDictionary<string, bool> names = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        int self = Process.GetCurrentProcess().Id;
        foreach (Process pr in Process.GetProcesses())
        {
            try
            {
                if (pr.Id != self && pr.MainWindowHandle != IntPtr.Zero && pr.MainWindowTitle.Length > 0) names[pr.ProcessName] = true;
            }
            catch { }
            finally { pr.Dispose(); }
        }
        c.BeginUpdate();
        c.Items.Clear();
        foreach (string n in names.Keys) c.Items.Add(n);
        c.EndUpdate();
        c.Text = current;
    }

    // Runs action after a 3-2-1 countdown on the button, so you have time to click into the game.
    void Countdown(Button b, string label, MethodInvoker action)
    {
        if (!b.Enabled) return;
        int left = 3;
        b.Enabled = false;
        b.Text = left + "...";
        Timer t = new Timer();
        t.Interval = 1000;
        testTimers.Add(t);
        t.Tick += delegate
        {
            left--;
            if (left > 0) { b.Text = left + "..."; return; }
            t.Stop();
            testTimers.Remove(t);
            t.Dispose();
            if (b.IsDisposed) return;
            b.Text = label;
            b.Enabled = true;
            action();
        };
        t.Start();
    }

    void ActionsMessage(string text, Color color) { actionsMsg.Text = text; actionsMsg.ForeColor = color; }

    void OnQueue(object sender, EventArgs e)
    {
        int n = app.Engine.Waiting;
        queueLbl.Text = n == 0 ? "Queue: empty" : "Queue: " + n + " waiting";
        queueLbl.ForeColor = n == 0 ? UI.Muted : UI.Warn;
    }

    void OnActionsToggled(object sender, EventArgs e)
    {
        loading = true;
        actionsOnBox.Checked = app.S.ActionsOn;
        loading = false;
    }

    void OnElevation(string program) { ShowElevation(program); }

    void ShowElevation(string program)
    {
        bool show = program != null && !KeySender.SelfElevated;
        elevLbl.Text = show ? program + " runs as administrator, so Windows blocks keys from ClutchKit. Restart ClutchKit as administrator to fix it." : "";
        elevLbl.Visible = elevBtn.Visible = show;
    }

    // ---------------- key capture (gift actions) ----------------

    void StartKeyCapture(Button b, Func<Hotkey> get, Action<Hotkey> set, bool isPauseKey)
    {
        if (keyCapBtn == b) { CancelKeyCapture(); return; }
        CancelCapture();
        keyCapBtn = b;
        keyCapGet = get;
        keyCapSet = set;
        keyCapIsPauseKey = isPauseKey;
        app.CapturingHotkey = true;
        b.Text = "Press a key...";
        b.ForeColor = UI.Warn;
        ActiveControl = null;
        Focus();
    }

    void CancelKeyCapture()
    {
        if (keyCapBtn == null) return;
        Button b = keyCapBtn;
        Hotkey current = keyCapGet();
        keyCapBtn = null;
        app.CapturingHotkey = false;
        if (b.IsDisposed) return;
        b.Text = current.ToString();
        b.ForeColor = current.IsSet ? UI.Text : UI.Muted;
        keyCapSet(current);   // lets the card redraw its button text
    }

    bool KeyCaptureKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin ||
            key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
            key == Keys.LMenu || key == Keys.RMenu)
            return true;   // wait for the main key
        int mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Hotkey.Ctrl;
        if ((keyData & Keys.Alt) != 0) mods |= Hotkey.Alt;
        if ((keyData & Keys.Shift) != 0) mods |= Hotkey.Shift;
        if (IsWinDown()) mods |= Hotkey.Win;

        if (key == Keys.Escape && mods == 0) { CancelKeyCapture(); ActionsMessage("Cancelled - nothing changed.", UI.Muted); return true; }
        Hotkey hk = key == Keys.Back && mods == 0 ? new Hotkey(0, 0) : new Hotkey(mods, (int)key);
        if (keyCapIsPauseKey && hk.IsSet)
        {
            string owner = app.KeyOwner(hk, null, -2);
            if (owner != null) { ActionsMessage(hk + " is already used for \"" + owner + "\". Press a different key.", UI.Warn); return true; }
        }
        keyCapSet(hk);
        CancelKeyCapture();
        app.ApplySettings();
        ActionsMessage(hk.IsSet ? "Saved: " + hk + "." : "No key now.", hk.IsSet ? UI.Good : UI.Muted);
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    static extern short AsyncKeyState(int vk);

    static bool IsWinDown() { return (AsyncKeyState(0x5B) & 0x8000) != 0 || (AsyncKeyState(0x5C) & 0x8000) != 0; }

    // ---------------- TikTok LIVE ----------------

    Panel BuildTikTokPage()
    {
        Panel p = NewPage("TikTok LIVE", "Connect to your LIVE so gifts can trigger your gift actions. TikTok has no official way to do this, " +
                                         "so ClutchKit uses Euler Stream (free account) to read the gifts.");
        Settings s = app.S;

        UI.FieldLabel(p, "Your TikTok username", 38, 126);
        TextBox user = new TextBox();
        UI.Style(user);
        user.BorderStyle = BorderStyle.FixedSingle;
        user.Text = s.TikTokUser;
        user.Location = new Point(UI.D(230), UI.D(126));
        user.Size = new Size(UI.D(260), UI.D(28));
        user.TextChanged += delegate { app.S.TikTokUser = TikTokLive.CleanUser(user.Text); Changed(); };
        p.Controls.Add(user);

        UI.FieldLabel(p, "Euler Stream API key", 38, 166);
        TextBox key = new TextBox();
        UI.Style(key);
        key.BorderStyle = BorderStyle.FixedSingle;
        key.UseSystemPasswordChar = true;
        key.Text = s.TikTokKey;
        key.Location = new Point(UI.D(230), UI.D(166));
        key.Size = new Size(UI.D(260), UI.D(28));
        key.TextChanged += delegate { app.S.TikTokKey = key.Text.Trim(); Changed(); };
        p.Controls.Add(key);
        LinkLabel signup = new LinkLabel();
        signup.Text = "Get a free key";
        signup.Font = UI.F(10f, FontStyle.Regular);
        signup.LinkColor = UI.Accent;
        signup.ActiveLinkColor = UI.Text;
        signup.Location = new Point(UI.D(502), UI.D(170));
        signup.AutoSize = true;
        signup.LinkClicked += delegate { try { Process.Start(TikTokLive.SignupUrl); } catch { } };
        p.Controls.Add(signup);

        CheckBox auto = UI.Check(p, "Connect automatically when ClutchKit starts", 38, 210, s.AutoConnect);
        auto.CheckedChanged += delegate { app.S.AutoConnect = auto.Checked; Changed(); };

        connectBtn = UI.Btn(p, "Connect", 38, 250, 180, 40, true);
        connectBtn.Click += delegate
        {
            if (app.TikTok.Running) app.TikTok.Stop();
            else app.TikTok.Start(app.S.TikTokUser, app.S.TikTokKey);
        };
        ttStatus = UI.Para(p, "", 232, 250, 440, 44);

        UI.Para(p, "Your username and key are only sent to Euler Stream, and only while connected. The key is saved in settings.ini on this PC.",
                38, 304, 630, 40);
        UI.Lbl(p, "Activity", 38, 350, 300, 11f, UI.Text, FontStyle.Bold);
        MakeLog(p, 38, 380, 632, 240);

        app.TikTok.StatusChanged += OnTikTokStatus;
        OnTikTokStatus(null, EventArgs.Empty);
        return p;
    }

    void OnTikTokStatus(object sender, EventArgs e)
    {
        TikTokLive t = app.TikTok;
        connectBtn.Text = t.Running ? "Disconnect" : "Connect";
        ttStatus.Text = t.Detail;
        switch (t.Current)
        {
            case TikTokLive.State.Connected: ttStatus.ForeColor = UI.Good; break;
            case TikTokLive.State.Error: ttStatus.ForeColor = UI.Accent; break;
            case TikTokLive.State.Off: ttStatus.ForeColor = UI.Muted; break;
            default: ttStatus.ForeColor = UI.Warn; break;
        }
    }

    // ---------------- activity log ----------------

    ListBox MakeLog(Control parent, int x, int y, int w, int h)
    {
        ListBox l = new ListBox();
        l.BackColor = UI.Input;
        l.ForeColor = UI.Text;
        l.BorderStyle = BorderStyle.None;
        l.Font = UI.F(9f, FontStyle.Regular);
        l.IntegralHeight = false;
        l.HorizontalScrollbar = true;
        l.Location = new Point(UI.D(x), UI.D(y));
        l.Size = new Size(UI.D(w), UI.D(h));
        foreach (string line in app.LogLines) l.Items.Add(line);
        if (l.Items.Count > 0) l.TopIndex = l.Items.Count - 1;
        parent.Controls.Add(l);
        logBoxes.RemoveAll(delegate (ListBox o) { return o.IsDisposed; });
        logBoxes.Add(l);
        l.Disposed += delegate { logBoxes.Remove(l); };
        return l;
    }

    void OnLogged(string line)
    {
        foreach (ListBox l in logBoxes.ToArray())
        {
            if (l.IsDisposed) continue;
            l.Items.Add(line);
            while (l.Items.Count > 300) l.Items.RemoveAt(0);
            l.TopIndex = l.Items.Count - 1;
        }
    }

    void UnhookActionPages()
    {
        app.Engine.QueueChanged -= OnQueue;
        app.Engine.ElevationProblem -= OnElevation;
        app.ActionsToggled -= OnActionsToggled;
        app.Logged -= OnLogged;
        app.TikTok.StatusChanged -= OnTikTokStatus;
        foreach (Timer t in testTimers) t.Dispose();
        testTimers.Clear();
    }
}
