// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RVA
{

    public static class AuroraConfigs
    {
        public const string PresetNodeName = "RVA_AURORA";
        public const string FileNodeName   = "RVA_SETTINGS";
        public const string LegacyNodeName = "AURORAGLOW_SETTINGS";
        public const string BodyNodeName   = "BODY";
        public const string DefaultBody    = "Kerbin";

        class Entry
        {
            public string         body;
            public bool           enabled = true;
            public AuroraSettings preset;
            public AuroraSettings current;
            public string         source;
        }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> order = new List<string>();
        static readonly string[] opmBodies = { "Neidon", "Sarnus", "Urlum", "Tekto" };
        static bool loaded;
        static bool uranusGenericMode;
        static AuroraSettings uranusGenericSettings;
        static bool urlumGenericMode;
        static AuroraSettings urlumGenericSettings;

        public static string FilePath => AuroraSettings.FilePath;
        public static bool   Loaded   => loaded;

        public static IList<string> Bodies
        {
            get
            {
                EnsureLoaded();
                var list = new List<string>();
                foreach (var b in order) if (entries[b].enabled) list.Add(b);
                return list;
            }
        }

        public static bool Has(string body)
        {
            EnsureLoaded();
            return body != null && entries.TryGetValue(body, out var e) && e.enabled;
        }

        public static AuroraSettings Get(string body)
        {
            EnsureLoaded();
            if (string.Equals(body, "Uranus", StringComparison.OrdinalIgnoreCase) && uranusGenericMode)
            {
                if (uranusGenericSettings == null) SetUranusGenericMode(true);
                if (uranusGenericSettings != null) return uranusGenericSettings;
            }
            if (string.Equals(body, "Urlum", StringComparison.OrdinalIgnoreCase) && urlumGenericMode)
            {
                if (urlumGenericSettings == null) SetUrlumGenericMode(true);
                if (urlumGenericSettings != null) return urlumGenericSettings;
            }
            return body != null && entries.TryGetValue(body, out var e) && e.enabled ? e.current : null;
        }

        public static bool IsOpmBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            foreach (string candidate in opmBodies)
                if (string.Equals(body, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static IEnumerable<string> OpmBodies => opmBodies;
        public static bool UranusGenericMode => uranusGenericMode;
        public static bool UrlumGenericMode => urlumGenericMode;

        public static bool SetUranusGenericMode(bool generic)
        {
            EnsureLoaded();
            if (!generic)
            {
                uranusGenericMode = false;
                uranusGenericSettings = null;
                return true;
            }

            if (!entries.TryGetValue("Uranus", out var uranus) || !uranus.enabled
                || !entries.TryGetValue("Neptune", out var neptune) || !neptune.enabled)
                return false;

            var genericSettings = uranus.current.Clone();
            genericSettings.CopyPerBodyFrom(neptune.current);
            genericSettings.CopyGlobalsFrom(uranus.current);
            genericSettings.baseActivity = uranus.current.baseActivity;
            genericSettings.stormActivity = uranus.current.stormActivity;
            genericSettings.averageQuietDuration = uranus.current.averageQuietDuration;
            genericSettings.daylightFade = uranus.current.daylightFade;
            genericSettings.spotMode = false;
            uranusGenericSettings = genericSettings;
            uranusGenericMode = true;
            return true;
        }

        public static bool SetUrlumGenericMode(bool generic)
        {
            EnsureLoaded();
            if (!generic)
            {
                urlumGenericMode = false;
                urlumGenericSettings = null;
                return true;
            }

            if (!entries.TryGetValue("Urlum", out var urlum) || !urlum.enabled
                || !entries.TryGetValue("Neidon", out var neidon) || !neidon.enabled)
                return false;

            var settings = urlum.current.Clone();
            settings.CopyAppearanceFrom(neidon.current);
            urlumGenericSettings = settings;
            urlumGenericMode = true;
            return true;
        }

        public static AuroraSettings Preset(string body)
        {
            EnsureLoaded();
            return body != null && entries.TryGetValue(body, out var e) ? e.preset : new AuroraSettings();
        }

        public static AuroraSettings Global
        {
            get
            {
                EnsureLoaded();
                if (entries.TryGetValue(DefaultBody, out var k)) return k.current;
                foreach (var b in order) return entries[b].current;
                return fallback ?? (fallback = new AuroraSettings());
            }
        }
        static AuroraSettings fallback;

        static bool EnsureLoaded()
        {
            if (!loaded) Load(out string rep);
            return true;
        }

        public static void Load(out string report)
        {
            entries.Clear(); order.Clear();
            uranusGenericMode = false;
            uranusGenericSettings = null;
            urlumGenericMode = false;
            urlumGenericSettings = null;
            AuroraCloudTops.Rescan();
            var sb = new StringBuilder();

            ConfigNode[] presets = null;
            try { if (GameDatabase.Instance != null) presets = GameDatabase.Instance.GetConfigNodes(PresetNodeName); }
            catch (Exception e) { sb.Append($"(GameDatabase unavailable: {e.GetType().Name}) "); }

            if (presets != null)
            {
                foreach (var n in presets)
                {
                    if (n == null) continue;
                    string body = n.GetValue("body") ?? n.GetValue("name");
                    if (string.IsNullOrEmpty(body)) continue;
                    body = body.Trim();
                    var e = GetOrAdd(body);
                    string en = n.GetValue("enabled");
                    if (en != null && bool.TryParse(en.Trim(), out bool enabled)) e.enabled = enabled;
                    e.preset.ApplyNode(n, globals: false, perBody: true, allowDevOnly: true, out int bad, out int dev);
                    e.source = (e.source == null ? "" : e.source + " + ") + $"{PresetNodeName}" + (bad > 0 ? $" ({bad} unparsable)" : "");
                }
            }
            if (entries.Count == 0)
            {
                GetOrAdd(DefaultBody).source = "built-in defaults (no RVA_AURORA node found)";
            }
            SortOrder();

            foreach (var e in entries.Values) e.current = e.preset.Clone();

            string path = FilePath;
            if (!File.Exists(path) && File.Exists(AuroraSettings.LegacyFilePath))
                path = AuroraSettings.LegacyFilePath;
            if (!File.Exists(path))
            {
                sb.Append($"no shared settings file at {path}, presets only");
            }
            else
            {
                ConfigNode root = null;
                try { root = ConfigNode.Load(path); } catch { }
                ConfigNode node = root != null ? (root.GetNode(FileNodeName) ?? root.GetNode(LegacyNodeName) ?? root) : null;
                if (node == null) sb.Append($"settings file unreadable: {path}, presets only");
                else
                {
                    int applied = 0, bad = 0, dev = 0;

                    foreach (var e in entries.Values) { applied += e.current.ApplyNode(node, true, false, false, out int b1, out int d1); bad += b1; dev += d1; }
                    sb.Append($"loaded {applied} value(s) from {path}")
                      .Append(bad > 0 ? $" ({bad} unparsable, skipped)" : "")
                      .Append(dev > 0 ? $" ({dev} dev-only key(s) ignored by this build)" : "");
                }
            }

            loaded = true;
            sb.Append("; bodies: ");
            bool first = true;
            foreach (var b in order)
            {
                var e = entries[b];
                sb.Append(first ? "" : ", ").Append(b).Append(e.enabled ? "" : " (disabled)");
                first = false;
            }
            report = sb.ToString();
        }

        static Entry GetOrAdd(string body)
        {
            if (!entries.TryGetValue(body, out var e))
            {
                e = new Entry { body = body, preset = new AuroraSettings() };
                entries[body] = e;
                order.Add(body);
            }
            return e;
        }

        static void SortOrder()
        {

            int i = order.FindIndex(b => string.Equals(b, DefaultBody, StringComparison.OrdinalIgnoreCase));
            if (i > 0) { string k = order[i]; order.RemoveAt(i); order.Insert(0, k); }
        }

        public static void SaveGlobals()
        {
            EnsureLoaded();
            var root = new ConfigNode();
            var node = root.AddNode(FileNodeName);
            Global.WriteNode(node, globals: true, perBody: false);
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            root.Save(path);
        }

        public static string WritePresetFile(string body)
        {
            EnsureLoaded();
            if (body == null || !entries.TryGetValue(body, out var e)) throw new ArgumentException("no such body: " + body);
            var root = new ConfigNode();
            var node = root.AddNode(PresetNodeName);
            node.AddValue("body", body);
            node.AddValue("enabled", e.enabled ? "true" : "false");
            e.current.WriteNode(node, globals: false, perBody: true);
            string dir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealisticVolumetricAurorae/Configs", PresetFolder(body));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, body + ".cfg");
            root.Save(path, "Realistic Volumetric Aurorae (RVA) - Copyright (c) 2026 HereComesTheBoom (\"Realistic Volumetric Aurorae\")\n"
                          + "SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)\n"
                          + body + " preset, written by RVA");

            e.preset.CopyPerBodyFrom(e.current);
            e.source = "written by RVA";
            return path;
        }

        static string PresetFolder(string body)
        {
            switch (body.ToLowerInvariant())
            {
                case "kerbin": case "duna": case "eve": case "jool": case "laythe":
                    return "Stock";
                case "earth": case "jupiter": case "saturn": case "uranus": case "neptune":
                    return "Sol";
                case "neidon": case "sarnus": case "urlum": case "tekto":
                    return "OPM";
                case "vespin": case "teshyr": case "glacian": case "iores":
                    return Path.Combine("KSS2", "Aethera");
                case "grav":
                    return Path.Combine("KSS2", "Kerr");
                case "alva": case "blalo": case "elno": case "ilve": case "kaith": case "novin":
                    return Path.Combine("KSS2", "Novakirbani");
                case "brumal": case "cald": case "fumar": case "gloam": case "rime": case "tirmun":
                    return Path.Combine("KSS2", "Novakirbani", "Proksimus");
                case "hetoo": case "vael":
                    return Path.Combine("KSS2", "Orbel");
                case "hydron":
                    return Path.Combine("KSS2", "Seereon");
                case "sobek":
                    return Path.Combine("CosmicSerenity", "Anubis");
                case "erebus": case "icarus": case "poseidon":
                    return Path.Combine("CosmicSerenity", "Atlas");
                case "caelus": case "enmy": case "folie": case "iris":
                    return Path.Combine("CosmicSerenity", "Hades");
                case "zephyra":
                    return Path.Combine("CosmicSerenity", "KerbolExpansion");
                case "hai": case "hundun": case "laozi": case "lingbao": case "qi": case "shan": case "yang":
                    return Path.Combine("CosmicSerenity", "Yunxiao");
                case "bis": case "donk": case "dorau": case "gurdamma": case "lapat": case "ovin":
                    return Path.Combine("PromisedWorlds", "Debdeb");
                case "glumo": case "merbel":
                    return Path.Combine("PromisedWorlds", "Tuun");
                default:
                    return "Other";
            }
        }

        public static void ResetToPreset(string body)
        {
            EnsureLoaded();
            if (body != null && entries.TryGetValue(body, out var e)) e.current.CopyPerBodyFrom(e.preset);
        }

        public static void SyncGlobals(AuroraSettings from)
        {
            EnsureLoaded();
            foreach (var e in entries.Values) if (!ReferenceEquals(e.current, from)) e.current.CopyGlobalsFrom(from);
        }

        public static string Describe()
        {
            EnsureLoaded();
            var sb = new StringBuilder();
            foreach (var b in order)
            {
                var e = entries[b];
                sb.Append("  ").Append(b).Append(e.enabled ? "" : " (disabled)").Append(": ").Append(e.source ?? "defaults").Append('\n');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
