using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Campaign build window: pick a <see cref="CampaignCurveConfig"/>, a level range, and see
    /// what the curve asks of each slot and what the built levels take, for that range on one graph —
    /// both in seconds of a regular player at that level (× place factor), or, with the "Seconds of"
    /// switch, of the board itself (no factor).
    ///
    /// Performance: targets are computed once per "Show graph" click (never in OnGUI), the
    /// polyline is decimated to two points per pixel column (max/min per column, so peaks and
    /// breathers both survive) and cached until data or window size changes, and all drawing
    /// is immediate-mode GL — no per-frame allocations.
    /// </summary>
    public class CampaignBuilderWindow : EditorWindow
    {
        const int MaxRange = 50_000;

        // [SerializeField] on the settings fields — plain fields are wiped by every domain reload
        // (any script compile), which is what kept snapping the range back to 1–600.
        [SerializeField] CampaignCurveConfig config;
        [SerializeField] int from = 1;
        [SerializeField] int to = 600;
        bool graphFoldout = true;
        bool buildFoldout = true;
        const string SetsRoot = "Assets/Levels/Sets";
        string[] sets = { "Campaign" };
        [SerializeField] string setName = "Campaign";
        int setIdx;
        string SetName => setName;
        string OutputFolder => $"{SetsRoot}/{SetName}";
        [SerializeField] int seedBase = 1;
        [SerializeField] bool skipExisting = true;
        int setCount;    // consecutive levels from 1 in the selected set
        bool setGap;     // a level exists past the run → hole in the sequence
        [SerializeField] bool overrideOffTol = true;
        int offTolCount = -1;                       // -1 = needs recount
        (int, int, int, Object) offTolKey;          // (from, to, setIdx, config) the count was made for
        int threadCount = Mathf.Max(1, System.Environment.ProcessorCount - 1);

        // graph data, rebuilt only on Show graph: the curve's targets and the built levels, drawn
        // together on one plot; each line has a checkbox. null = that line has no data (no curve).
        int[] targets, built;
        byte[] roles, lessons;   // lessons: 0 none, 1-3 the stage of a trick lesson
        int dataFrom, maxSeconds;
        [SerializeField] bool showTargets = true, showBuilt = true;

        // The graph's unit. Player = the board's own seconds × the level's place factor (what a regular
        // player at that level takes — what the curve asks for). Board = the board's own seconds, as an
        // experienced player takes it, with the factor taken out. Switching needs no re-rating: the
        // board seconds and the factors are kept, and only the drawn numbers change.
        [SerializeField] bool withPlaceFactor = true;
        static readonly string[] UnitNames = { "Player at this level (× place factor)", "Board itself (no factor)" };
        float[] targetBoard, builtBoard, factors;   // board seconds (0 = no level) and each level's place factor

        // screen-space cache, rebuilt when data, checkboxes or rect changes
        Vector3[] targetLine, builtLine;
        Vector3[] milestones, lessonDots = new Vector3[0];   // lessonDots: z = half size
        Rect cachedRect;
        bool cacheStale = true;

        static Material lineMat;

        [MenuItem("QueensPuzzle/Campaign Builder")]
        static void Open() => GetWindow<CampaignBuilderWindow>("Campaign Builder");

        void OnEnable() { wantsMouseMove = true; RefreshSets(); }
        void OnFocus() { RefreshSets(); offTolCount = -1; }   // catches set/level changes done outside the window

        void RefreshSets()
        {
            var found = new System.Collections.Generic.List<string>();
            if (System.IO.Directory.Exists(SetsRoot))
                foreach (var d in System.IO.Directory.GetDirectories(SetsRoot))
                    found.Add(System.IO.Path.GetFileName(d));
            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            if (found.Count == 0) found.Add("Campaign");   // created on first generate
            sets = found.ToArray();
            setIdx = Mathf.Max(0, System.Array.IndexOf(sets, setName));
            setName = sets[setIdx];
            RefreshSetStats();
        }

        // List every hole in the set's numbering, from 1 up to the highest existing level.
        void ShowMissingLevels()
        {
            var present = new System.Collections.Generic.HashSet<int>();
            int max = 0;
            if (System.IO.Directory.Exists(OutputFolder))
                foreach (var f in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k))
                    { present.Add(k); if (k > max) max = k; }

            var missing = new System.Collections.Generic.List<int>();
            for (int l = 1; l <= max; l++)
                if (!present.Contains(l)) missing.Add(l);

            if (missing.Count == 0)
            {
                EditorUtility.DisplayDialog("Missing levels", $"{SetName}: no gaps — {max} level(s), all present.", "OK");
                return;
            }
            string list = string.Join(", ", missing);
            Debug.Log($"[CampaignBuilder] {SetName} missing {missing.Count} level(s): {list}");
            if (list.Length > 900) list = list.Substring(0, 900) + "… (full list in the Console)";
            EditorUtility.DisplayDialog("Missing levels",
                $"{SetName}: {missing.Count} missing of 1–{max}:\n\n{list}", "OK");
        }

        void RefreshSetStats()
        {
            var present = new System.Collections.Generic.HashSet<int>();
            int max = 0;
            if (System.IO.Directory.Exists(OutputFolder))
                foreach (var f in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f), out int k))
                    { present.Add(k); if (k > max) max = k; }
            setCount = 0;
            while (present.Contains(setCount + 1)) setCount++;   // consecutive run from 1
            setGap = max > setCount;
        }

        void OnGUI()
        {
            config = (CampaignCurveConfig)EditorGUILayout.ObjectField("Curve config", config, typeof(CampaignCurveConfig), false);

            EditorGUILayout.BeginHorizontal();
            from = EditorGUILayout.DelayedIntField("Level from", from);
            to = EditorGUILayout.DelayedIntField("to", to);
            EditorGUILayout.EndHorizontal();

            buildFoldout = EditorGUILayout.Foldout(buildFoldout, "Build", true);
            if (buildFoldout)
            {
                EditorGUI.BeginChangeCheck();
                setIdx = EditorGUILayout.Popup("Set", setIdx, sets);
                if (EditorGUI.EndChangeCheck()) { setName = sets[Mathf.Clamp(setIdx, 0, sets.Length - 1)]; RefreshSetStats(); offTolCount = -1; }
                if (setGap)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var prev = GUI.color; GUI.color = new Color(1f, 0.5f, 0.4f);
                        EditorGUILayout.LabelField(" ", $"⚠ {setCount} level(s) in {SetName} — level {setCount + 1} missing (gap)", EditorStyles.miniBoldLabel);
                        GUI.color = prev;
                        if (GUILayout.Button("Show missing", GUILayout.Width(96)))
                            ShowMissingLevels();
                    }
                }
                else
                    EditorGUILayout.LabelField(" ", $"{setCount} level(s) in {SetName}, no gaps", EditorStyles.miniLabel);
                seedBase = EditorGUILayout.IntField("Seed base", seedBase);
                skipExisting = EditorGUILayout.Toggle("Skip existing", skipExisting);
                overrideOffTol = EditorGUILayout.Toggle("Override off-tolerance", overrideOffTol);
                threadCount = Mathf.Clamp(EditorGUILayout.IntField("Threads", threadCount), 1, 64);
                if (config != null)
                {
                    UpdateOffTolCount();
                    EditorGUILayout.LabelField(" ",
                        $"{offTolCount} existing level(s) not what their slot asks (board size, or time ±{config.timeTol * 100:0}%)", EditorStyles.miniLabel);
                }
                using (new EditorGUI.DisabledScope(config == null))
                {
                    if (GUILayout.Button($"Generate levels {from}–{to}"))
                    {
                        GenerateLevels();
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button($"Generate outside Unity {from}–{to}"))
                    {
                        GenerateOutside();
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button("Export levels → Resources"))
                    LevelResourcesExporter.Export(OutputFolder, SetName);
            }

            DrawColorsSection();

            graphFoldout = EditorGUILayout.Foldout(graphFoldout, "Graph", true);
            if (!graphFoldout) return;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Show graph"))
            {
                BuildData();
                GUIUtility.ExitGUI();   // layout pass didn't include the graph rect yet
            }
            // which lines to draw — no rebuild needed, the data has both
            bool st = EditorGUILayout.ToggleLeft("Targets", showTargets, GUILayout.Width(70));
            bool sb = EditorGUILayout.ToggleLeft("Built levels", showBuilt, GUILayout.Width(90));
            if (st != showTargets || sb != showBuilt) { showTargets = st; showBuilt = sb; cacheStale = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Seconds of", GUILayout.Width(70));
            int unit = GUILayout.Toolbar(withPlaceFactor ? 0 : 1, UnitNames);
            if ((unit == 0) != withPlaceFactor) { withPlaceFactor = unit == 0; ApplyUnit(); }
            EditorGUILayout.EndHorizontal();

            if (built == null) return;
            var rect = GUILayoutUtility.GetRect(120f, 8000f, 120f, 8000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawGraph(rect);
        }

        // The slots of the range that still need a level (null, with a message, when none does).
        PoolBuildRunner.ScanResult OpenSlots()
        {
            from = Mathf.Max(1, from);
            to = Mathf.Max(from, to);

            var targets = new System.Collections.Generic.List<LevelTarget>(to - from + 1);
            for (int l = from; l <= to; l++) targets.Add(config.GetTarget(l));
            var scan = PoolBuildRunner.Scan(targets, OutputFolder, skipExisting, overrideOffTol);
            if (scan.open.Count > 0) return scan;
            EditorUtility.DisplayDialog("Campaign Builder", "All levels in this range already exist and are inside tolerance.", "OK");
            return null;
        }

        // The same build, run outside the editor by Tools/LevelGen: the editor stays free and several
        // can run at once. Watch and stop it in QueensPuzzle > Level Gen Runs.
        void GenerateOutside()
        {
            var scan = OpenSlots();
            if (scan == null) return;
            LevelGenWindow.Start($"{SetName} {from}–{to}", OutputFolder, config.Gates, scan.open,
                new PoolBuilder.Options { threadCount = threadCount, seedBase = seedBase, colors = Rules(PaletteLab()) });
        }

        // generate → rate → fit into any open slot whose TIME it matches (±timeTol, fixed) — no widening,
        // a board that fits nothing is discarded. The build core lives in PoolBuildRunner.
        void GenerateLevels()
        {
            var scan = OpenSlots();
            if (scan == null) return;

            if (!EditorUtility.DisplayDialog("Generate levels — summary",
                $"Set:  {SetName}\n" +
                $"Range:  {from}–{to}\n\n" +
                $"To build:  {scan.open.Count} level(s)\n" +
                $"   • new (missing):  {scan.newCount}\n" +
                $"   • override off-tolerance:  {scan.offTol}\n" +
                $"   • overwrite existing:  {scan.overwrite}\n\n" +
                $"Tolerance:  ±{config.timeTol * 100:0}% of the slot's seconds (fixed)\n" +
                $"Threads:  {threadCount}   ·   Seed base:  {seedBase}",
                "Go", "Cancel"))
                return;

            var (made, attempts) = PoolBuildRunner.Run(config.Gates, scan.open, OutputFolder,
                new PoolBuildRunner.Options
                {
                    threadCount = threadCount,
                    seedBase = seedBase,
                    title = "Generating campaign levels",
                    colors = Rules(PaletteLab()),
                    tolLabel = $"time ±{config.timeTol * 100:0}%",
                });

            offTolCount = -1;   // recount after the build
            RefreshSetStats();
            Debug.Log($"[CampaignBuilder] filled {made} level(s), {scan.open.Count} still open, {attempts} boards generated at time ±{config.timeTol * 100:0}%");
        }

        // ---- colors: find & fix confusable region colors ----------------------------------
        //
        // Two colours can confuse a player in two different ways, and each needs its own rule:
        //  - physically alike (OKLab distance < colorMinDist): the eye can't tell them apart at
        //    all, so they must never share a board;
        //  - same colour *family* (shared XKCD name, or hue angles too close): easy to tell apart
        //    side by side, but the player identifies regions categorically — "the pink one" — so
        //    a light pink and a dark pink still collide. Such pairs may share a board but must
        //    never touch. The two family tests cover each other's blind spots: names miss pairs
        //    straddling a naming boundary, hue misses pairs people collapse into one name.

        bool colorsFoldout = true;
        float colorMinDist = 0.09f;   // OKLab distance below which two colors read as "the same"
        float nameMinDist = 0.20f;    // XKCD name distance below which two colors share a name
        float hueMinDeg = 18f;        // hue angle difference below which two colors share a family
        System.Collections.Generic.List<(int lvl, string desc)> colorFindings;
        string colorFindingsTitle;
        Vector2 colorScroll;

        // Pairs both family tests call "far" but a human eye confirmed as confusable — the veto
        // list of last resort, edited in the window and persisted in EditorPrefs. Ships with
        // magenta #EA73FA ~ salmon #FF808E and purple #B185CF ~ periwinkle #9DAFFF.
        const string ByEyePrefsKey = "CampaignBuilder.forbiddenByEye";
        System.Collections.Generic.List<Vector2Int> forbiddenByEye;
        int byEyeA, byEyeB;

        // Project-level pairs; the prefs list only ever adds on top of these, so removing one
        // for good means removing it here.
        static readonly Vector2Int[] shippedByEye =
        {
            new Vector2Int(3, 13),   // magenta ~ salmon
            new Vector2Int(1, 11),   // purple ~ periwinkle
            new Vector2Int(10, 12),  // muted blue ~ muted green
            new Vector2Int(4, 8),    // mint ~ light green
            new Vector2Int(4, 16),   // mint ~ pale cyan
        };

        System.Collections.Generic.List<Vector2Int> ByEyePairs()
        {
            if (forbiddenByEye != null) return forbiddenByEye;
            forbiddenByEye = new System.Collections.Generic.List<Vector2Int>(shippedByEye);
            foreach (var tok in EditorPrefs.GetString(ByEyePrefsKey, "")
                         .Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var ab = tok.Split('~');
                if (ab.Length == 2 && int.TryParse(ab[0], out int a) && int.TryParse(ab[1], out int b)
                    && !forbiddenByEye.Contains(new Vector2Int(a, b)))
                    forbiddenByEye.Add(new Vector2Int(a, b));
            }
            return forbiddenByEye;
        }

        void SaveByEyePairs()
        {
            var parts = new string[forbiddenByEye.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = $"{forbiddenByEye[i].x}~{forbiddenByEye[i].y}";
            EditorPrefs.SetString(ByEyePrefsKey, parts.Length == 0 ? "none" : string.Join(";", parts));
        }

        /// The palette, this window's thresholds and the by-eye pairs, as the plain numbers the
        /// shared colour code (LevelColors) works on.
        LevelColors.Rules Rules(Vector3[] lab)
        {
            var r = new LevelColors.Rules { colorMinDist = colorMinDist, nameMinDist = nameMinDist, hueMinDeg = hueMinDeg, lab = new float[lab.Length * 3] };
            for (int i = 0; i < lab.Length; i++) { r.lab[i * 3] = lab[i].x; r.lab[i * 3 + 1] = lab[i].y; r.lab[i * 3 + 2] = lab[i].z; }
            var pairs = ByEyePairs();
            r.byEye = new int[pairs.Count * 2];
            for (int i = 0; i < pairs.Count; i++) { r.byEye[i * 2] = pairs[i].x; r.byEye[i * 2 + 1] = pairs[i].y; }
            return r;
        }

        /// The same rules at the default thresholds — for a build started from another window.
        public static LevelColors.Rules DefaultColorRules()
        {
            var win = CreateInstance<CampaignBuilderWindow>();
            try { return win.Rules(PaletteLab()); }
            finally { DestroyImmediate(win); }
        }

        void DrawColorsSection()
        {
            colorsFoldout = EditorGUILayout.Foldout(colorsFoldout, "Colors", true);
            if (!colorsFoldout) return;

            colorMinDist = EditorGUILayout.Slider("Min OKLab distance", colorMinDist, 0.03f, 0.20f);
            nameMinDist = EditorGUILayout.Slider("Min name distance", nameMinDist, 0.03f, 0.40f);
            hueMinDeg = EditorGUILayout.Slider("Min hue diff (deg)", hueMinDeg, 5f, 45f);

            // the human veto list — forbidden as neighbours no matter what the sliders say
            EditorGUILayout.LabelField("Forbidden by eye", EditorStyles.miniBoldLabel);
            var pal = qp.SORegionsColors.Instance.Colors;
            var byEye = ByEyePairs();
            for (int p = 0; p < byEye.Count; p++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var pr = byEye[p];
                    var rect = GUILayoutUtility.GetRect(36, 16, GUILayout.Width(36));
                    if (pr.x < pal.Length && pr.y < pal.Length)
                    {
                        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 16, 16), pal[pr.x].Color);
                        EditorGUI.DrawRect(new Rect(rect.x + 18, rect.y, 16, 16), pal[pr.y].Color);
                        EditorGUILayout.LabelField($"{pr.x}~{pr.y}  {pal[pr.x].Name}~{pal[pr.y].Name}", EditorStyles.miniLabel);
                    }
                    else EditorGUILayout.LabelField($"{pr.x}~{pr.y}  (outside palette)", EditorStyles.miniLabel);
                    if (GUILayout.Button("✕", GUILayout.Width(22))) { byEye.RemoveAt(p--); SaveByEyePairs(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var names = new string[pal.Length];
                for (int p = 0; p < pal.Length; p++) names[p] = $"{p} {pal[p].Name}";
                byEyeA = EditorGUILayout.Popup(byEyeA, names);
                byEyeB = EditorGUILayout.Popup(byEyeB, names);
                if (GUILayout.Button("Add pair", GUILayout.Width(70)) && byEyeA != byEyeB)
                {
                    var pr = new Vector2Int(Mathf.Min(byEyeA, byEyeB), Mathf.Max(byEyeA, byEyeB));
                    if (!byEye.Contains(pr)) { byEye.Add(pr); SaveByEyePairs(); }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find close colors (OKLab)"))
                {
                    FindColorIssues(global: true, neighbors: false);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Find forbidden neighbors (XKCD + hue)"))
                {
                    FindColorIssues(global: false, neighbors: true);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Recolor all levels"))
                {
                    RecolorAllLevels();
                    GUIUtility.ExitGUI();
                }
            }

            if (colorFindings == null) return;
            int violations = 0;
            foreach (var f in colorFindings) if (f.lvl > 0) violations++;
            EditorGUILayout.LabelField(
                $"{violations} {colorFindingsTitle} in {CountDistinctLevels()} level(s) of {SetName}" +
                (colorFindings.Count > violations ? $"  +{colorFindings.Count - violations} borderline" : ""),
                violations > 0 ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel);
            if (colorFindings.Count == 0) return;
            colorScroll = EditorGUILayout.BeginScrollView(colorScroll, GUILayout.MaxHeight(140));
            int i = 0;
            while (i < colorFindings.Count)   // one row per level, its pairs joined
            {
                int lvl = colorFindings[i].lvl;
                var parts = new System.Collections.Generic.List<string>();
                while (i < colorFindings.Count && colorFindings[i].lvl == lvl)
                {
                    var f = colorFindings[i++];
                    if (parts.Count < 3) parts.Add(f.desc);
                    else if (parts.Count == 3) parts.Add("…");
                }
                EditorGUILayout.LabelField(
                    lvl > 0 ? $"L{lvl}:  {string.Join(" · ", parts)}" : string.Join(" · ", parts),
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        int CountDistinctLevels()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var f in colorFindings) if (f.lvl > 0) seen.Add(f.lvl);
            return seen.Count;
        }

        static Vector3 ToOklab(Color c)
        {
            float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
            float l = Mathf.Pow(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b, 1f / 3f);
            float m = Mathf.Pow(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b, 1f / 3f);
            float s = Mathf.Pow(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b, 1f / 3f);
            return new Vector3(
                0.2104542553f * l + 0.793617785f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.428592205f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.808675766f * s);
        }

        static Vector3[] PaletteLab()
        {
            var colors = qp.SORegionsColors.Instance.Colors;
            var lab = new Vector3[colors.Length];
            for (int i = 0; i < colors.Length; i++) lab[i] = ToOklab(colors[i].Color);
            return lab;
        }

        // global: any two colours on the board closer than colorMinDist in OKLab — such a pair
        // must not share a board at all. neighbors: touching regions whose colours share a name
        // per forbiddenNeighbors (or are the same colour outright).
        void FindColorIssues(bool global, bool neighbors)
        {
            var lab = PaletteLab();
            var rules = Rules(lab);
            var palette = qp.SORegionsColors.Instance.Colors;
            colorFindings = new System.Collections.Generic.List<(int, string)>();
            colorFindingsTitle = global && neighbors ? "color issue(s)"
                : global ? "close pair(s)" : "forbidden neighbor pair(s)";
            // the neighbors button also reports near-threshold pairs, with the levels they touch in
            var border = neighbors && !global ? BorderlinePairs(lab) : null;
            var borderHits = new System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<string>>();
            if (!System.IO.Directory.Exists(OutputFolder)) return;
            foreach (var file in System.IO.Directory.GetFiles(OutputFolder, "*.asset"))
            {
                if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), out int l)) continue;
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                if (lvl == null) continue;
                int n = lvl.size;
                var adj = neighbors ? LevelColors.Adjacency(lvl.size, lvl.regions) : null;
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        int ca = lvl.ColorOf(a), cb = lvl.ColorOf(b);
                        if (ca >= lab.Length || cb >= lab.Length) continue;
                        if (global)
                        {
                            float d = ca == cb ? 0f : Vector3.Distance(lab[ca], lab[cb]);
                            if (d < colorMinDist)
                                colorFindings.Add((l, $"{palette[ca].Name}~{palette[cb].Name} {d:0.00}"));
                        }
                        if (neighbors && adj[a, b])
                        {
                            if (ca == cb || LevelColors.ForbiddenNeighbor(rules, ca, cb))
                                colorFindings.Add((l, $"{(char)('A' + a)}~{(char)('A' + b)} {palette[ca].Name}~{palette[cb].Name}"));
                            else if (border != null && border.ContainsKey((Mathf.Min(ca, cb), Mathf.Max(ca, cb))))
                            {
                                var key = (Mathf.Min(ca, cb), Mathf.Max(ca, cb));
                                if (!borderHits.TryGetValue(key, out var hits)) borderHits[key] = hits = new System.Collections.Generic.List<string>();
                                if (hits.Count < 3) hits.Add($"L{l} {(char)('A' + a)}~{(char)('A' + b)}");
                            }
                        }
                    }
            }
            colorFindings.Sort((x, y) => x.lvl.CompareTo(y.lvl));
            if (border != null)
            {
                int row = 0;
                foreach (var kv in border)
                {
                    string where = borderHits.TryGetValue(kv.Key, out var hits)
                        ? string.Join(", ", hits) : "not adjacent in this set";
                    colorFindings.Add((--row, $"borderline  {kv.Value}   ({where})"));
                }
            }
            Repaint();
        }

        // Recolour levels that violate either rule: a pair anywhere on the board closer than
        // colorMinDist in OKLab, or a forbidden family pair on touching regions. Clean levels
        // keep the colours players already saw. Same-name pairs may share a board — they just
        // never touch — which leaves far more palette slack for big boards than the old
        // everything-far-from-everything pass.
        void RecolorAllLevels()
        {
            if (!System.IO.Directory.Exists(OutputFolder)) return;
            var files = System.IO.Directory.GetFiles(OutputFolder, "*.asset");
            if (!EditorUtility.DisplayDialog("Recolor all levels",
                    $"Rewrite region colours on violating level(s) of {files.Length} in {SetName}?\n\n" +
                    $"Only boards that break the rules are touched: colours at least {colorMinDist:0.00} apart,\n" +
                    "and same-name colours never touch.\n" +
                    "Re-export the level pack afterwards.", "Recolor", "Cancel"))
                return;

            var rules = Rules(PaletteLab());
            int done = 0, failed = 0, skipped = 0;
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(files[i]), out int l)) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Recoloring",
                            $"level {l}  ({done} done, {skipped} clean, {failed} failed)", (float)i / files.Length))
                        break;

                    var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                    if (lvl == null) continue;

                    var adj = LevelColors.Adjacency(lvl.size, lvl.regions);
                    if (!LevelColors.HasIssues(lvl.size, lvl.regionColors, rules, adj)) { skipped++; continue; }   // clean — keep shipped colours

                    var assign = LevelColors.Pick(lvl.size, l, rules, adj);
                    if (assign == null) { failed++; continue; }   // threshold too high for this size

                    Undo.RecordObject(lvl, "Recolor level");
                    lvl.regionColors = LevelColors.Stored(assign);
                    EditorUtility.SetDirty(lvl);
                    done++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CampaignBuilder] recoloured {done} level(s), {skipped} already clean, {failed} unsolvable at {colorMinDist:0.00}. " +
                      "Re-export the level pack to ship it.");
            FindColorIssues(global: true, neighbors: true);   // should come back empty
        }

        // Palette pairs that PASS every rule but sit within a safety margin of a threshold.
        // The rules are step functions over a continuous perception, so a pair this close to
        // a line is "unchecked", not "clean" — review by eye and veto via the by-eye list.
        System.Collections.Generic.Dictionary<(int, int), string> BorderlinePairs(Vector3[] lab)
        {
            var palette = qp.SORegionsColors.Instance.Colors;
            var rules = Rules(lab);
            var border = new System.Collections.Generic.Dictionary<(int, int), string>();
            for (int a = 0; a < lab.Length; a++)
                for (int b = a + 1; b < lab.Length; b++)
                {
                    float ok = Vector3.Distance(lab[a], lab[b]);
                    if (ok < colorMinDist || LevelColors.ForbiddenNeighbor(rules, a, b)) continue;   // already caught
                    float nm = a < LevelColors.NameDist.GetLength(0) && b < LevelColors.NameDist.GetLength(0) ? LevelColors.NameDist[a, b] : 1f;
                    float hd = LevelColors.HueGap(rules, a, b);
                    if (ok >= colorMinDist + 0.03f && nm >= nameMinDist + 0.05f && hd >= hueMinDeg + 4f) continue;
                    border[(a, b)] = $"{a}~{b} {palette[a].Name}~{palette[b].Name}  OKLab {ok:0.00} · name {nm:0.00} · hue {hd:0}°";
                }
            return border;
        }

        /// One level under the same rules and default thresholds as the campaign pass —
        /// LevelBuilderWindow's per-level Recolor button. Null when no colour set fits.
        public static int[] RecolorSingle(LevelData lvl, int seed)
        {
            var win = CreateInstance<CampaignBuilderWindow>();
            try { return LevelColors.Pick(lvl.size, seed, win.Rules(PaletteLab()), LevelColors.Adjacency(lvl.size, lvl.regions)); }
            finally { DestroyImmediate(win); }
        }

        // Existing levels in range that are not what their slot asks for (board size or time window).
        // Cached — recounts only when range/set/config change, on window focus, or after a build.
        void UpdateOffTolCount()
        {
            var key = (from, to, setIdx, (Object)config);
            if (offTolCount >= 0 && key == offTolKey) return;
            offTolKey = key;
            offTolCount = 0;
            for (int l = from; l <= to; l++)
            {
                var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{l}.asset");
                if (lvl == null) continue;
                var t = config.GetTarget(l);
                if (!PoolBuildRunner.Fits(t, lvl)) offTolCount++;   // size and time, like the build
            }
        }

        // Both lines in one pass: what the curve asks of each slot (needs a curve) and what the built
        // level in the output folder takes (its board rated by TimeRater — once per asset, so the first
        // graph of a long range takes a moment). Kept as board seconds + the level's place factor;
        // ApplyUnit turns them into the drawn numbers. Missing levels show as 0 (gaps on the floor).
        void BuildData()
        {
            from = Mathf.Max(1, from);
            to = Mathf.Clamp(to, from, from + MaxRange - 1);
            int n = to - from + 1;
            targetBoard = config != null ? new float[n] : null;
            builtBoard = new float[n];
            factors = new float[n];
            roles = new byte[n];
            lessons = new byte[n];
            dataFrom = from;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (n > 400 && i % 200 == 0) EditorUtility.DisplayProgressBar("Campaign graph", $"rating level {from + i} of {to}", i / (float)n);
                    factors[i] = config != null ? config.PlaceFactorAt(from + i) : 1f;
                    if (config != null)
                    {
                        var t = config.GetTarget(from + i);
                        targetBoard[i] = t.boardSeconds;
                        factors[i] = t.placeFactor;
                        roles[i] = (byte)t.role;
                        lessons[i] = (byte)(t.tricks.lesson ? t.tricks.stage : 0);
                    }
                    var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{from + i}.asset");
                    builtBoard[i] = lvl != null ? (float)PoolBuildRunner.SecondsOf(lvl) : 0f;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            ApplyUnit();
        }

        // The drawn numbers in the chosen unit (see withPlaceFactor), and the scale that fits them.
        void ApplyUnit()
        {
            if (builtBoard == null) return;
            int n = builtBoard.Length;
            targets = targetBoard != null ? new int[n] : null;
            built = new int[n];
            maxSeconds = 1;
            for (int i = 0; i < n; i++)
            {
                float f = withPlaceFactor ? factors[i] : 1f;
                if (targets != null) { targets[i] = Mathf.RoundToInt(targetBoard[i] * f); maxSeconds = Mathf.Max(maxSeconds, targets[i]); }
                built[i] = Mathf.RoundToInt(builtBoard[i] * f);
                maxSeconds = Mathf.Max(maxSeconds, built[i]);
            }
            cacheStale = true;   // invalidate screen cache
            Repaint();
        }

        void DrawGraph(Rect r)
        {
            const float padL = 44, padR = 8, padT = 8, padB = 20;
            var plot = new Rect(r.x + padL, r.y + padT, r.width - padL - padR, r.height - padT - padB);
            if (plot.width < 20 || plot.height < 20) return;

            var e = Event.current;
            if (e.type == EventType.MouseMove && plot.Contains(e.mousePosition)) Repaint();
            if (e.type != EventType.Repaint) return;
            if (cacheStale || r != cachedRect) RebuildScreenCache(plot, r);

            bool pro = EditorGUIUtility.isProSkin;
            var ink = pro ? new Color(0.75f, 0.75f, 0.73f) : new Color(0.33f, 0.33f, 0.31f);
            var grid = pro ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.08f);
            var blue = pro ? new Color(0.22f, 0.53f, 0.90f) : new Color(0.16f, 0.47f, 0.84f);
            var orange = pro ? new Color(0.95f, 0.62f, 0.20f) : new Color(0.88f, 0.52f, 0.10f);
            var red = pro ? new Color(0.90f, 0.40f, 0.40f) : new Color(0.82f, 0.23f, 0.23f);

            EditorGUI.DrawRect(plot, pro ? new Color(0, 0, 0, 0.18f) : new Color(1, 1, 1, 0.5f));

            // gridlines + labels (GUI, repaint-safe)
            var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            label.normal.textColor = ink;
            int step = NiceStep(maxSeconds, 5);
            for (int v = 0; v <= maxSeconds; v += step)
            {
                float y = plot.yMax - v / (float)maxSeconds * plot.height;
                EditorGUI.DrawRect(new Rect(plot.xMin, y, plot.width, 1f), grid);
                GUI.Label(new Rect(r.x, y - 8, padL - 6, 16), v.ToString(), label);
            }

            // lines + milestone dots via GL — built first, so the target line sits on top
            if (!lineMat)
                lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            GUI.BeginClip(new Rect(0, 0, position.width, position.height));
            lineMat.SetPass(0);
            GL.PushMatrix();

            void Strip(Vector3[] pts, Color c)
            {
                if (pts == null) return;
                GL.Begin(GL.LINE_STRIP);
                GL.Color(c);   // after Begin — GL resets the colour on every Begin
                for (int i = 0; i < pts.Length; i++) GL.Vertex(pts[i]);
                GL.End();
            }
            Strip(builtLine, orange);
            Strip(targetLine, blue);

            GL.Begin(GL.QUADS);
            GL.Color(red);
            const float s = 3.5f;
            for (int i = 0; i < milestones.Length; i++)
            {
                var p = milestones[i];
                GL.Vertex3(p.x - s, p.y - s, 0);
                GL.Vertex3(p.x + s, p.y - s, 0);
                GL.Vertex3(p.x + s, p.y + s, 0);
                GL.Vertex3(p.x - s, p.y + s, 0);
            }
            // lesson levels: a green square on the first (the trick is learned), a small one on the two after
            var green = pro ? new Color(0.35f, 0.80f, 0.45f) : new Color(0.10f, 0.60f, 0.25f);
            GL.Color(green);
            for (int i = 0; i < lessonDots.Length; i++)
            {
                var p = lessonDots[i];
                float h = p.z;   // half size, by stage
                GL.Vertex3(p.x - h, p.y - h, 0);
                GL.Vertex3(p.x + h, p.y - h, 0);
                GL.Vertex3(p.x + h, p.y + h, 0);
                GL.Vertex3(p.x - h, p.y + h, 0);
            }
            GL.End();

            GL.PopMatrix();
            GUI.EndClip();

            // x labels
            var xlab = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            xlab.normal.textColor = ink;
            int count = built.Length;
            int ticks = Mathf.Min(6, count);
            for (int i = 0; i < ticks; i++)
            {
                int lvl = dataFrom + (int)((count - 1) * i / (float)Mathf.Max(1, ticks - 1));
                float x = plot.xMin + (lvl - dataFrom) / (float)Mathf.Max(1, count - 1) * plot.width;
                GUI.Label(new Rect(x - 24, plot.yMax + 2, 48, 16), lvl.ToString(), xlab);
            }

            // what the y axis is in, always on the plot so a screenshot says it too
            var unitLab = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperLeft };
            unitLab.normal.textColor = ink;
            GUI.Label(new Rect(plot.xMin + 6, plot.yMin + 2, plot.width - 12, 16),
                withPlaceFactor ? "Seconds: a regular player at this level (board × place factor)"
                                : "Seconds: the board itself, experienced player (no place factor)", unitLab);

            DrawGraphHover(plot, ink, blue, orange, red);
        }

        void DrawGraphHover(Rect plot, Color ink, Color blue, Color orange, Color red)
        {
            if (built == null || built.Length == 0 || !plot.Contains(Event.current.mousePosition)) return;

            int n = built.Length;
            int i = Mathf.Clamp(Mathf.RoundToInt((Event.current.mousePosition.x - plot.xMin) / Mathf.Max(1f, plot.width) * Mathf.Max(1, n - 1)), 0, n - 1);
            int level = dataFrom + i;
            bool milestone = roles != null && i < roles.Length && roles[i] == (byte)Role.Milestone;
            bool hasTarget = targets != null && showTargets;

            float x = plot.xMin + i / (float)Mathf.Max(1, n - 1) * plot.width;
            var marker = milestone ? red : blue;
            EditorGUI.DrawRect(new Rect(x - 1f, plot.yMin, 2f, plot.height), new Color(marker.r, marker.g, marker.b, 0.28f));
            if (showBuilt) { var p = ToScreen(plot, i, n, built[i]); EditorGUI.DrawRect(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), orange); }
            if (hasTarget) { var p = ToScreen(plot, i, n, targets[i]); EditorGUI.DrawRect(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), milestone ? red : blue); }

            string text = $"Level {level}"
                + (hasTarget ? $"  |  target {targets[i]} s" : "")
                + (showBuilt ? $"  |  built {built[i]} s" : "")
                + (factors != null && i < factors.Length ? $"  |  factor ×{factors[i]:0.00}" + (withPlaceFactor ? " (in)" : " (out)") : "")
                + (milestone ? "  |  milestone" : "")
                + TrickNote(level);
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                padding = new RectOffset(7, 7, 3, 3)
            };
            style.normal.textColor = ink;

            Vector2 size = style.CalcSize(new GUIContent(text));
            var box = new Rect(Event.current.mousePosition.x + 12f, Event.current.mousePosition.y - size.y - 10f, size.x + 10f, size.y + 6f);
            if (box.xMax > plot.xMax) box.x = Event.current.mousePosition.x - box.width - 12f;
            if (box.yMin < plot.yMin) box.y = Event.current.mousePosition.y + 12f;
            GUI.Label(box, text, style);
        }

        // What the curve asks of the level's tricks, and what is wrong with the built level's tricks (if anything).
        string TrickNote(int level)
        {
            if (config == null) return "";
            var t = config.GetTarget(level);
            string s = "";
            if (t.tricks.lesson)
            {
                string[] stage = { "learn", "practise", "prove" };
                string times = t.tricks.maxUses == 0 ? $"×{t.tricks.minUses}+" : t.tricks.minUses == t.tricks.maxUses ? $"×{t.tricks.minUses}" : $"×{t.tricks.minUses}-{t.tricks.maxUses}";
                s += $"  |  {stage[Mathf.Clamp(t.tricks.stage - 1, 0, 2)]} {t.tricks.teach} {times}";
            }
            if (t.dog) s += "  |  dog";
            var lvl = AssetDatabase.LoadAssetAtPath<LevelData>($"{OutputFolder}/{level}.asset");
            if (lvl != null)
            {
                string bad = CampaignCurve.TrickProblem(t, PoolBuildRunner.ReportOf(lvl));
                if (bad != null) s += $"  |  built: {bad}";
            }
            return s;
        }

        void RebuildScreenCache(Rect plot, Rect full)
        {
            cachedRect = full;
            cacheStale = false;
            targetLine = showTargets && targets != null ? Polyline(plot, targets) : null;
            builtLine = showBuilt ? Polyline(plot, built) : null;

            // milestone dots ride the target line, or the built line when targets are hidden
            var on = showTargets && targets != null ? targets : showBuilt ? built : null;
            var miles = new System.Collections.Generic.List<Vector3>();
            if (on != null)
                for (int i = 0; i < on.Length; i++)
                    if (roles[i] == (byte)Role.Milestone)
                        miles.Add(ToScreen(plot, i, on.Length, on[i]));
            milestones = miles.ToArray();

            var dots = new System.Collections.Generic.List<Vector3>();
            if (on != null && lessons != null)
                for (int i = 0; i < on.Length; i++)
                    if (lessons[i] > 0)
                    {
                        var p = ToScreen(plot, i, on.Length, on[i]);
                        dots.Add(new Vector3(p.x, p.y, lessons[i] == 1 ? 4.5f : 2.5f));
                    }
            lessonDots = dots.ToArray();
        }

        // Two points per pixel column (max then min) — spikes and breathers both survive
        // decimation, and the polyline stays ≤ 2 * plot.width points no matter the range.
        Vector3[] Polyline(Rect plot, int[] values)
        {
            int n = values.Length;
            int cols = Mathf.Max(1, (int)plot.width);
            var pts = new System.Collections.Generic.List<Vector3>(Mathf.Min(n, cols) * 2);
            if (n <= cols)
            {
                for (int i = 0; i < n; i++)
                    pts.Add(ToScreen(plot, i, n, values[i]));
            }
            else
            {
                for (int c = 0; c < cols; c++)
                {
                    int i0 = (int)(c * (long)n / cols), i1 = (int)((c + 1) * (long)n / cols);
                    if (i1 <= i0) i1 = i0 + 1;
                    int lo = int.MaxValue, hi = int.MinValue;
                    for (int i = i0; i < i1; i++)
                    {
                        if (values[i] < lo) lo = values[i];
                        if (values[i] > hi) hi = values[i];
                    }
                    pts.Add(ToScreen(plot, i0, n, hi));
                    if (lo != hi) pts.Add(ToScreen(plot, i0, n, lo));
                }
            }
            return pts.ToArray();
        }

        Vector3 ToScreen(Rect plot, int i, int n, int w) => new Vector3(
            plot.xMin + i / (float)Mathf.Max(1, n - 1) * plot.width,
            plot.yMax - w / (float)maxSeconds * plot.height,
            0f);

        static int NiceStep(int max, int ticks)
        {
            int raw = Mathf.Max(1, max / Mathf.Max(1, ticks));
            int mag = Mathf.Max(1, (int)Mathf.Pow(10, Mathf.FloorToInt(Mathf.Log10(raw))));
            foreach (float m in new[] { 1f, 2f, 2.5f, 5f, 10f })
                if (raw <= m * mag) return Mathf.Max(1, (int)(m * mag));
            return Mathf.Max(1, 10 * mag);
        }
    }
}
