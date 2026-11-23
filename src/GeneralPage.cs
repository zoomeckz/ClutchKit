// ClutchKit - the "General" page: a thank-you, the language choice and a quick start.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

public partial class MainForm
{
    public const string IssuesUrl = "https://github.com/zoomeckz/ClutchKit/issues";

    Panel BuildGeneralPage()
    {
        Panel p = NewPage(L.T("General"), L.T("Language, a quick start, and a few words from us."));

        // thank you
        Panel thanks = new Panel();
        thanks.BackColor = UI.Card;
        thanks.Location = new Point(UI.D(38), UI.D(120));
        thanks.Size = new Size(UI.D(632), UI.D(150));
        p.Controls.Add(thanks);
        UI.Lbl(thanks, L.T("Thank you for using ClutchKit!"), 18, 14, 590, 13f, UI.Text, FontStyle.Bold);
        UI.Para(thanks, L.T("ClutchKit is free, with no limits and no subscription: as many counters, sounds and actions as you like. It runs on your own PC, so it's fast and private. If it helps your stream, the best thanks is telling another streamer about it. Have a great LIVE!"),
                20, 52, 594, 90);

        // language
        UI.Lbl(p, L.T("Language"), 38, 292, 300, 11f, UI.Text, FontStyle.Bold);
        ComboBox lang = UI.Combo(p, 38, 324, 300);
        string detected = L.Detect();
        lang.Items.Add(L.T("Automatic (Windows language)") + " - " + L.Names[Array.IndexOf(L.Codes, detected)]);
        lang.Items.AddRange(L.Names);
        lang.SelectedIndex = app.S.Language.Length == 0 ? 0 : Array.IndexOf(L.Codes, app.S.Language) + 1;
        lang.SelectedIndexChanged += delegate
        {
            if (loading) return;
            string choice = lang.SelectedIndex <= 0 ? "" : L.Codes[lang.SelectedIndex - 1];
            if (choice == app.S.Language) return;
            app.Ui(delegate { app.SetLanguage(choice); });   // rebuilds this window, so not from inside its own event
        };
        UI.Para(p, L.T("Translations can have small mistakes. Spotted one? Let us know and we'll fix it."), 350, 322, 320, 44);

        // quick start
        UI.Lbl(p, L.T("Get started"), 38, 384, 300, 11f, UI.Text, FontStyle.Bold);
        string[] steps =
        {
            L.T("1. Counters: make a counter (deaths, wins...) and give it hotkeys."),
            L.T("2. Overlay: copy the link and add it in LIVE Studio (Add source > Link)."),
            L.T("3. TikTok LIVE: connect with your free Euler Stream key."),
            L.T("4. Actions: let gifts, follows and likes play sounds or press keys in your game.")
        };
        for (int i = 0; i < steps.Length; i++) UI.Para(p, steps[i], 38, 416 + i * 30, 632, 26).ForeColor = UI.Text;

        LinkLabel issues = new LinkLabel();
        issues.Text = L.T("Report a problem or suggest a feature");
        issues.Font = UI.F(10f, FontStyle.Regular);
        issues.LinkColor = UI.Accent;
        issues.ActiveLinkColor = UI.Text;
        issues.Location = new Point(UI.D(38), UI.D(546));
        issues.AutoSize = true;
        issues.LinkClicked += delegate { try { Process.Start(IssuesUrl); } catch { } };
        p.Controls.Add(issues);
        return p;
    }
}
