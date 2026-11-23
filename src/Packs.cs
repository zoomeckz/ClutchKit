// ClutchKit - action packs: topics with their actions and sound files in one .ckpack file, to share
// with other streamers (e.g. "my Minecraft actions") or to keep as a backup.
// ----------------------------------------------------------------------------------
// A pack is a text file in the settings.ini format: groups= / group.<id>.* / rules= / rule.<id>.*,
// plus the sound files (sound.<n>.name and sound.<n>.data, base64). Sounds are referenced as
// "pack:<name>". A counter is matched by its name, since counter ids differ between PCs.
// Imported topics start switched off, so you can look at what they do before they run.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class Packs
{
    const long MaxBytes = 100L * 1024 * 1024;   // a pack bigger than this is refused

    public static string SafeName(string name)
    {
        StringBuilder b = new StringBuilder();
        foreach (char c in name ?? "") b.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        string s = b.ToString().Trim().Trim('.');
        return s.Length > 0 ? (s.Length > 60 ? s.Substring(0, 60) : s) : "sound";
    }

    // Writes the topics to path. null = OK, else what went wrong.
    public static string Export(App app, List<RuleGroup> groups, string path)
    {
        try
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("# ClutchKit actions - import it in ClutchKit: Actions > Import...");
            b.AppendLine("clutchkit.pack=1");
            b.AppendLine("clutchkit.version=" + Program.Version);

            Dictionary<string, string> sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // pack name -> file
            List<string> gids = new List<string>(), rids = new List<string>();
            StringBuilder body = new StringBuilder();
            int gid = 0, rid = 0;
            foreach (RuleGroup g in groups)
            {
                gid++;
                gids.Add(gid.ToString());
                RuleGroup copy = new RuleGroup();
                copy.Id = gid;
                copy.Name = g.Name;
                copy.Enabled = g.Enabled;
                copy.Collapsed = false;
                copy.Write(body);
                foreach (GiftRule r in app.S.Rules)
                {
                    if (r.GroupId != g.Id) continue;
                    rid++;
                    rids.Add(rid.ToString());
                    GiftRule c = r.Clone();
                    c.Id = rid;
                    c.GroupId = gid;
                    if (c.SoundFile.Length > 0)
                    {
                        string name = SafeName(Path.GetFileName(c.SoundFile));
                        string have;
                        if (sounds.TryGetValue(name, out have) && !string.Equals(have, c.SoundFile, StringComparison.OrdinalIgnoreCase))
                            name = SafeName(Path.GetFileNameWithoutExtension(name) + "-" + rid + Path.GetExtension(name));
                        if (File.Exists(c.SoundFile)) sounds[name] = c.SoundFile;
                        c.SoundFile = "pack:" + name;
                    }
                    c.Write(body);
                    if (c.Kind == GiftRule.KindCounter)
                    {
                        Counter counter = app.S.Find(r.CounterId);
                        if (counter != null) body.AppendLine("rule." + rid + ".counterName=" + counter.DisplayName);
                    }
                }
            }
            b.AppendLine("groups=" + string.Join(",", gids.ToArray()));
            b.AppendLine("rules=" + string.Join(",", rids.ToArray()));
            b.Append(body);

            int n = 0;
            long total = 0;
            foreach (KeyValuePair<string, string> kv in sounds)
            {
                byte[] data = File.ReadAllBytes(kv.Value);
                total += data.Length;
                if (total > MaxBytes / 2) return L.T("the sound files are too big together (more than 50 MB)");
                n++;
                b.AppendLine("sound." + n + ".name=" + kv.Key);
                b.AppendLine("sound." + n + ".data=" + Convert.ToBase64String(data));
            }
            Settings.WriteFile(path, b.ToString());
            if (!File.Exists(path)) return L.T("the file couldn't be written");
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Adds the pack's topics (switched off) and copies its sounds into the sounds folder.
    // message = what happened, for the Actions page.
    public static bool Import(App app, string path, out string message)
    {
        Dictionary<string, string> kv;
        try
        {
            if (new FileInfo(path).Length > MaxBytes) { message = L.T("This file is too big to be a ClutchKit pack."); return false; }
            kv = Settings.ReadKv(File.ReadAllLines(path));
        }
        catch (Exception ex) { message = L.F("Couldn't read the file: {0}", ex.Message); return false; }
        if (!kv.ContainsKey("clutchkit.pack")) { message = L.T("This isn't a ClutchKit actions file (.ckpack)."); return false; }

        Settings s = app.S;
        // sounds first: pack name -> the copy in ClutchKit's sounds folder
        Dictionary<string, string> sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string soundsDir = Path.Combine(app.Folder, "sounds");
        for (int i = 1; i <= 1000; i++)
        {
            string name, data;
            if (!kv.TryGetValue("sound." + i + ".name", out name) || !kv.TryGetValue("sound." + i + ".data", out data)) break;
            name = SafeName(Path.GetFileName(name.Trim()));
            if (Array.IndexOf(SoundPlayer.Extensions, Path.GetExtension(name).ToLowerInvariant()) < 0) continue;   // only sound files
            try
            {
                byte[] bytes = Convert.FromBase64String(data.Trim());
                Directory.CreateDirectory(soundsDir);
                string dest = Path.Combine(soundsDir, name);
                if (File.Exists(dest) && new FileInfo(dest).Length != bytes.Length)
                    dest = Path.Combine(soundsDir, Path.GetFileNameWithoutExtension(name) + "-" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(name));
                if (!File.Exists(dest)) File.WriteAllBytes(dest, bytes);
                sounds[name] = dest;
            }
            catch (Exception ex) { Diag.Error("packs", "sound " + name + ": " + ex.Message); }
        }

        Dictionary<int, RuleGroup> groups = new Dictionary<int, RuleGroup>();
        List<string> names = new List<string>();
        foreach (int id in Settings.IdList(kv, "groups", Settings.MaxGroups))
        {
            if (s.Groups.Count >= Settings.MaxGroups) break;
            RuleGroup src = RuleGroup.Read(kv, id);
            RuleGroup g = s.NewGroup(src.Name);
            g.Enabled = false;   // look first, then switch it on
            g.Collapsed = false;
            groups[id] = g;
            names.Add(g.Name);
        }
        if (groups.Count == 0)
        {
            message = s.Groups.Count >= Settings.MaxGroups ? L.F("That's the maximum of {0} topics.", Settings.MaxGroups) : L.T("There are no topics in this file.");
            return false;
        }

        int added = 0, skipped = 0, noCounter = 0;
        foreach (int id in Settings.IdList(kv, "rules", Settings.MaxRules))
        {
            if (s.Rules.Count >= Settings.MaxRules) { skipped++; continue; }
            GiftRule r = GiftRule.Read(kv, id);
            RuleGroup g;
            if (!groups.TryGetValue(r.GroupId, out g)) { skipped++; continue; }
            r.Id = app.NextRuleId();
            r.GroupId = g.Id;
            r.Collapsed = true;
            if (r.SoundFile.StartsWith("pack:"))
            {
                string file;
                r.SoundFile = sounds.TryGetValue(SafeName(r.SoundFile.Substring(5)), out file) ? file : "";
            }
            else if (r.SoundFile.Length > 0 && !File.Exists(r.SoundFile)) r.SoundFile = "";
            if (r.Kind == GiftRule.KindCounter)
            {
                string want;
                r.CounterId = 0;
                if (kv.TryGetValue("rule." + id + ".countername", out want))
                    foreach (Counter c in s.Counters)
                        if (string.Equals(c.DisplayName, want.Trim(), StringComparison.CurrentCultureIgnoreCase)) { r.CounterId = c.Id; break; }
                if (r.CounterId == 0) noCounter++;
            }
            s.Rules.Add(r);
            added++;
        }
        app.ApplySettings();
        message = L.F("Imported {0} action(s) in: {1}. The new topics are off - check them, then tick \"On\".", added, string.Join(", ", names.ToArray()));
        if (skipped > 0) message += " " + L.F("{0} skipped (the maximum is {1}).", skipped, Settings.MaxRules);
        if (noCounter > 0) message += " " + L.F("{0} need a counter picked.", noCounter);
        app.Log(message);
        return true;
    }
}
