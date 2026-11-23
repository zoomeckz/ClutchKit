// ClutchKit - the "Widgets" page of the settings window: goal bars, the subathon timer and the top lists.
// Each widget has its own LIVE Studio link (Copy link) and a browser preview.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

public partial class MainForm
{
    Panel widgetsHost;
    Label widgetMsg, timerLbl, timerStateLbl, topGiftersLbl, topLikersLbl;
    Button timerBtn;
    readonly Dictionary<Goal, Label> goalLabels = new Dictionary<Goal, Label>();

    Panel BuildWidgetsPage()
    {
        Panel p = NewPage(L.T("Stream widgets"), L.T("Goal bars, a subathon timer and top lists for your stream. Each one is its own Link source in LIVE Studio, so you can place it anywhere."));

        Button newLive = UI.Btn(p, L.T("New LIVE: start over"), 38, 124, 220, 36, false);
        newLive.Click += delegate
        {
            if (MessageBox.Show(this, L.T("Clear this LIVE's top lists and the goals that start over each LIVE?"), "ClutchKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.W.NewLive();
            WidgetMessage(L.T("Started over."), UI.Good);
        };
        UI.Para(p, L.T("This happens by itself when your next LIVE starts."), 270, 132, 400, 24);
        widgetMsg = UI.Para(p, "", 38, 172, 632, 40);
        WidgetMessage(null, UI.Muted);

        widgetsHost = new Panel();
        widgetsHost.BackColor = UI.Bg;
        widgetsHost.Location = new Point(UI.D(38), UI.D(220));
        widgetsHost.Width = UI.D(632);
        p.Controls.Add(widgetsHost);
        RebuildWidgets();
        p.VisibleChanged += delegate { if (p.Visible) RebuildWidgets(); };   // counters may have been renamed meanwhile

        app.W.Changed += OnWidgetsChanged;
        return p;
    }

    void WidgetMessage(string text, Color color)
    {
        widgetMsg.Text = text ?? L.T("Copy a widget's link, then in LIVE Studio: Add source > Link > paste. Widgets use the font and colors of your first counter.");
        widgetMsg.ForeColor = color;
    }

    Panel WidgetCard(int y, int h)
    {
        Panel card = new Panel();
        card.BackColor = UI.Card;
        card.Location = new Point(0, UI.D(y));
        card.Size = new Size(UI.D(632), UI.D(h));
        widgetsHost.Controls.Add(card);
        return card;
    }

    TextBox WidgetTitleBox(Control card, string text, Action<string> set)
    {
        TextBox box = new TextBox();
        UI.Style(box);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Text = text;
        box.MaxLength = 60;
        box.Location = new Point(UI.D(16), UI.D(14));
        box.Size = new Size(UI.D(200), UI.D(28));
        box.TextChanged += delegate { set(box.Text.Trim()); Changed(); };
        card.Controls.Add(box);
        Hint(box, L.T("Title (empty = none)"));
        return box;
    }

    // "Copy link" and "Preview" for one widget.
    void LinkButtons(Control card, int y, string view, string name)
    {
        Button copy = UI.Btn(card, L.T("Copy link"), 16, y, 180, 36, true);
        copy.Click += delegate
        {
            app.CopyText(Overlay.ViewLink(app.S.Port, view, name));
            WidgetMessage(L.T("Copied! Paste it in LIVE Studio with Ctrl+V."), UI.Good);
        };
        Button preview = UI.Btn(card, L.T("Preview"), 204, y, 130, 36, false);
        preview.Click += delegate { app.OpenPreview(view, name); };
    }

    void RebuildWidgets()
    {
        widgetsHost.SuspendLayout();
        UI.ClearControls(widgetsHost);
        goalLabels.Clear();
        int y = 0;

        // ---- goals ----
        UI.Lbl(widgetsHost, L.T("Goals"), 0, y, 200, 11f, UI.Text, FontStyle.Bold);
        UI.Para(widgetsHost, L.T("\"Raise by\" moves a reached goal up (e.g. +1000), so the bar keeps going."), 210, y + 2, 422, 40);
        y += 46;
        foreach (Goal g in app.S.Goals) { BuildGoalCard(g, y); y += 196 + 12; }
        if (app.S.Goals.Count < Settings.MaxGoals)
        {
            Button add = UI.Btn(widgetsHost, L.T("+ Add goal"), 0, y, 180, 38, true);
            add.Click += delegate
            {
                Goal g = app.AddGoal();
                if (g != null) { RebuildWidgets(); WidgetMessage(L.T("Added. Pick what it counts and the goal."), UI.Good); }
            };
            y += 38 + 26;
        }
        else y += 10;

        // ---- subathon timer ----
        UI.Lbl(widgetsHost, L.T("Subathon timer"), 0, y, 300, 11f, UI.Text, FontStyle.Bold);
        UI.Para(widgetsHost, L.T("Gifts, follows, shares and likes add time while it runs (or is paused)."), 210, y + 2, 422, 40);
        y += 46;
        BuildTimerCard(y);
        y += 272 + 26;

        // ---- top lists ----
        UI.Lbl(widgetsHost, L.T("Top lists"), 0, y, 300, 11f, UI.Text, FontStyle.Bold);
        UI.Para(widgetsHost, L.T("Gifters by coins, likers by likes. Test events count for this LIVE only."), 210, y + 2, 422, 40);
        y += 46;
        topGiftersLbl = BuildTopCard(app.S.TopGifters, y);
        y += 174 + 12;
        topLikersLbl = BuildTopCard(app.S.TopLikers, y);
        y += 174 + 24;

        widgetsHost.Height = UI.D(y);
        widgetsHost.ResumeLayout();
        OnWidgetsChanged(null, EventArgs.Empty);
    }

    void BuildGoalCard(Goal g, int y)
    {
        Panel card = WidgetCard(y, 196);
        TextBox title = WidgetTitleBox(card, g.Title, delegate (string t) { g.Title = t; });

        ComboBox src = UI.Combo(card, 226, 14, 200);
        src.Items.AddRange(L.All(Goal.SourceNames));
        foreach (Counter c in app.S.Counters) src.Items.Add(L.F("Counter: {0}", c.DisplayName));
        int si = Array.IndexOf(Goal.Sources, g.Source);
        if (g.Source == Goal.FromCounter)
        {
            int ci = app.S.Counters.FindIndex(delegate (Counter c) { return c.Id == g.CounterId; });
            si = ci >= 0 ? Goal.Sources.Length + ci : -1;
            if (si < 0) { src.Items.Add(L.T("(counter removed)")); si = src.Items.Count - 1; }
        }
        src.SelectedIndex = Math.Max(0, si);
        CheckBox show = UI.Check(card, L.T("Show on stream"), 440, 17, g.Visible);
        show.BackColor = UI.Card;
        show.CheckedChanged += delegate { g.Visible = show.Checked; Changed(); };

        Label value = UI.Lbl(card, "", 16, 52, 200, 14f, UI.Text, FontStyle.Bold);
        goalLabels[g] = value;
        UI.Lbl(card, L.T("Goal"), 226, 60, 62, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown target = UI.Number(card, 290, 56, 1, 2000000000, (int)Math.Min(2000000000, g.Target), 100);
        target.Size = new Size(UI.D(110), UI.D(30));
        target.ValueChanged += delegate { g.Target = (long)target.Value; g.Reached = false; Changed(); app.W.SetGoalValue(g, g.Value); };
        UI.Lbl(card, L.T("raise by"), 412, 60, 92, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown step = UI.Number(card, 506, 56, 0, 2000000000, (int)Math.Min(2000000000, g.Step), 100);
        step.Size = new Size(UI.D(100), UI.D(30));
        step.ValueChanged += delegate { g.Step = (long)step.Value; Changed(); };

        CheckBox restart = UI.Check(card, L.T("Start over each LIVE"), 16, 104, g.Restart);
        restart.BackColor = UI.Card;
        restart.CheckedChanged += delegate { g.Restart = restart.Checked; Changed(); };
        UI.Lbl(card, L.T("Bar color"), 300, 104, 100, 10f, UI.Muted, FontStyle.Regular);
        UI.ColorButton(card, 404, 100, g.BarColor, delegate (Color c) { g.BarColor = c; Changed(); });

        LinkButtons(card, 146, "goal:" + g.Id, "goal");
        Button zero = UI.Btn(card, L.T("Set to 0"), 342, 146, 120, 36, false);
        zero.Click += delegate { app.W.SetGoalValue(g, 0); };
        Button remove = UI.Btn(card, L.T("Remove"), 516, 146, 100, 36, false);
        remove.Click += delegate
        {
            if (MessageBox.Show(this, L.F("Remove the goal \"{0}\"?", g.Title.Length > 0 ? g.Title : Goal.DefaultTitle(g.Source)), "ClutchKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.RemoveGoal(g);
            app.Ui(delegate { if (!IsDisposed) RebuildWidgets(); });   // not from inside the button's own click
        };

        MethodInvoker fromCounter = delegate
        {
            bool counter = g.Source == Goal.FromCounter;
            restart.Enabled = zero.Enabled = !counter;   // a counter goal shows the counter as it is
        };
        fromCounter();
        src.SelectedIndexChanged += delegate
        {
            string oldDefault = g.Source == Goal.FromCounter ? CounterTitle(g.CounterId) : Goal.DefaultTitle(g.Source);
            int i = src.SelectedIndex;
            if (i < Goal.Sources.Length) g.Source = Goal.Sources[i];
            else if (i - Goal.Sources.Length < app.S.Counters.Count) { g.Source = Goal.FromCounter; g.CounterId = app.S.Counters[i - Goal.Sources.Length].Id; }
            else return;
            if (g.Title.Length == 0 || g.Title == oldDefault)   // a title you didn't change follows what it counts
                title.Text = g.Source == Goal.FromCounter ? CounterTitle(g.CounterId) : Goal.DefaultTitle(g.Source);
            fromCounter();
            Changed();
            app.W.SetGoalValue(g, g.Value);
        };
    }

    string CounterTitle(int id)
    {
        Counter c = app.S.Find(id);
        return c != null ? c.DisplayName : L.T("Goal");
    }

    void BuildTimerCard(int y)
    {
        TimerSetup t = app.S.Timer;
        Panel card = WidgetCard(y, 272);
        WidgetTitleBox(card, t.Title ?? "", delegate (string s) { t.Title = s; });
        CheckBox show = UI.Check(card, L.T("Show on stream"), 226, 17, t.Visible);
        show.BackColor = UI.Card;
        show.CheckedChanged += delegate { t.Visible = show.Checked; Changed(); };

        timerLbl = UI.Lbl(card, "", 16, 46, 220, 20f, UI.Text, FontStyle.Bold);
        timerLbl.Size = new Size(UI.D(220), UI.D(48));
        timerBtn = UI.Btn(card, "", 246, 52, 120, 36, true);
        timerBtn.Click += delegate
        {
            if (app.W.TimerState == Widgets.Clock.Running) app.W.PauseTimer(); else app.W.StartTimer();
        };
        Button reset = UI.Btn(card, L.T("Reset"), 372, 52, 90, 36, false);
        reset.Click += delegate
        {
            if (app.W.TimerState != Widgets.Clock.Idle &&
                MessageBox.Show(this, L.F("Set the timer back to {0} and stop it?", Widgets.ClockText((long)t.StartMin * 60000)), "ClutchKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.W.ResetTimer();
        };
        Button plus = UI.Btn(card, "+5 min", 468, 52, 70, 36, false);
        plus.Click += delegate { app.W.AddTime(5 * 60000, true); };
        Button minus = UI.Btn(card, "-5 min", 544, 52, 70, 36, false);
        minus.Click += delegate { app.W.AddTime(-5 * 60000, true); };

        UI.Lbl(card, L.T("Starts at (min)"), 16, 106, 140, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown start = UI.Number(card, 160, 102, 1, 100000, t.StartMin, 5);
        start.Size = new Size(UI.D(80), UI.D(30));
        start.ValueChanged += delegate { t.StartMin = (int)start.Value; Changed(); app.W.StartTimeChanged(); };
        UI.Lbl(card, L.T("max. hours (0 = none)"), 256, 106, 170, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown max = UI.Number(card, 430, 102, 0, 10000, t.MaxHours, 1);
        max.Size = new Size(UI.D(70), UI.D(30));
        max.ValueChanged += delegate { t.MaxHours = (int)max.Value; Changed(); };

        UI.Lbl(card, L.T("Seconds added for each"), 16, 146, 400, 10f, UI.Text, FontStyle.Bold);
        TimeField(card, L.T("coin"), 16, 74, t.PerCoin, delegate (int v) { t.PerCoin = v; });
        TimeField(card, L.T("follow"), 150, 222, t.PerFollow, delegate (int v) { t.PerFollow = v; });
        TimeField(card, L.T("share"), 298, 370, t.PerShare, delegate (int v) { t.PerShare = v; });
        TimeField(card, L.T("100 likes"), 446, 546, t.Per100Likes, delegate (int v) { t.Per100Likes = v; });

        LinkButtons(card, 222, "timer", "timer");
        timerStateLbl = UI.Para(card, "", 350, 230, 266, 24);
    }

    void TimeField(Control card, string label, int x, int numX, int value, Action<int> set)
    {
        UI.Lbl(card, label, x, 182, numX - x - 4, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown n = UI.Number(card, numX, 178, 0, 86400, value, 5);
        n.Size = new Size(UI.D(64), UI.D(30));
        n.ValueChanged += delegate { set((int)n.Value); Changed(); };
    }

    Label BuildTopCard(TopSetup t, int y)
    {
        Panel card = WidgetCard(y, 174);
        WidgetTitleBox(card, t.Title ?? "", delegate (string s) { t.Title = s; });
        CheckBox show = UI.Check(card, L.T("Show on stream"), 226, 17, t.Visible);
        show.BackColor = UI.Card;
        show.CheckedChanged += delegate { t.Visible = show.Checked; Changed(); };
        Label kind = UI.Lbl(card, t.ByCoins ? L.T("by coins") : L.T("by likes"), 440, 18, 176, 10f, UI.Muted, FontStyle.Regular);
        kind.TextAlign = ContentAlignment.TopRight;

        UI.Lbl(card, L.T("How many"), 16, 58, 100, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown count = UI.Number(card, 118, 54, 1, 10, t.Count, 1);
        count.Size = new Size(UI.D(60), UI.D(30));
        count.ValueChanged += delegate { t.Count = (int)count.Value; Changed(); };
        ComboBox period = UI.Combo(card, 190, 54, 160);
        period.Items.AddRange(new object[] { L.T("This LIVE"), L.T("All time") });
        period.SelectedIndex = t.AllTime ? 1 : 0;
        period.SelectedIndexChanged += delegate { t.AllTime = period.SelectedIndex == 1; Changed(); OnWidgetsChanged(null, EventArgs.Empty); };
        Button clear = UI.Btn(card, L.T("Clear"), 360, 52, 110, 34, false);
        clear.Click += delegate
        {
            string q = t.AllTime ? L.T("Clear the all-time top lists (gifters and likers)?") : L.T("Clear this LIVE's top lists (gifters and likers)?");
            if (MessageBox.Show(this, q, "ClutchKit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.W.ClearTop(t.AllTime);
        };

        Label now = UI.Para(card, "", 16, 96, 600, 24);
        LinkButtons(card, 126, "top:" + t.Key, "top");
        return now;
    }

    string TopText(TopSetup t)
    {
        List<Widgets.Fan> top = app.W.Top(t);
        if (top.Count == 0) return L.T("Nobody yet.");
        List<string> parts = new List<string>();
        for (int i = 0; i < top.Count && i < 5; i++)
            parts.Add((i + 1) + ". " + top[i].Name + " " + (t.ByCoins ? top[i].Coins : top[i].Likes).ToString("N0"));
        return string.Join("   ", parts.ToArray());
    }

    void OnWidgetsChanged(object sender, EventArgs e)
    {
        if (IsDisposed || widgetsHost == null) return;
        foreach (KeyValuePair<Goal, Label> kv in goalLabels)
        {
            if (kv.Value.IsDisposed) continue;
            long v = app.W.GoalValue(kv.Key);
            kv.Value.Text = v.ToString("N0") + " / " + kv.Key.Target.ToString("N0");
            kv.Value.ForeColor = v >= kv.Key.Target ? UI.Good : UI.Text;
        }
        if (timerLbl != null && !timerLbl.IsDisposed)
        {
            Widgets.Clock state = app.W.TimerState;
            timerLbl.Text = Widgets.ClockText(app.W.TimeLeftMs);
            timerBtn.Text = state == Widgets.Clock.Running ? L.T("Pause") : state == Widgets.Clock.Paused ? L.T("Go on") : L.T("Start");
            timerStateLbl.Text = state == Widgets.Clock.Running ? L.T("Running.") : state == Widgets.Clock.Paused ? L.T("Paused - gifts still add time.") :
                                 state == Widgets.Clock.Done ? L.T("Time's up!") : L.T("Not started yet.");
            timerStateLbl.ForeColor = state == Widgets.Clock.Running ? UI.Good : state == Widgets.Clock.Done ? UI.Accent : UI.Muted;
        }
        if (topGiftersLbl != null && !topGiftersLbl.IsDisposed) topGiftersLbl.Text = TopText(app.S.TopGifters);
        if (topLikersLbl != null && !topLikersLbl.IsDisposed) topLikersLbl.Text = TopText(app.S.TopLikers);
    }
}
