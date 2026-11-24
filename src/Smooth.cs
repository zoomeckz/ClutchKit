// ClutchKit - a settings page that scrolls smoothly: the mouse wheel glides to where it should be
// instead of jumping. Settings > "Smooth scrolling" turns it off (then it scrolls like Windows does).

using System;
using System.Drawing;
using System.Windows.Forms;

public class SmoothPanel : Panel
{
    public static bool Smooth = true;

    readonly Timer anim = new Timer();
    int target;

    public SmoothPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        anim.Interval = 15;
        anim.Tick += delegate { Step(); };
    }

    int Max { get { return Math.Max(0, DisplayRectangle.Height - ClientSize.Height); } }
    int Pos { get { return -AutoScrollPosition.Y; } }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Smooth || !VerticalScroll.Visible) { anim.Stop(); base.OnMouseWheel(e); return; }
        HandledMouseEventArgs h = e as HandledMouseEventArgs;
        if (h != null) h.Handled = true;
        if (!anim.Enabled) target = Pos;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines <= 0) lines = 3;
        target = Math.Max(0, Math.Min(Max, target - e.Delta * lines * UI.D(20) / 120));
        if (!anim.Enabled) anim.Start();
    }

    // Moves a part of the way each tick: fast at first, gently at the end.
    void Step()
    {
        int pos = Pos, diff = target - pos;
        if (Math.Abs(diff) <= 1 || IsDisposed)
        {
            anim.Stop();
            if (!IsDisposed && diff != 0) AutoScrollPosition = new Point(-AutoScrollPosition.X, target);
            return;
        }
        int move = (int)Math.Round(diff * 0.22);
        if (move == 0) move = Math.Sign(diff);
        AutoScrollPosition = new Point(-AutoScrollPosition.X, pos + move);
        if (Pos == pos) anim.Stop();   // can't move further (e.g. the page got shorter)
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }
}
