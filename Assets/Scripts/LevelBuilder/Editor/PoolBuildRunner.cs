using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// The shared pool build core, extracted from the campaign builder so any target source can
    /// drive it (campaign levels, daily tiers). Generate → rate → fit into any open slot at the
    /// target's FIXED window — no widening, a board that fits nothing is discarded.
    ///
    /// Inputs are deliberately dumb: <see cref="GenerationGates"/> + a list of LevelTargets whose
    /// time windows (seconds) are already baked in. Callers own all dialogs; this owns the worker threads,
    /// the progress bar and the asset writes ({outputFolder}/{target.level}.asset).
    /// </summary>
    public static class PoolBuildRunner
    {
        public struct Options
        {
            public int threadCount;
            public int seedBase;
            public string title;      // progress-bar title
            public string tolLabel;   // display only, e.g. "time ±6%"
            public float maxSeconds;  // stop after this long and leave the rest open (0 = until done or cancelled)
            public LevelColors.Rules colors;   // every built level is coloured by these (null = left uncoloured)
        }

        public sealed class ScanResult
        {
            public readonly System.Collections.Generic.List<LevelTarget> open
                = new System.Collections.Generic.List<LevelTarget>();
            public int newCount, offTol, overwrite;
        }

        /// <summary>
        /// Which targets still need a level: no asset yet · skip-existing off · or the existing
        /// level is not what the slot asks for (another board size, or outside the time window)
        /// and override is on.
        /// </summary>
        public static ScanResult Scan(System.Collections.Generic.IList<LevelTarget> targets,
            string outputFolder, bool skipExisting, bool overrideOffTol)
        {
            var r = new ScanResult();
            foreach (var t in targets)
            {
                var existing = AssetDatabase.LoadAssetAtPath<LevelData>($"{outputFolder}/{t.level}.asset");
                if (existing == null) { r.open.Add(t); r.newCount++; }
                else if (overrideOffTol && !Fits(t, existing)) { r.open.Add(t); r.offTol++; }   // size and time, like the build
                else if (!skipExisting) { r.open.Add(t); r.overwrite++; }
            }
            return r;
        }

        /// <summary>
        /// Fills the open slots until done or the user cancels the progress bar. Mutates
        /// <paramref name="open"/> (filled slots are removed). Returns (levels filled, boards generated).
        /// The build itself is <see cref="PoolBuilder.Run"/>; this adds what only the editor can do.
        /// </summary>
        public static (int made, long attempts) Run(GenerationGates gates,
            System.Collections.Generic.List<LevelTarget> open,
            string outputFolder, Options opt)
        {
            EnsureFolder(outputFolder);
            try
            {
                AssetDatabase.StartAssetEditing();   // files are still written per level; Unity imports them once at the end
                var (made, attempts, uncolourable) = PoolBuilder.Run(gates, open,
                    new PoolBuilder.Options { threadCount = opt.threadCount, seedBase = opt.seedBase, maxSeconds = opt.maxSeconds, colors = opt.colors },
                    f => SaveLevel(outputFolder, f),
                    (done, left, boards) => !EditorUtility.DisplayCancelableProgressBar(opt.title,
                        $"filled {done} · open {left} · boards {boards} · threads {opt.threadCount} · {opt.tolLabel}",
                        done / (float)(done + left)));
                if (uncolourable > 0) Debug.LogWarning($"[PoolBuild] {uncolourable} board(s) fitted a slot but no colour set fits them — discarded.");
                return (made, attempts);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // A built level stores its board, not its seconds, so the seconds need the board rated again.
        // Kept per asset (and dropped when the board changes) so a count or a graph over a range
        // costs the rating once, not on every repaint.
        static readonly System.Collections.Generic.Dictionary<int, (int weight, int size, int revealed, double secs, WeightRater.Report rep)> _secs
            = new System.Collections.Generic.Dictionary<int, (int, int, int, double, WeightRater.Report)>();

        /// <summary>Calibrated seconds of a built level — the board's own, for an experienced player
        /// (rates the board; cached per asset).</summary>
        public static double SecondsOf(LevelData lvl) => Rated(lvl).secs;

        /// <summary>The rater's report of a built level, with its revealed queens (cached per asset).</summary>
        public static WeightRater.Report ReportOf(LevelData lvl) => Rated(lvl).rep;

        static (double secs, WeightRater.Report rep) Rated(LevelData lvl)
        {
            int id = lvl.GetInstanceID();
            int revealed = lvl.revealedRows == null ? 0 : lvl.revealedRows.Length;
            if (_secs.TryGetValue(id, out var c) && c.weight == lvl.weight && c.size == lvl.size && c.revealed == revealed) return (c.secs, c.rep);
            var rep = WeightRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns, lvl.revealedRows);
            double s = TimeRater.SecondsExact(lvl.size, lvl.regions, lvl.solutionColumns, rep, lvl.revealedRows);
            _secs[id] = (lvl.weight, lvl.size, revealed, s, rep);
            return (s, rep);
        }

        /// <summary>Is a built level what its slot asks for — the board size, the time AND the tricks?
        /// A dog slot holds a board with exactly one revealed queen; any other slot, a board with none.</summary>
        public static bool Fits(in LevelTarget t, LevelData lvl) =>
            lvl.size == t.boardSize
            && (lvl.revealedRows == null ? 0 : lvl.revealedRows.Length) == (t.dog ? 1 : 0)
            && CampaignCurve.InTimeWindow(t, SecondsOf(lvl))
            && CampaignCurve.TricksOk(t, ReportOf(lvl));

        static void SaveLevel(string outputFolder, in PoolBuilder.Filled f)
        {
            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = f.slot.boardSize;
            data.regions = f.region;
            data.solutionColumns = f.cols;
            data.weight = f.weight;
            data.seed = f.seed;
            data.revealedRows = f.revealedRows ?? new int[0];
            data.regionColors = f.regionColors;
            AssetDatabase.CreateAsset(data, $"{outputFolder}/{f.slot.level}.asset");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
