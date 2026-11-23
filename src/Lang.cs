// ClutchKit - languages.
// ----------------------------------------------------------------------------------
// Every text the user sees goes through L.T("English text") (or L.F for texts with {0} parts).
// The English text is the key; Translations.cs has one row per text with every language.
// A text that has no row (yet) shows in English, so nothing ever breaks.
// L.N("text") only marks a text for translation where it's stored (arrays); it's translated
// with L.T(...) where it's shown.

using System;
using System.Collections.Generic;
using System.Globalization;

public static partial class L
{
    // Order of the columns in Translations.cs
    public static readonly string[] Codes = { "en", "es", "pt", "id", "tr", "de", "fr", "vi" };
    public static readonly string[] Names = { "English", "Español", "Português (Brasil)", "Bahasa Indonesia", "Türkçe", "Deutsch", "Français", "Tiếng Việt" };

    static Dictionary<string, string> current = new Dictionary<string, string>();
    static string code = "en";

    public static string Code { get { return code; } }

    // "" = follow the Windows language
    public static string Resolve(string setting)
    {
        if (!string.IsNullOrEmpty(setting) && Array.IndexOf(Codes, setting) >= 0) return setting;
        return Detect();
    }

    public static string Detect()
    {
        try
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (two == "fil" || two == "tl") two = "en";
            if (two == "ms") two = "id";   // Malay readers understand Indonesian best of what we have
            return Array.IndexOf(Codes, two) >= 0 ? two : "en";
        }
        catch { return "en"; }
    }

    public static void Use(string languageCode)
    {
        int col = Array.IndexOf(Codes, languageCode);
        if (col < 0) col = 0;
        Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (col > 0)
        {
            foreach (string[] row in Rows())
                if (row.Length == Codes.Length && !string.IsNullOrEmpty(row[col]) && !d.ContainsKey(row[0])) d[row[0]] = row[col];
        }
        current = d;   // swapped in one go, so other threads never see a half-built table
        code = Codes[col];
    }

    public static string T(string english)
    {
        if (english == null) return "";
        string s;
        return current.TryGetValue(english, out s) ? s : english;
    }

    public static string F(string english, params object[] args)
    {
        string pattern = T(english);
        try { return string.Format(pattern, args); }
        catch (FormatException)
        {
            try { return string.Format(english, args); } catch { return english; }
        }
    }

    public static string N(string english) { return english; }

    public static string[] All(string[] english)
    {
        string[] a = new string[english.Length];
        for (int i = 0; i < a.Length; i++) a[i] = T(english[i]);
        return a;
    }

    static string[] R(string en, string es, string pt, string id, string tr, string de, string fr, string vi)
    {
        return new[] { en, es, pt, id, tr, de, fr, vi };
    }
}
