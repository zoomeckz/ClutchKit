// ClutchKit - the "Twitch" page of the settings window: the channel to read (chat, bits, subs,
// raids and the viewer count) and its connection status.

using System;
using System.Drawing;
using System.Windows.Forms;

public partial class MainForm
{
    Button twitchBtn;
    Label twitchStatus;

    Panel BuildTwitchPage()
    {
        Panel p = NewPage(L.T("Twitch"), L.T("Streaming on Twitch too? ClutchKit reads your Twitch chat, bits, subs and raids for the alerts overlay and chat commands, and your Twitch viewer count. No login needed."));
        Settings s = app.S;

        UI.FieldLabel(p, L.T("Your Twitch channel"), 38, 126);
        TextBox channel = new TextBox();
        UI.Style(channel);
        channel.BorderStyle = BorderStyle.FixedSingle;
        channel.Text = s.TwitchChannel;
        channel.Location = new Point(UI.D(230), UI.D(126));
        channel.Size = new Size(UI.D(260), UI.D(28));
        channel.TextChanged += delegate { app.S.TwitchChannel = TwitchChat.CleanChannel(channel.Text); Changed(); };
        p.Controls.Add(channel);
        Hint(channel, "twitch.tv/...");

        CheckBox auto = UI.Check(p, L.T("Connect automatically when ClutchKit starts"), 38, 170, s.TwitchAuto);
        auto.CheckedChanged += delegate { app.S.TwitchAuto = auto.Checked; Changed(); };

        twitchBtn = UI.Btn(p, L.T("Connect"), 38, 210, 180, 40, true);
        twitchBtn.Click += delegate
        {
            if (app.Twitch.Running) app.Twitch.Stop();
            else app.Twitch.Start(app.S.TwitchChannel);
        };
        twitchStatus = UI.Para(p, "", 232, 210, 440, 44);

        UI.Para(p, L.T("What you get: chat commands (the same actions as TikTok chat), and bits, subs, gifted subs and raids on the alerts overlay (Widgets page). Follows need a Twitch login, so they don't show up."),
                38, 266, 632, 62);
        UI.Para(p, L.T("ClutchKit reads the chat like a logged-out viewer and never writes in it. The viewer count comes from decapi.me, a free service that only gets your channel name."),
                38, 334, 632, 44);
        UI.Lbl(p, L.T("Activity"), 38, 392, 300, 11f, UI.Text, FontStyle.Bold);
        MakeLog(p, 38, 422, 632, 200);

        app.Twitch.StatusChanged += OnTwitchStatus;
        OnTwitchStatus(null, EventArgs.Empty);
        return p;
    }

    void OnTwitchStatus(object sender, EventArgs e)
    {
        TwitchChat t = app.Twitch;
        twitchBtn.Text = t.Running ? L.T("Disconnect") : L.T("Connect");
        twitchStatus.Text = t.Detail;
        switch (t.Current)
        {
            case TwitchChat.State.Connected: twitchStatus.ForeColor = UI.Good; break;
            case TwitchChat.State.Error: twitchStatus.ForeColor = UI.Accent; break;
            case TwitchChat.State.Off: twitchStatus.ForeColor = UI.Muted; break;
            default: twitchStatus.ForeColor = UI.Warn; break;
        }
    }
}
