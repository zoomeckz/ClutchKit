// ClutchKit - the "OBS" page of the settings window: browser source links for the counters and
// widgets, and the connection that lets actions control OBS (scenes, sources, replay, recording, mute).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

public partial class MainForm
{
    Button obsBtn;
    Label obsStatus;

    class ObsView
    {
        public string View, Name, Text, Size;
        public override string ToString() { return Text; }
    }

    Panel BuildObsPage()
    {
        Panel p = NewPage(L.T("OBS Studio"), L.T("Streaming with OBS? Put your counters and widgets in OBS as Browser sources, and let gifts, follows and chat control OBS."));
        Settings s = app.S;

        // ---- browser sources ----
        UI.Lbl(p, L.T("Counters and widgets in OBS"), 38, 124, 620, 11f, UI.Text, FontStyle.Bold);
        UI.Para(p, L.T("In OBS: Sources > + > Browser > OK, paste the link into URL, set the width and height below and click OK. Everything you change in ClutchKit shows up by itself."),
                38, 154, 632, 44);
        ComboBox which = UI.Combo(p, 38, 208, 240);
        Button copy = UI.Btn(p, L.T("Copy OBS link"), 288, 206, 190, 36, true);
        Button preview = UI.Btn(p, L.T("Preview"), 486, 206, 110, 36, false);
        TextBox url = new TextBox();
        UI.Style(url);
        url.BorderStyle = BorderStyle.FixedSingle;
        url.ReadOnly = true;
        url.Location = new Point(UI.D(38), UI.D(254));
        url.Size = new Size(UI.D(558), UI.D(28));
        p.Controls.Add(url);
        Label size = UI.Para(p, "", 38, 290, 632, 24);

        MethodInvoker fill = delegate
        {
            ObsView keep = which.SelectedItem as ObsView;
            which.BeginUpdate();
            which.Items.Clear();
            which.Items.Add(new ObsView { View = null, Name = "counters", Text = L.T("Counters"), Size = "800 x 300" });
            foreach (Goal g in s.Goals)
                which.Items.Add(new ObsView { View = "goal:" + g.Id, Name = "goal", Size = "800 x 200",
                                              Text = L.F("Goal: {0}", g.Title.Length > 0 ? g.Title : Goal.DefaultTitle(g.Source)) });
            which.Items.Add(new ObsView { View = "timer", Name = "timer", Size = "600 x 200", Text = L.T("Subathon timer") });
            which.Items.Add(new ObsView { View = "top:gifters", Name = "top", Size = "500 x 500", Text = L.T("Top gifters") });
            which.Items.Add(new ObsView { View = "top:likers", Name = "top", Size = "500 x 500", Text = L.T("Top likers") });
            int at = 0;
            for (int i = 0; keep != null && i < which.Items.Count; i++)
                if (((ObsView)which.Items[i]).View == keep.View) at = i;
            which.SelectedIndex = at;
            which.EndUpdate();
        };
        which.SelectedIndexChanged += delegate
        {
            ObsView v = which.SelectedItem as ObsView;
            if (v == null) return;
            url.Text = Overlay.ObsLink(s.Port, v.View);
            size.Text = L.F("Suggested size: {0} (any size works - it scales to fit).", v.Size);
            size.ForeColor = UI.Muted;
        };
        copy.Click += delegate
        {
            app.CopyText(url.Text);
            size.Text = L.T("Copied! In OBS: paste it into the Browser source's URL (Ctrl+V).");
            size.ForeColor = UI.Good;
        };
        preview.Click += delegate
        {
            ObsView v = which.SelectedItem as ObsView;
            if (v != null) app.OpenPreview(v.View, v.Name);
        };
        fill();
        p.VisibleChanged += delegate { if (p.Visible) fill(); };   // goals may have changed on the Widgets page

        // ---- control OBS ----
        UI.Lbl(p, L.T("Let actions control OBS"), 38, 336, 620, 11f, UI.Text, FontStyle.Bold);
        UI.Para(p, L.T("Turn it on in OBS 28 or newer: Tools > WebSocket Server Settings > tick \"Enable WebSocket server\". \"Show Connect Info\" shows the port and password."),
                38, 366, 632, 60);

        UI.FieldLabel(p, L.T("OBS address"), 38, 432);
        TextBox host = new TextBox();
        UI.Style(host);
        host.BorderStyle = BorderStyle.FixedSingle;
        host.Text = s.ObsHost;
        host.Location = new Point(UI.D(200), UI.D(432));
        host.Size = new Size(UI.D(160), UI.D(28));
        host.TextChanged += delegate { app.S.ObsHost = ObsLink.CleanHost(host.Text); Changed(); };
        p.Controls.Add(host);
        UI.Lbl(p, L.T("Port"), 372, 436, 60, 10f, UI.Muted, FontStyle.Regular);
        NumericUpDown port = UI.Number(p, 434, 432, 1, 65535, s.ObsPort, 1);
        port.Size = new Size(UI.D(90), UI.D(30));
        port.ValueChanged += delegate { app.S.ObsPort = (int)port.Value; Changed(); };

        UI.FieldLabel(p, L.T("Password"), 38, 472);
        TextBox pw = new TextBox();
        UI.Style(pw);
        pw.BorderStyle = BorderStyle.FixedSingle;
        pw.UseSystemPasswordChar = true;
        pw.Text = s.ObsPassword;
        pw.Location = new Point(UI.D(200), UI.D(472));
        pw.Size = new Size(UI.D(324), UI.D(28));
        pw.TextChanged += delegate { app.S.ObsPassword = pw.Text.Trim(); Changed(); };
        p.Controls.Add(pw);

        CheckBox auto = UI.Check(p, L.T("Connect automatically when ClutchKit starts"), 38, 514, s.ObsAuto);
        auto.CheckedChanged += delegate { app.S.ObsAuto = auto.Checked; Changed(); };

        obsBtn = UI.Btn(p, L.T("Connect"), 38, 552, 180, 40, true);
        obsBtn.Click += delegate
        {
            if (app.Obs.Running) app.Obs.Stop();
            else app.Obs.Start(app.S.ObsHost, app.S.ObsPort, app.S.ObsPassword);
        };
        obsStatus = UI.Para(p, "", 232, 552, 440, 44);

        UI.Para(p, L.T("Then on the Actions page pick \"Control OBS\": switch scene, show or hide a source (for a few seconds), save the replay buffer, record or mute. The password is saved in settings.ini on this PC."),
                38, 608, 632, 60);

        app.Obs.StatusChanged += OnObsStatus;
        app.Obs.ListsChanged += OnObsStatus;
        OnObsStatus(null, EventArgs.Empty);
        return p;
    }

    void OnObsStatus(object sender, EventArgs e)
    {
        ObsLink o = app.Obs;
        obsBtn.Text = o.Running ? L.T("Disconnect") : L.T("Connect");
        string text = o.Detail;
        if (o.Connected && (o.Scenes.Length > 0 || o.Sources.Length > 0))
            text += " " + L.F("{0} scenes, {1} sources.", o.Scenes.Length, o.Sources.Length);
        obsStatus.Text = text;
        switch (o.Current)
        {
            case ObsLink.State.Connected: obsStatus.ForeColor = UI.Good; break;
            case ObsLink.State.Error: obsStatus.ForeColor = UI.Accent; break;
            case ObsLink.State.Off: obsStatus.ForeColor = UI.Muted; break;
            default: obsStatus.ForeColor = UI.Warn; break;
        }
    }
}
