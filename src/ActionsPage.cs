// ClutchKit - the "Actions" and "TikTok LIVE" pages of the settings window.
// Actions are sorted into topics: collapsible boxes that can be switched on/off as a whole.
// Each action can be folded to one line; a search box filters the list; actions can be duplicated.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public partial class MainForm
{
    static readonly string[] CommonGifts = { "Rose", "TikTok", "GG", "Finger Heart", "Heart Me", "Ice Cream Cone", "Doughnut", "Perfume", "Hand Hearts" };
    static readonly Color GroupBar = Color.FromArgb(38, 42, 51);

    Panel actionsPage, rulesHost;
    Label queueLbl, elevLbl, actionsMsg, ttStatus;
    Button elevBtn, pauseKeyBtn, connectBtn;
    CheckBox actionsOnBox;
    TextBox searchBox;
    Timer searchTimer;
    int scrollToRule = -1;
    readonly List<ListBox> logBoxes = new List<ListBox>();
    readonly List<Timer> testTimers = new List<Timer>();

    // key capture for action keys and the on/off key
    Button keyCapBtn;
    Action<Hotkey> keyCapSet;
    Func<Hotkey> keyCapGet;
    bool keyCapIsPauseKey;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

    // Grey hint text inside an empty text box.
    static void Hint(TextBox box, string text)
    {
        if (box.IsHandleCreated) SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, text);
        else box.HandleCreated += delegate { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); };
    }

    // ---------------- Actions ----------------

    Panel BuildActionsPage()
    {
        Panel p = NewPage(L.T("Actions"), L.T("When something happens on your LIVE - a gift, a follow, a share, a viewer joining, likes - ClutchKit can play a sound, press keys in your game or type text (like a Minecraft command)."));
        actionsPage = p;
        Settings s = app.S;

        actionsOnBox = UI.Check(p, L.T("Actions on"), 38, 124, s.ActionsOn);
        actionsOnBox.Font = UI.F(11f, FontStyle.Bold);
        actionsOnBox.CheckedChanged += delegate { if (!loading) app.SetActionsOn(actionsOnBox.Checked); };
        UI.Lbl(p, L.T("On/off key"), 250, 128, 100, 10f, UI.Muted, FontStyle.Regular);
        pauseKeyBtn = UI.Btn(p, s.PauseKey.ToString(), 350, 120, 160, 36, false);
        pauseKeyBtn.BackColor = UI.Input;
        pauseKeyBtn.Click += delegate
        {
            StartKeyCapture(pauseKeyBtn, delegate { return app.S.PauseKey; }, delegate (Hotkey hk) { app.S.PauseKey = hk; }, true);
        };
        UI.Para(p, L.T("Turn actions off fast, e.g. when you're in a menu."), 520, 118, 150, 44);

        UI.FieldLabel(p, L.T("Only press keys in"), 38, 170);
        ComboBox only = UI.Combo(p, 190, 170, 220);
        only.DropDownStyle = ComboBoxStyle.DropDown;
        only.Text = s.OnlyProcess;
        only.DropDown += delegate { FillPrograms(only); };
        only.TextChanged += delegate { app.S.OnlyProcess = KeySender.CleanProcessName(only.Text); Changed(); };
        UI.Para(p, L.T("Pick your game, so keys never go into chat or Discord. Empty = whatever is in front. (Sounds always play.)"), 420, 166, 250, 60);

        queueLbl = UI.Para(p, "", 38, 230, 200, 24);
        Button clear = UI.Btn(p, L.T("Stop and clear"), 240, 224, 146, 32, false);
        clear.Click += delegate { app.Engine.Clear(); app.Log(L.T("Stopped the sound and cleared the queue.")); };
        UI.Lbl(p, L.T("Wait between key actions (ms)"), 390, 230, 190, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown gap = UI.Number(p, 590, 224, 0, 5000, s.GapMs, 10);
        gap.Size = new Size(UI.D(80), UI.D(30));
        gap.ValueChanged += delegate { app.S.GapMs = (int)gap.Value; Changed(); };

        elevLbl = UI.Para(p, "", 38, 266, 420, 44);
        elevLbl.ForeColor = UI.Warn;
        elevBtn = UI.Btn(p, L.T("Restart as administrator"), 462, 270, 208, 36, false);
        elevBtn.Click += delegate { app.RestartAsAdmin(); };
        ShowElevation(null);

        // test event
        UI.Lbl(p, L.T("Test with a fake event"), 38, 316, 300, 11f, UI.Text, FontStyle.Bold);
        ComboBox testType = UI.Combo(p, 38, 348, 120);
        testType.Items.AddRange(L.All(GiftRule.TriggerNames));
        testType.SelectedIndex = 0;
        ComboBox testGift = GiftCombo(p, 166, 348, 140);
        testGift.Text = "Rose";
        Label testX = UI.Lbl(p, "x", 312, 352, 16, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown testCount = UI.Number(p, 330, 348, 1, 100000, 1, 1);
        testCount.Size = new Size(UI.D(64), UI.D(30));
        Label testCoinsLbl = UI.Lbl(p, L.T("coins"), 400, 352, 48, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown testCoins = UI.Number(p, 450, 348, 0, 100000, 1, 1);
        testCoins.Size = new Size(UI.D(70), UI.D(30));
        MethodInvoker testLayout = delegate
        {
            string type = GiftRule.Triggers[testType.SelectedIndex];
            bool gift = type == LiveEvent.Gift, likes = type == LiveEvent.Likes;
            testGift.Visible = testCoinsLbl.Visible = testCoins.Visible = gift;
            testX.Visible = testCount.Visible = gift || likes;
            if (likes && testCount.Value < 10) testCount.Value = 100;
        };
        testType.SelectedIndexChanged += delegate { testLayout(); };
        testLayout();
        Button sendTest = UI.Btn(p, L.T("Send in 3 s"), 530, 344, 140, 38, true);
        sendTest.Click += delegate
        {
            LiveEvent e = new LiveEvent();
            e.Test = true;
            e.Type = GiftRule.Triggers[testType.SelectedIndex];
            e.User = "test_viewer";
            e.GiftName = testGift.Text.Trim();
            e.RepeatCount = (int)testCount.Value;
            e.Coins = (int)testCoins.Value;
            Countdown(sendTest, L.T("Send in 3 s"), delegate { app.Engine.OnEvent(e); });
        };
        UI.Para(p, L.T("Acts exactly like the real thing. Click into your game during the countdown."), 38, 386, 620, 24);

        UI.Lbl(p, L.T("Activity"), 38, 420, 300, 11f, UI.Text, FontStyle.Bold);
        MakeLog(p, 38, 448, 632, 110);

        // the list: search, topics, actions
        UI.Lbl(p, L.T("Your actions"), 38, 576, 300, 11f, UI.Text, FontStyle.Bold);
        searchBox = new TextBox();
        UI.Style(searchBox);
        searchBox.BorderStyle = BorderStyle.FixedSingle;
        searchBox.Location = new Point(UI.D(38), UI.D(608));
        searchBox.Size = new Size(UI.D(240), UI.D(28));
        Hint(searchBox, L.T("Search actions..."));
        p.Controls.Add(searchBox);
        searchTimer = new Timer();
        searchTimer.Interval = 250;
        searchTimer.Tick += delegate { searchTimer.Stop(); RebuildRules(); };
        searchBox.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
        searchBox.KeyDown += delegate (object o, KeyEventArgs k) { if (k.KeyCode == Keys.Escape) { searchBox.Text = ""; k.SuppressKeyPress = true; } };
        Button newTopic = UI.Btn(p, L.T("+ New topic"), 288, 604, 140, 34, true);
        newTopic.Click += delegate
        {
            string name = AskText(L.T("New topic"), L.T("Name of the topic (e.g. Minecraft, Sounds, Big gifts):"), "");
            if (name == null) return;
            RuleGroup g = app.AddGroup(name);
            if (g == null) { ActionsMessage(L.F("That's the maximum of {0} topics.", Settings.MaxGroups), UI.Warn); return; }
            RebuildRules();
        };
        Button openAll = UI.Btn(p, L.T("Open all"), 436, 604, 114, 34, false);
        openAll.Click += delegate { foreach (RuleGroup g in app.S.Groups) g.Collapsed = false; app.ApplySettings(); RebuildRules(); };
        Button closeAll = UI.Btn(p, L.T("Close all"), 556, 604, 114, 34, false);
        closeAll.Click += delegate
        {
            foreach (RuleGroup g in app.S.Groups) g.Collapsed = true;
            foreach (GiftRule r in app.S.Rules) r.Collapsed = true;
            app.ApplySettings();
            RebuildRules();
        };
        actionsMsg = UI.Para(p, L.T("Click a key button, then press the key (Esc = cancel)."), 38, 644, 632, 24);

        rulesHost = new Panel();
        rulesHost.BackColor = UI.Bg;
        rulesHost.Location = new Point(UI.D(38), UI.D(674));
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

    // Rebuilds the list right after the current click is done (the clicked button is part of the list).
    void RebuildLater() { app.Ui(delegate { if (!IsDisposed) RebuildRules(); }); }

    string SearchText { get { return searchBox == null ? "" : searchBox.Text.Trim(); } }

    static bool Has(string text, string query)
    {
        return text != null && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    bool RuleMatches(GiftRule r, RuleGroup g, string q)
    {
        if (q.Length == 0) return true;
        return Has(r.Summary, q) || Has(r.Gifts, q) || Has(r.Text, q) || Has(Path.GetFileName(r.SoundFile), q) ||
               Has(L.T(GiftRule.TriggerNames[Math.Max(0, Array.IndexOf(GiftRule.Triggers, r.Trigger))]), q) || Has(g.Name, q);
    }

    // Builds the list again (topics and actions). Folded topics/actions only build their title bar,
    // so even hundreds of actions stay quick.
    void RebuildRules()
    {
        CancelKeyCapture();
        Point scroll = actionsPage.AutoScrollPosition;
        string q = SearchText;
        int width = rulesHost.Width;
        Control focus = null;

        rulesHost.SuspendLayout();
        while (rulesHost.Controls.Count > 0) rulesHost.Controls[0].Dispose();
        int y = 0, shown = 0;
        foreach (RuleGroup g in app.S.Groups)
        {
            List<GiftRule> rules = app.S.Rules.FindAll(delegate (GiftRule r) { return r.GroupId == g.Id && RuleMatches(r, g, q); });
            if (q.Length > 0 && rules.Count == 0) continue;
            int total = app.S.Rules.FindAll(delegate (GiftRule r) { return r.GroupId == g.Id; }).Count;
            Panel bar = BuildGroupBar(g, total, width);
            bar.Location = new Point(0, y);
            rulesHost.Controls.Add(bar);
            y += bar.Height + UI.D(6);
            if (g.Collapsed && q.Length == 0) { y += UI.D(8); continue; }

            foreach (GiftRule r in rules)
            {
                Panel card = BuildRuleCard(r, width - UI.D(16));
                card.Location = new Point(UI.D(16), y);
                rulesHost.Controls.Add(card);
                y += card.Height + UI.D(6);
                shown++;
                if (r.Id == scrollToRule) focus = card;
            }
            if (rules.Count == 0)
            {
                Label empty = UI.Para(rulesHost, L.T("No actions here yet - click \"+ Add\" on the topic bar."), 0, 0, 500, 24);
                empty.Location = new Point(UI.D(16), y);
                y += UI.D(30);
            }
            y += UI.D(8);
        }
        if (q.Length > 0 && shown == 0)
        {
            Label none = UI.Para(rulesHost, L.F("Nothing found for \"{0}\".", q), 0, 0, 500, 24);
            none.Location = new Point(0, y);
            y += UI.D(30);
        }
        rulesHost.Height = y + UI.D(24);
        rulesHost.ResumeLayout();

        actionsPage.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        if (focus != null) actionsPage.ScrollControlIntoView(focus);
        scrollToRule = -1;
    }

    // The title bar of a topic: fold/unfold, name, on/off, add, rename, delete.
    Panel BuildGroupBar(RuleGroup g, int count, int width)
    {
        Panel bar = new Panel();
        bar.BackColor = GroupBar;
        bar.Size = new Size(width, UI.D(44));
        bool open = !g.Collapsed || SearchText.Length > 0;

        Button fold = UI.Btn(bar, open ? "▾" : "▸", 6, 6, 32, 32, false);
        fold.BackColor = GroupBar;
        fold.FlatAppearance.BorderSize = 0;
        Label name = UI.Lbl(bar, g.Name + "   (" + count + ")", 42, 11, 250, 11f, g.Enabled ? UI.Text : UI.Muted, FontStyle.Bold);
        name.AutoEllipsis = true;
        name.Cursor = Cursors.Hand;
        EventHandler toggle = delegate
        {
            if (SearchText.Length > 0) return;
            g.Collapsed = !g.Collapsed;
            app.ApplySettings();
            RebuildLater();
        };
        fold.Click += toggle;
        name.Click += toggle;

        CheckBox on = UI.Check(bar, L.T("On"), 296, 12, g.Enabled);
        on.BackColor = GroupBar;
        on.CheckedChanged += delegate
        {
            g.Enabled = on.Checked;
            name.ForeColor = g.Enabled ? UI.Text : UI.Muted;
            app.ApplySettings();
            app.Log(g.Enabled ? L.F("Topic \"{0}\" is on.", g.Name) : L.F("Topic \"{0}\" is off - its actions won't run.", g.Name));
        };
        Button add = UI.Btn(bar, L.T("+ Add"), 372, 6, 80, 32, true);
        add.Click += delegate
        {
            GiftRule r = app.AddRule(g.Id);
            if (r == null) { ActionsMessage(L.F("That's the maximum of {0} actions.", Settings.MaxRules), UI.Warn); return; }
            g.Collapsed = false;
            if (searchBox.Text.Length > 0) searchBox.Text = "";
            scrollToRule = r.Id;
            app.ApplySettings();
            RebuildLater();
            ActionsMessage(L.T("Added. Choose what triggers it (e.g. a Rose) and what it does."), UI.Good);
        };
        Button rename = UI.Btn(bar, L.T("Rename"), 456, 6, 86, 32, false);
        rename.Click += delegate
        {
            string n = AskText(L.T("Rename topic"), L.T("New name:"), g.Name);
            if (n == null) return;
            g.Name = n;
            app.ApplySettings();
            RebuildLater();
        };
        if (app.S.Groups.Count > 1)
        {
            Button del = UI.Btn(bar, L.T("Delete"), 546, 6, 80, 32, false);
            del.Click += delegate
            {
                string question = count > 0 ? L.F("Delete the topic \"{0}\" and its {1} actions?", g.Name, count) : L.F("Delete the topic \"{0}\"?", g.Name);
                if (MessageBox.Show(this, question, "ClutchKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                app.RemoveGroup(g);
                RebuildLater();
            };
        }
        return bar;
    }

    // One action: a title bar (fold, on/off, summary, test, duplicate, remove) and, when unfolded, its settings.
    Panel BuildRuleCard(GiftRule r, int width)
    {
        Panel card = new Panel();
        card.BackColor = UI.Card;
        bool open = !r.Collapsed;
        card.Size = new Size(width, UI.D(open ? 236 : 42));

        // title bar
        Button fold = UI.Btn(card, open ? "▾" : "▸", 4, 5, 30, 32, false);
        fold.BackColor = UI.Card;
        fold.FlatAppearance.BorderSize = 0;
        fold.Click += delegate { r.Collapsed = !r.Collapsed; app.ApplySettings(); RebuildLater(); };
        CheckBox on = UI.Check(card, "", 40, 12, r.Enabled);
        on.BackColor = UI.Card;
        on.CheckedChanged += delegate { r.Enabled = on.Checked; Changed(); };
        Label summary = UI.Lbl(card, r.Summary, 62, 11, 238, 10f, UI.Text, FontStyle.Regular);
        summary.AutoEllipsis = true;
        summary.Cursor = Cursors.Hand;
        summary.Click += delegate { r.Collapsed = !r.Collapsed; app.ApplySettings(); RebuildLater(); };
        Button test = UI.Btn(card, "", 304, 5, 118, 32, false);
        Button dup = UI.Btn(card, L.T("Duplicate"), 426, 5, 104, 32, false);
        dup.Click += delegate
        {
            GiftRule copy = app.DuplicateRule(r);
            if (copy == null) { ActionsMessage(L.F("That's the maximum of {0} actions.", Settings.MaxRules), UI.Warn); return; }
            scrollToRule = copy.Id;
            RebuildLater();
            ActionsMessage(L.T("Copied. Change what you need in the copy."), UI.Good);
        };
        Button remove = UI.Btn(card, L.T("Remove"), 534, 5, 80, 32, false);
        remove.Click += delegate
        {
            if (MessageBox.Show(this, L.F("Remove the action \"{0}\"?", r.Summary), "ClutchKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.RemoveRule(r);
            RebuildLater();
        };
        MethodInvoker testLabel = delegate { test.Text = r.Kind == GiftRule.KindSound ? L.T("Test") : L.T("Test in 3 s"); };
        testLabel();
        test.Click += delegate
        {
            if (!r.CanRun)
            {
                ActionsMessage(r.Kind == GiftRule.KindText ? L.T("Type the text first.") : r.Kind == GiftRule.KindSound ? L.T("Pick a sound first.") : L.T("Pick a key first."), UI.Warn);
                if (r.Collapsed) { r.Collapsed = false; RebuildLater(); }
                return;
            }
            if (r.Kind == GiftRule.KindSound) { app.Engine.RunTest(r); return; }
            Countdown(test, L.T("Test in 3 s"), delegate { app.Engine.RunTest(r); });
            ActionsMessage(L.T("Click into your game now - it runs in 3 seconds."), UI.Warn);
        };
        MethodInvoker refresh = delegate { summary.Text = r.Summary; };
        if (!open) return card;

        // row 1: what triggers it
        int y0 = 50;
        ComboBox trigger = UI.Combo(card, 16, y0, 130);
        trigger.Items.AddRange(L.All(GiftRule.TriggerNames));
        trigger.SelectedIndex = Math.Max(0, Array.IndexOf(GiftRule.Triggers, r.Trigger));
        ComboBox gift = GiftCombo(card, 154, y0, 170);
        gift.Text = r.Gifts;
        gift.TextChanged += delegate { r.Gifts = gift.Text.Trim(); refresh(); Changed(); };
        Label amountLbl = UI.Lbl(card, "", 332, y0 + 4, 96, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown amount = UI.Number(card, 430, y0, 0, 10000000, 0, 1);
        amount.Size = new Size(UI.D(80), UI.D(30));

        // row 2: what it does
        int y1 = y0 + 46;
        ComboBox kind = UI.Combo(card, 16, y1, 140);
        kind.Items.AddRange(L.All(GiftRule.KindNames));
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(GiftRule.Kinds, r.Kind));
        Button key = UI.Btn(card, "", 166, y1 - 2, 160, 34, false);
        key.BackColor = UI.Input;
        Label holdLbl = UI.Lbl(card, L.T("hold (ms)"), 336, y1 + 4, 90, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown hold = UI.Number(card, 430, y1, 10, 10000, r.HoldMs, 10);
        hold.Size = new Size(UI.D(80), UI.D(30));
        hold.ValueChanged += delegate { r.HoldMs = (int)hold.Value; Changed(); };
        TextBox text = new TextBox();
        UI.Style(text);
        text.BorderStyle = BorderStyle.FixedSingle;
        text.Text = r.Text;
        text.Location = new Point(UI.D(334), UI.D(y1 + 2));
        text.Size = new Size(UI.D(176), UI.D(28));
        text.TextChanged += delegate { r.Text = text.Text; refresh(); Changed(); };
        card.Controls.Add(text);
        CheckBox enter = UI.Check(card, "+ Enter", 518, y1 + 4, r.Enter);
        enter.BackColor = UI.Card;
        enter.CheckedChanged += delegate { r.Enter = enter.Checked; refresh(); Changed(); };
        TextBox sound = new TextBox();
        UI.Style(sound);
        sound.BorderStyle = BorderStyle.FixedSingle;
        sound.ReadOnly = true;
        sound.Location = new Point(UI.D(166), UI.D(y1 + 2));
        sound.Size = new Size(UI.D(226), UI.D(28));
        card.Controls.Add(sound);
        Button browse = UI.Btn(card, L.T("Browse..."), 398, y1 - 2, 96, 34, false);
        Label volLbl = UI.Lbl(card, L.T("volume"), 500, y1 + 4, 56, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown vol = UI.Number(card, 556, y1, 0, 100, r.Volume, 5);
        vol.Size = new Size(UI.D(56), UI.D(30));
        vol.ValueChanged += delegate { r.Volume = (int)vol.Value; Changed(); };

        // row 3: how often
        int y2 = y1 + 46;
        UI.Lbl(card, L.T("Times"), 16, y2 + 4, 56, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown times = UI.Number(card, 72, y2, 1, 100, r.Times, 1);
        times.Size = new Size(UI.D(56), UI.D(30));
        times.ValueChanged += delegate { r.Times = (int)times.Value; refresh(); Changed(); };
        UI.Lbl(card, L.T("cooldown (s)"), 136, y2 + 4, 100, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown cooldown = UI.Number(card, 236, y2, 0, 3600, r.CooldownSec, 1);
        cooldown.Size = new Size(UI.D(60), UI.D(30));
        cooldown.ValueChanged += delegate { r.CooldownSec = (int)cooldown.Value; Changed(); };
        CheckBox perGift = UI.Check(card, L.T("Every gift in a combo"), 306, y2 + 4, r.PerGift);
        perGift.BackColor = UI.Card;
        perGift.CheckedChanged += delegate { r.PerGift = perGift.Checked; Changed(); };

        // row 4: which topic it's in
        int y3 = y2 + 46;
        UI.Lbl(card, L.T("Topic"), 16, y3 + 4, 56, 10f, UI.Muted, FontStyle.Regular);
        ComboBox topic = UI.Combo(card, 72, y3, 224);
        foreach (RuleGroup g in app.S.Groups) topic.Items.Add(g.Name);
        topic.SelectedIndex = Math.Max(0, app.S.Groups.FindIndex(delegate (RuleGroup g) { return g.Id == r.GroupId; }));
        topic.SelectedIndexChanged += delegate
        {
            RuleGroup to = app.S.Groups[topic.SelectedIndex];
            if (to.Id == r.GroupId) return;
            app.MoveRule(r, to.Id);
            to.Collapsed = false;
            scrollToRule = r.Id;
            RebuildLater();   // not from inside the combo's own event
            ActionsMessage(L.F("Moved to \"{0}\".", to.Name), UI.Good);
        };

        bool building = true;
        MethodInvoker layout = delegate
        {
            bool isGift = r.Trigger == LiveEvent.Gift, isLikes = r.Trigger == LiveEvent.Likes;
            bool isText = r.Kind == GiftRule.KindText, isSound = r.Kind == GiftRule.KindSound;
            gift.Visible = isGift;
            amountLbl.Visible = amount.Visible = isGift || isLikes;
            amountLbl.Text = isLikes ? L.T("every (likes)") : L.T("min. coins");
            building = true;
            amount.Minimum = isLikes ? 1 : 0;
            amount.Value = Math.Max(amount.Minimum, Math.Min(amount.Maximum, isLikes ? r.EveryLikes : r.MinCoins));
            building = false;
            key.Visible = !isSound;
            holdLbl.Visible = hold.Visible = !isText && !isSound;
            text.Visible = enter.Visible = isText;
            sound.Visible = browse.Visible = volLbl.Visible = vol.Visible = isSound;
            sound.Text = r.SoundFile.Length > 0 ? Path.GetFileName(r.SoundFile) : L.T("(no sound picked)");
            perGift.Visible = isGift && !isSound;   // sounds play once per gift send
            if (key != keyCapBtn) key.Text = isText ? (r.Key.IsSet ? L.F("First: {0}", r.Key) : L.T("First: no key")) : r.Key.ToString();
            key.ForeColor = r.Key.IsSet ? UI.Text : UI.Muted;
            testLabel();
            refresh();
        };
        amount.ValueChanged += delegate
        {
            if (building) return;
            if (r.Trigger == LiveEvent.Likes) r.EveryLikes = (int)amount.Value; else r.MinCoins = (int)amount.Value;
            refresh();
            Changed();
        };
        trigger.SelectedIndexChanged += delegate
        {
            r.Trigger = GiftRule.Triggers[trigger.SelectedIndex];
            if (r.Trigger == LiveEvent.Join && r.CooldownSec == 0) cooldown.Value = 10;   // joins come fast in busy rooms
            layout();
            Changed();
        };
        kind.SelectedIndexChanged += delegate
        {
            r.Kind = GiftRule.Kinds[kind.SelectedIndex];
            layout();
            Changed();
        };
        key.Click += delegate
        {
            StartKeyCapture(key, delegate { return r.Key; }, delegate (Hotkey hk) { r.Key = hk; layout(); }, false);
            if (keyCapBtn == key)
                ActionsMessage(r.Kind == GiftRule.KindText
                    ? L.T("Press the key that opens the chat first (e.g. T in Minecraft), or Backspace for none. Esc = cancel.")
                    : L.T("Press the key it should press in your game (Backspace = no key, Esc = cancel)."), UI.Warn);
        };
        browse.Click += delegate
        {
            string picked = PickSound(r.SoundFile);
            if (picked == null) return;
            r.SoundFile = picked;
            layout();
            Changed();
            app.Engine.RunTest(r);   // play it once so you hear what you picked
        };
        layout();
        return card;
    }

    // Small "type a name" window (new topic, rename). Returns the text, or null if cancelled/empty.
    string AskText(string title, string prompt, string initial)
    {
        using (Form f = new Form())
        {
            f.Text = title;
            f.Icon = app.AppIcon;
            f.BackColor = UI.Bg;
            f.ForeColor = UI.Text;
            f.Font = UI.F(10f, FontStyle.Regular);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(UI.D(440), UI.D(170));
            UI.Para(f, prompt, 20, 18, 400, 44);
            TextBox box = new TextBox();
            UI.Style(box);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Text = initial;
            box.MaxLength = 60;
            box.Location = new Point(UI.D(20), UI.D(66));
            box.Size = new Size(UI.D(400), UI.D(28));
            f.Controls.Add(box);
            Button ok = UI.Btn(f, L.T("OK"), 200, 112, 110, 38, true);
            Button cancel = UI.Btn(f, L.T("Cancel"), 316, 112, 104, 38, false);
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            if (f.ShowDialog(this) != DialogResult.OK) return null;
            string s = box.Text.Trim();
            return s.Length > 0 ? s : null;
        }
    }

    // Lets you pick a sound file and copies it into ClutchKit's "sounds" folder, so the action keeps
    // working if the original file is moved or deleted. Returns the copy's path, or null.
    string PickSound(string current)
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = L.T("Pick a sound");
            d.Filter = L.T("Sounds") + " (*.mp3, *.wav, *.wma, *.m4a, *.aac)|*.mp3;*.wav;*.wma;*.m4a;*.aac|" + L.T("All files") + "|*.*";
            string soundsDir = Path.Combine(app.Folder, "sounds");
            try { d.InitialDirectory = current.Length > 0 && File.Exists(current) ? Path.GetDirectoryName(current) : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic); }
            catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return null;
            try
            {
                Directory.CreateDirectory(soundsDir);
                string src = Path.GetFullPath(d.FileName);
                if (src.StartsWith(Path.GetFullPath(soundsDir) + "\\", StringComparison.OrdinalIgnoreCase)) return src;
                string dest = Path.Combine(soundsDir, Path.GetFileName(src));
                if (File.Exists(dest) && new FileInfo(dest).Length != new FileInfo(src).Length)
                    dest = Path.Combine(soundsDir, Path.GetFileNameWithoutExtension(src) + "-" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(src));
                File.Copy(src, dest, true);
                return dest;
            }
            catch (Exception ex)
            {
                ActionsMessage(L.F("Couldn't copy the sound ({0}), using it from where it is.", ex.Message), UI.Warn);
                return d.FileName;
            }
        }
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
            if (left > 0) { if (!b.IsDisposed) b.Text = left + "..."; return; }
            t.Stop();
            testTimers.Remove(t);
            t.Dispose();
            if (!b.IsDisposed)
            {
                b.Text = label;
                b.Enabled = true;
            }
            action();
        };
        t.Start();
    }

    void ActionsMessage(string text, Color color) { actionsMsg.Text = text; actionsMsg.ForeColor = color; }

    void OnQueue(object sender, EventArgs e)
    {
        int n = app.Engine.Waiting;
        queueLbl.Text = n == 0 ? L.T("Queue: empty") : L.F("Queue: {0} waiting", n);
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
        elevLbl.Text = show ? L.F("{0} runs as administrator, so Windows blocks keys from ClutchKit. Restart ClutchKit as administrator to fix it.", program) : "";
        elevLbl.Visible = elevBtn.Visible = show;
    }

    // ---------------- key capture (action keys) ----------------

    void StartKeyCapture(Button b, Func<Hotkey> get, Action<Hotkey> set, bool isPauseKey)
    {
        if (keyCapBtn == b) { CancelKeyCapture(); return; }
        CancelCapture();
        keyCapBtn = b;
        keyCapGet = get;
        keyCapSet = set;
        keyCapIsPauseKey = isPauseKey;
        app.CapturingHotkey = true;
        b.Text = L.T("Press a key...");
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

        if (key == Keys.Escape && mods == 0) { CancelKeyCapture(); ActionsMessage(L.T("Cancelled - nothing changed."), UI.Muted); return true; }
        Hotkey hk = key == Keys.Back && mods == 0 ? new Hotkey(0, 0) : new Hotkey(mods, (int)key);
        if (keyCapIsPauseKey && hk.IsSet)
        {
            string owner = app.KeyOwner(hk, null, -2);
            if (owner != null) { ActionsMessage(L.F("{0} is already used for \"{1}\". Press a different key.", hk, owner), UI.Warn); return true; }
        }
        keyCapSet(hk);
        CancelKeyCapture();
        app.ApplySettings();
        ActionsMessage(hk.IsSet ? L.F("Saved: {0}.", hk) : L.T("No key now."), hk.IsSet ? UI.Good : UI.Muted);
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    static extern short AsyncKeyState(int vk);

    static bool IsWinDown() { return (AsyncKeyState(0x5B) & 0x8000) != 0 || (AsyncKeyState(0x5C) & 0x8000) != 0; }

    // ---------------- TikTok LIVE ----------------

    Panel BuildTikTokPage()
    {
        Panel p = NewPage(L.T("TikTok LIVE"), L.T("Connect to your LIVE so gifts, follows, shares, joins and likes can trigger your actions. TikTok has no official way to do this, so ClutchKit uses Euler Stream (free account) to read them."));
        Settings s = app.S;

        UI.FieldLabel(p, L.T("Your TikTok username"), 38, 126);
        TextBox user = new TextBox();
        UI.Style(user);
        user.BorderStyle = BorderStyle.FixedSingle;
        user.Text = s.TikTokUser;
        user.Location = new Point(UI.D(230), UI.D(126));
        user.Size = new Size(UI.D(260), UI.D(28));
        user.TextChanged += delegate { app.S.TikTokUser = TikTokLive.CleanUser(user.Text); Changed(); };
        p.Controls.Add(user);

        UI.FieldLabel(p, L.T("Euler Stream API key"), 38, 166);
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
        signup.Text = L.T("Get a free key");
        signup.Font = UI.F(10f, FontStyle.Regular);
        signup.LinkColor = UI.Accent;
        signup.ActiveLinkColor = UI.Text;
        signup.Location = new Point(UI.D(502), UI.D(170));
        signup.AutoSize = true;
        signup.LinkClicked += delegate { try { Process.Start(TikTokLive.SignupUrl); } catch { } };
        p.Controls.Add(signup);

        CheckBox auto = UI.Check(p, L.T("Connect automatically when ClutchKit starts"), 38, 210, s.AutoConnect);
        auto.CheckedChanged += delegate { app.S.AutoConnect = auto.Checked; Changed(); };

        connectBtn = UI.Btn(p, L.T("Connect"), 38, 250, 180, 40, true);
        connectBtn.Click += delegate
        {
            if (app.TikTok.Running) app.TikTok.Stop();
            else app.TikTok.Start(app.S.TikTokUser, app.S.TikTokKey);
        };
        ttStatus = UI.Para(p, "", 232, 250, 440, 44);

        UI.Para(p, L.T("Your username and key are only sent to Euler Stream, and only while connected. The key is saved in settings.ini on this PC."),
                38, 304, 630, 40);
        UI.Lbl(p, L.T("Activity"), 38, 350, 300, 11f, UI.Text, FontStyle.Bold);
        MakeLog(p, 38, 380, 632, 240);

        app.TikTok.StatusChanged += OnTikTokStatus;
        OnTikTokStatus(null, EventArgs.Empty);
        return p;
    }

    void OnTikTokStatus(object sender, EventArgs e)
    {
        TikTokLive t = app.TikTok;
        connectBtn.Text = t.Running ? L.T("Disconnect") : L.T("Connect");
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
        if (searchTimer != null) searchTimer.Dispose();
    }
}
