// ClutchKit - the "Debug" page: detailed logging, the debug report, a live status, and a layout check
// that builds the window in every language, looks for text that is cut off or runs into something,
// and saves a screenshot of every page (debug\layout\).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

public partial class MainForm
{
    static readonly string[] PageFiles = { "1-general", "2-counters", "3-widgets", "4-actions", "5-tiktok", "6-twitch", "7-overlay", "8-obs", "9-settings", "10-debug" };
    static Font mono;

    Timer debugTimer;
    TextBox debugStatus;

    Panel BuildDebugPage()
    {
        Panel p = NewPage(L.T("Debug"), L.T("Something not working? This page collects what's needed to find the problem. Nothing is sent anywhere - you decide what to share."));

        CheckBox verbose = UI.Check(p, L.T("Detailed logging (debug mode)"), 38, 124, app.S.Debug);
        verbose.Font = UI.F(11f, FontStyle.Bold);
        verbose.CheckedChanged += delegate
        {
            if (loading) return;
            app.S.Debug = verbose.Checked;
            Diag.SetVerbose(verbose.Checked);
            Changed();
        };
        UI.Para(p, L.T("Writes everything ClutchKit does to debug-log.txt, including the first TikTok messages of each kind (they contain viewer names). Turn it on, make the problem happen, then save a report."),
                38, 156, 632, 62);

        Button save = UI.Btn(p, L.T("Save debug report"), 38, 226, 230, 38, true);
        Button folder = UI.Btn(p, L.T("Open debug folder"), 280, 226, 210, 38, false);
        Label saved = UI.Para(p, "", 38, 270, 632, 24);
        save.Click += delegate
        {
            string error;
            string path = SaveReport(out error);
            if (path == null)
            {
                saved.Text = L.F("Couldn't save the report: {0}", error);
                saved.ForeColor = UI.Warn;
                return;
            }
            try { Clipboard.SetText(File.ReadAllText(path)); } catch { }
            saved.Text = L.T("Saved and copied - paste it in your bug report or chat.");
            saved.ForeColor = UI.Good;
            ShowInFolder(path);
        };
        folder.Click += delegate
        {
            try { Directory.CreateDirectory(Diag.Folder); Process.Start("explorer.exe", "\"" + Diag.Folder + "\""); } catch { }
        };

        Button layout = UI.Btn(p, L.T("Check layout in all languages"), 38, 304, 340, 38, false);
        Label layoutLbl = UI.Para(p, "", 38, 348, 632, 44);
        layout.Click += delegate
        {
            layout.Enabled = false;
            layoutLbl.Text = L.T("Checking...");
            layoutLbl.ForeColor = UI.Warn;
            layoutLbl.Refresh();
            int problems;
            try { problems = CheckAllLayouts(); }
            finally { if (!layout.IsDisposed) layout.Enabled = true; }
            layoutLbl.Text = L.F("Layout: {0} possible problem(s) in {1} languages. Screenshots and details are in the debug folder.", problems, L.Codes.Length);
            layoutLbl.ForeColor = problems == 0 ? UI.Good : UI.Warn;
        };

        UI.Lbl(p, L.T("Live status"), 38, 404, 300, 11f, UI.Text, FontStyle.Bold);
        if (mono == null) mono = new Font("Consolas", 9f);
        debugStatus = new TextBox();
        debugStatus.Multiline = true;
        debugStatus.ReadOnly = true;
        debugStatus.WordWrap = true;
        debugStatus.ScrollBars = ScrollBars.Vertical;
        debugStatus.BackColor = UI.Input;
        debugStatus.ForeColor = UI.Text;
        debugStatus.BorderStyle = BorderStyle.None;
        debugStatus.Font = mono;
        debugStatus.Location = new Point(UI.D(38), UI.D(432));
        debugStatus.Size = new Size(UI.D(632), UI.D(170));
        p.Controls.Add(debugStatus);

        LinkLabel issues = new LinkLabel();
        issues.Text = L.T("Report a problem or suggest a feature");
        issues.Font = UI.F(10f, FontStyle.Regular);
        issues.LinkColor = UI.Accent;
        issues.ActiveLinkColor = UI.Text;
        issues.Location = new Point(UI.D(38), UI.D(614));
        issues.AutoSize = true;
        issues.LinkClicked += delegate { try { Process.Start(IssuesUrl); } catch { } };
        p.Controls.Add(issues);

        // the live status, only while this page is open
        debugTimer = new Timer();
        debugTimer.Interval = 1000;
        debugTimer.Tick += delegate
        {
            if (!p.Visible || !Visible || probe) return;
            string s = Diag.Status(app).Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            if (debugStatus.Text != s) debugStatus.Text = s;
        };
        debugTimer.Start();
        return p;
    }

    string SaveReport(out string error)
    {
        try
        {
            Directory.CreateDirectory(Diag.Folder);
            string path = Path.Combine(Diag.Folder, "report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(path, Diag.Report(app), new UTF8Encoding(false));
            error = null;
            return path;
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    static void ShowInFolder(string path)
    {
        try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
    }

    // ---------------- layout check ----------------

    // Builds the window once per language (out of sight), checks every page and saves screenshots.
    // Returns how many possible problems it found; the details go to debug\layout-check.txt and the report.
    int CheckAllLayouts()
    {
        string dir = Path.Combine(Diag.Folder, "layout");
        try
        {
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        }
        catch { }
        string original = L.Code;
        float zoom = UI.Zoom;   // the check always builds the window at 100 %
        StringBuilder report = new StringBuilder();
        report.AppendLine("Layout check " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", " + UI.D(100) + "% scaling, " + app.S.Rules.Count + " actions");
        int total = 0;
        Cursor before = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            UI.Zoom = 1f;
            foreach (string code in L.Codes)
            {
                L.Use(code);
                MainForm f = null;
                try
                {
                    f = new MainForm(app, true);
                    f.Show();
                    List<string> found = f.ProbeLayout(dir, code);
                    total += found.Count;
                    report.AppendLine("[" + code + "] " + (found.Count == 0 ? "OK" : found.Count + " possible problem(s)"));
                    foreach (string s in found) report.AppendLine("  " + s);
                }
                catch (Exception ex)
                {
                    report.AppendLine("[" + code + "] check failed: " + ex.Message);
                    Diag.Error("layout", code + ": " + ex);
                }
                finally
                {
                    if (f != null) { f.AllowClose = true; f.Close(); f.Dispose(); }
                }
            }
        }
        finally
        {
            L.Use(original);
            UI.Zoom = zoom;
            Cursor.Current = before;
        }
        Diag.LastLayoutCheck = report.ToString();
        try { File.WriteAllText(Path.Combine(Diag.Folder, "layout-check.txt"), report.ToString(), new UTF8Encoding(false)); } catch { }
        return total;
    }

    // On a window made for the layout check: every page, fully unrolled, checked and photographed.
    List<string> ProbeLayout(string dir, string code)
    {
        List<string> found = new List<string>();
        CheckBoxes(nav, "Menu", found);
        try
        {
            using (Bitmap bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(Path.Combine(dir, code + "-0-window.png"), ImageFormat.Png);
            }
        }
        catch (Exception ex) { Diag.Error("layout", "window screenshot: " + ex.Message); }

        for (int i = 0; i < pages.Count; i++)
        {
            SelectPage(i);
            Panel page = pages[i];
            string name = navButtons[i].Text.Trim();
            // show the whole page at once instead of scrolling
            page.AutoScroll = false;
            page.Dock = DockStyle.None;
            int bottom = content.ClientSize.Height;
            foreach (Control c in page.Controls) if (c.Visible) bottom = Math.Max(bottom, c.Bottom + UI.D(16));
            page.Size = new Size(content.ClientSize.Width, Math.Min(bottom, 6000));
            CheckBoxes(page, name, found);
            try
            {
                using (Bitmap bmp = new Bitmap(page.Width, page.Height))
                {
                    page.DrawToBitmap(bmp, new Rectangle(0, 0, page.Width, page.Height));
                    string file = i < PageFiles.Length ? PageFiles[i] : (i + 1).ToString();
                    bmp.Save(Path.Combine(dir, code + "-" + file + ".png"), ImageFormat.Png);
                }
            }
            catch (Exception ex) { Diag.Error("layout", name + " screenshot: " + ex.Message); }
            page.Dock = DockStyle.Fill;
            page.AutoScroll = true;
        }

        // the same problem on many folded actions: list it once
        Dictionary<string, int> seen = new Dictionary<string, int>();
        List<string> order = new List<string>();
        foreach (string s in found) { int n; if (seen.TryGetValue(s, out n)) seen[s] = n + 1; else { seen[s] = 1; order.Add(s); } }
        List<string> result = new List<string>();
        foreach (string s in order) result.Add(seen[s] > 1 ? s + "  (x" + seen[s] + ")" : s);
        return result;
    }

    static string Short(string t)
    {
        t = (t ?? "").Replace("\r", " ").Replace("\n", " ");
        return t.Length > 60 ? t.Substring(0, 57) + "..." : t;
    }

    // Looks at the visible controls in one container (and the panels inside it): text that doesn't fit,
    // controls that run past the edge, and neighbours whose text runs into each other.
    void CheckBoxes(Control parent, string where, List<string> found)
    {
        List<Control> list = new List<Control>();
        List<Rectangle> boxes = new List<Rectangle>();
        foreach (Control c in parent.Controls)
        {
            if (!c.Visible || c.Width <= 0 || c.Height <= 0) continue;
            Rectangle box = c.Bounds;
            string t = c.Text ?? "";
            Label l = c as Label;
            Button b = c as Button;

            if (l != null && !l.AutoSize && t.Length > 0)
            {
                if (l.AutoEllipsis)
                {
                    // shortened with "..." on purpose (action summaries, topic names); only the used part counts
                    Size one = l.GetPreferredSize(Size.Empty);
                    box = new Rectangle(l.Left, l.Top, Math.Min(l.Width, one.Width), Math.Min(l.Height, one.Height));
                }
                else
                {
                    Size need = l.GetPreferredSize(new Size(l.ClientSize.Width, 0));
                    if (need.Height > l.ClientSize.Height + 2)
                        found.Add(where + ": text \"" + Short(t) + "\" is cut off at the bottom (needs " + need.Height + " px, has " + l.ClientSize.Height + ")");
                    else if (need.Width > l.ClientSize.Width + 2)
                        found.Add(where + ": text \"" + Short(t) + "\" is too wide (needs " + need.Width + " px, has " + l.ClientSize.Width + ")");
                    box = new Rectangle(l.Left, l.Top, Math.Min(l.Width, need.Width), Math.Min(l.Height, need.Height));
                }
            }
            else if (b != null && t.Length > 1)
            {
                int text = TextRenderer.MeasureText(t, b.Font).Width, room = b.ClientSize.Width - UI.D(6);
                if (text > room) found.Add(where + ": button \"" + Short(t) + "\" is too narrow (text needs " + text + " px, has " + room + ")");
            }
            if (c.Right > parent.ClientSize.Width + 1)
                found.Add(where + ": \"" + Short(t.Length > 0 ? t : c.GetType().Name) + "\" runs past the right edge by " + (c.Right - parent.ClientSize.Width) + " px");

            list.Add(c);
            boxes.Add(box);
            if (c is Panel && !(c is PreviewPanel) && c.Controls.Count > 0) CheckBoxes(c, where, found);
        }
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (boxes[i].IntersectsWith(boxes[j]))
                    found.Add(where + ": \"" + Short(TextOf(list[i])) + "\" overlaps \"" + Short(TextOf(list[j])) + "\"");
    }

    static string TextOf(Control c) { return (c.Text ?? "").Length > 0 ? c.Text : c.GetType().Name; }
}
