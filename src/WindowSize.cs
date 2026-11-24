// ClutchKit - the settings window's size: it can be resized and maximized, remembers its size, and
// (Settings > "Scale text with the window") draws text and layout bigger or smaller to fit the room.
// The pages are laid out for 900 x 660; a bigger or smaller window builds them again at a scale
// (UI.Zoom) once you let go of the window's edge.

using System;
using System.Drawing;
using System.Windows.Forms;

public partial class MainForm
{
    const int BaseW = 900, BaseH = 660;

    // a rebuild for a new size uses the size the window had (also when maximized)
    static Size nextClientSize = Size.Empty;
    FormWindowState lastState = FormWindowState.Normal;
    bool sizeReady;

    void SetUpWindowSize()
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        Size normal = app.S.WindowW > 0 && app.S.WindowH > 0
            ? new Size(UI.D0(app.S.WindowW), UI.D0(app.S.WindowH))
            : new Size(UI.D0(BaseW), UI.D0(BaseH));
        if (probe)
        {
            ClientSize = new Size(UI.D0(BaseW), UI.D0(BaseH));   // the layout check always uses the normal size
            return;
        }
        Size zoomFrom = nextClientSize.IsEmpty ? normal : nextClientSize;
        nextClientSize = Size.Empty;
        UI.Zoom = ZoomFor(zoomFrom);
        MinimumSize = new Size(UI.D0(560), UI.D0(420));
        ClientSize = normal;
        if (app.S.WindowMax)
        {
            WindowState = FormWindowState.Maximized;
            lastState = FormWindowState.Maximized;
        }
        ResizeEnd += delegate { AfterResize(); };
        SizeChanged += delegate { if (WindowState != lastState) AfterResize(); };   // maximize / restore
        Shown += delegate { sizeReady = true; AfterResize(); };
    }

    float ZoomFor(Size client)
    {
        if (!app.S.ScaleText) return 1f;
        float z = Math.Min(client.Width / (float)UI.D0(BaseW), client.Height / (float)UI.D0(BaseH));
        z = (float)Math.Round(z * 20) / 20;   // steps of 5 %
        return Math.Max(0.8f, Math.Min(2f, z));
    }

    // Remembers the size, and builds the window again if the text scale should change.
    void AfterResize()
    {
        if (!sizeReady || IsDisposed || WindowState == FormWindowState.Minimized) return;
        lastState = WindowState;
        bool max = WindowState == FormWindowState.Maximized;
        bool changed = app.S.WindowMax != max;
        app.S.WindowMax = max;
        if (!max)
        {
            int w = UI.Logical(ClientSize.Width), h = UI.Logical(ClientSize.Height);
            changed |= w != app.S.WindowW || h != app.S.WindowH;
            app.S.WindowW = w;
            app.S.WindowH = h;
        }
        if (changed) app.SaveSettingsSoon();
        if (Math.Abs(ZoomFor(ClientSize) - UI.Zoom) < 0.01f) return;
        nextClientSize = ClientSize;
        BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) app.RebuildMain(); });
    }
}
