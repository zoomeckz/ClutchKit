// ClutchKit - the TikTok, Twitch and OBS Studio logos for the menu, drawn from their official
// shapes (Simple Icons, CC0: https://simpleicons.org). The logos are trademarks of their owners and
// are only used to show which page belongs to which service.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

public static class Brand
{
    const string TikTokPath = "M12.525.02c1.31-.02 2.61-.01 3.91-.02.08 1.53.63 3.09 1.75 4.17 1.12 1.11 2.7 1.62 4.24 1.79v4.03c-1.44-.05-2.89-.35-4.2-.97-.57-.26-1.1-.59-1.62-.93-.01 2.92.01 5.84-.02 8.75-.08 1.4-.54 2.79-1.35 3.94-1.31 1.92-3.58 3.17-5.91 3.21-1.43.08-2.86-.31-4.08-1.03-2.02-1.19-3.44-3.37-3.65-5.71-.02-.5-.03-1-.01-1.49.18-1.9 1.12-3.72 2.58-4.96 1.66-1.44 3.98-2.13 6.15-1.72.02 1.48-.04 2.96-.04 4.44-.99-.32-2.15-.23-3.02.37-.63.41-1.11 1.04-1.36 1.75-.21.51-.15 1.07-.14 1.61.24 1.64 1.82 3.02 3.5 2.87 1.12-.01 2.19-.66 2.77-1.61.19-.33.4-.67.41-1.06.1-1.79.06-3.57.07-5.36.01-4.03-.01-8.05.02-12.07z";
    const string ObsPath = "M12,24C5.383,24,0,18.617,0,12S5.383,0,12,0s12,5.383,12,12S18.617,24,12,24z M12,1.109 C5.995,1.109,1.11,5.995,1.11,12C1.11,18.005,5.995,22.89,12,22.89S22.89,18.005,22.89,12C22.89,5.995,18.005,1.109,12,1.109z M6.182,5.99c0.352-1.698,1.503-3.229,3.05-3.996c-0.269,0.273-0.595,0.483-0.844,0.78c-1.02,1.1-1.48,2.692-1.199,4.156 c0.355,2.235,2.455,4.06,4.732,4.028c1.765,0.079,3.485-0.937,4.348-2.468c1.848,0.063,3.645,1.017,4.7,2.548 c0.54,0.799,0.962,1.736,0.991,2.711c-0.342-1.295-1.202-2.446-2.375-3.095c-1.135-0.639-2.529-0.802-3.772-0.425 c-1.56,0.448-2.849,1.723-3.293,3.293c-0.377,1.25-0.216,2.628,0.377,3.772c-0.825,1.429-2.315,2.449-3.932,2.756 c-1.244,0.261-2.551,0.059-3.709-0.464c1.036,0.302,2.161,0.355,3.191-0.011c1.381-0.457,2.522-1.567,3.024-2.935 c0.556-1.49,0.345-3.261-0.591-4.54c-0.7-1.007-1.803-1.717-3.002-1.969c-0.38-0.068-0.764-0.098-1.148-0.134 c-0.611-1.231-0.834-2.66-0.528-3.996L6.182,5.99z";
    const string TwitchPath = "M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z";

    public const string TikTok = "tiktok", Twitch = "twitch", Obs = "obs";
    public static readonly Color TikTokCyan = Color.FromArgb(37, 244, 238), TikTokRed = Color.FromArgb(254, 44, 85);
    public static readonly Color TwitchPurple = Color.FromArgb(145, 70, 255);

    static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

    // The logo as a size x size picture (null key = an empty picture, so menu texts line up).
    public static Bitmap Icon(string key, int size)
    {
        string k = (key ?? "") + "|" + size;
        Bitmap b;
        if (cache.TryGetValue(k, out b)) return b;
        b = new Bitmap(size, size);
        if (key != null)
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float pad = size * 0.06f, scale = (size - 2 * pad) / 24f;
                g.TranslateTransform(pad, pad);
                g.ScaleTransform(scale, scale);
                switch (key)
                {
                    case TikTok:
                        using (GraphicsPath p = SvgPath.Parse(TikTokPath))
                        {
                            // the official look: a cyan and a red copy behind the white note
                            Fill(g, p, TikTokCyan, -0.7f, -0.7f);
                            Fill(g, p, TikTokRed, 0.7f, 0.7f);
                            Fill(g, p, Color.White, 0, 0);
                        }
                        break;
                    case Twitch:
                        using (GraphicsPath p = SvgPath.Parse(TwitchPath)) Fill(g, p, TwitchPurple, 0, 0);
                        break;
                    case Obs:
                        using (GraphicsPath p = SvgPath.Parse(ObsPath)) Fill(g, p, Color.White, 0, 0);
                        break;
                }
            }
        cache[k] = b;
        return b;
    }

    static void Fill(Graphics g, GraphicsPath p, Color c, float dx, float dy)
    {
        GraphicsState s = g.Save();
        g.TranslateTransform(dx, dy);
        using (SolidBrush br = new SolidBrush(c)) g.FillPath(br, p);
        g.Restore(s);
    }
}

// Reads SVG path data ("M12 0c1.3-.02 ...") into a GraphicsPath: M, L, H, V, C, S, Q, T, Z and
// their lower-case (relative) forms - everything the logos above use.
public static class SvgPath
{
    public static GraphicsPath Parse(string d)
    {
        GraphicsPath path = new GraphicsPath(FillMode.Winding);
        List<string> t = Tokens(d);
        int i = 0;
        char cmd = ' ', prev = ' ';
        PointF cur = PointF.Empty, start = PointF.Empty, ctl = PointF.Empty;
        while (i < t.Count)
        {
            if (char.IsLetter(t[i][0])) cmd = t[i++][0];
            else if (cmd == 'M') cmd = 'L';   // more numbers after a move are lines
            else if (cmd == 'm') cmd = 'l';
            bool rel = char.IsLower(cmd);
            PointF o = rel ? cur : PointF.Empty;
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    cur = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                    start = cur;
                    path.StartFigure();
                    break;
                case 'L':
                    {
                        PointF p = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        path.AddLine(cur, p);
                        cur = p;
                        break;
                    }
                case 'H':
                    {
                        PointF p = new PointF((rel ? cur.X : 0) + N(t, ref i), cur.Y);
                        path.AddLine(cur, p);
                        cur = p;
                        break;
                    }
                case 'V':
                    {
                        PointF p = new PointF(cur.X, (rel ? cur.Y : 0) + N(t, ref i));
                        path.AddLine(cur, p);
                        cur = p;
                        break;
                    }
                case 'C':
                    {
                        PointF c1 = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        PointF c2 = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        PointF p = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        path.AddBezier(cur, c1, c2, p);
                        ctl = c2;
                        cur = p;
                        break;
                    }
                case 'S':
                    {
                        char pu = char.ToUpperInvariant(prev);
                        PointF c1 = pu == 'C' || pu == 'S' ? new PointF(2 * cur.X - ctl.X, 2 * cur.Y - ctl.Y) : cur;
                        PointF c2 = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        PointF p = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        path.AddBezier(cur, c1, c2, p);
                        ctl = c2;
                        cur = p;
                        break;
                    }
                case 'Q':
                case 'T':
                    {
                        char pu = char.ToUpperInvariant(prev);
                        PointF q = char.ToUpperInvariant(cmd) == 'Q' ? new PointF(o.X + N(t, ref i), o.Y + N(t, ref i)) :
                                   pu == 'Q' || pu == 'T' ? new PointF(2 * cur.X - ctl.X, 2 * cur.Y - ctl.Y) : cur;
                        PointF p = new PointF(o.X + N(t, ref i), o.Y + N(t, ref i));
                        path.AddBezier(cur, new PointF(cur.X + 2f / 3 * (q.X - cur.X), cur.Y + 2f / 3 * (q.Y - cur.Y)),
                                       new PointF(p.X + 2f / 3 * (q.X - p.X), p.Y + 2f / 3 * (q.Y - p.Y)), p);
                        ctl = q;
                        cur = p;
                        break;
                    }
                case 'Z':
                    path.CloseFigure();
                    cur = start;
                    break;
                default:
                    i++;   // not used by the logos; skip
                    break;
            }
            prev = cmd;
        }
        return path;
    }

    static float N(List<string> t, ref int i)
    {
        if (i >= t.Count || char.IsLetter(t[i][0])) return 0;
        return float.Parse(t[i++], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // "1.53.63-.02" -> "1.53", ".63", "-.02"
    static List<string> Tokens(string d)
    {
        List<string> list = new List<string>();
        int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsLetter(c) && c != 'e' && c != 'E') { list.Add(c.ToString()); i++; continue; }
            if (c == ',' || char.IsWhiteSpace(c)) { i++; continue; }
            int s = i;
            if (c == '-' || c == '+') i++;
            bool dot = false;
            while (i < d.Length)
            {
                char x = d[i];
                if (char.IsDigit(x)) i++;
                else if (x == '.' && !dot) { dot = true; i++; }
                else if ((x == 'e' || x == 'E') && i + 1 < d.Length) { i++; if (d[i] == '-' || d[i] == '+') i++; }
                else break;
            }
            if (i == s) { i++; continue; }   // a stray character
            list.Add(d.Substring(s, i - s));
        }
        return list;
    }
}
