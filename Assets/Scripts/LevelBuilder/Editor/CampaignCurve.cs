namespace QueensPuzzle
{
    /// <summary>
    /// Build-time accept/reject for generated levels against <see cref="GenerationGates"/>.
    /// All tunables live on the curve config asset (exposed as its Gates property); this only
    /// wires them to <see cref="WeightRater"/> reports (editor-only, hence not on the config class).
    ///
    /// A board fits a slot BY TIME. The slot states the seconds it asks for and its window
    /// (<see cref="CampaignCurveConfig.LevelTarget"/>); a board is measured in calibrated seconds
    /// over the roads players take (<see cref="TimeRater"/>). Two boards of one weight can be 20% apart
    /// in what players take — matching on seconds is what keeps a plain slot from coming out a peak.
    /// </summary>
    public static class CampaignCurve
    {
        /// <summary>
        /// Is a board of these calibrated seconds what this slot asks for? The slot asks in real
        /// seconds at its level, so the board's own seconds are first turned into those.
        /// </summary>
        public static bool InTimeWindow(in CampaignCurveConfig.LevelTarget t, double boardSeconds)
        {
            double real = boardSeconds * t.placeFactor;
            return real >= t.minSeconds && real <= t.maxSeconds;
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

        /// <summary>How many times a solve needed one of the curve's tricks.</summary>
        public static int Uses(in WeightRater.Report r, CampaignCurveConfig.Trick k)
        {
            switch (k)
            {
                case CampaignCurveConfig.Trick.LineSingle:            return r.lineSingles;
                case CampaignCurveConfig.Trick.LineToRegion:          return r.lineToRegionUses;
                case CampaignCurveConfig.Trick.RegionToLine:          return r.regionToLineUses;
                case CampaignCurveConfig.Trick.Squeeze:               return r.squeezeUses;
                case CampaignCurveConfig.Trick.Subset2LinesToRegions: return r.subsetLineToRegionUses - r.subsetLineToRegion3Uses;
                case CampaignCurveConfig.Trick.Subset2RegionsToLines: return r.subsetRegionToLineUses - r.subsetRegionToLine3Uses;
                case CampaignCurveConfig.Trick.Subset3:               return r.subsetLineToRegion3Uses + r.subsetRegionToLine3Uses;
                case CampaignCurveConfig.Trick.Choke:                 return r.regionChokeUses + r.lineChokeUses;
                case CampaignCurveConfig.Trick.Fish:                  return r.fishUses;
            }
            return 0;
        }

        static readonly CampaignCurveConfig.Trick[] AllTricks = (CampaignCurveConfig.Trick[])System.Enum.GetValues(typeof(CampaignCurveConfig.Trick));

        /// <summary>
        /// What is wrong with a board's tricks for this slot, or null when nothing is: a trick the level
        /// forbids (not opened yet, or too young to stand beside a new one), or the lesson's own trick
        /// needed too few / too many times.
        /// </summary>
        public static string TrickProblem(in CampaignCurveConfig.LevelTarget t, in WeightRater.Report rep)
        {
            var rule = t.tricks;
            if (rule.forbidden != 0)
                foreach (var k in AllTricks)
                    if (rule.Forbids(k) && Uses(rep, k) > 0) return $"{k} not allowed here";
            if (rule.lesson)
            {
                int n = Uses(rep, rule.teach);
                if (n < rule.minUses) return $"needs {rule.teach} ×{rule.minUses}, has {n}";
                if (rule.maxUses > 0 && n > rule.maxUses) return $"{rule.teach} at most ×{rule.maxUses}, has {n}";
            }
            return null;
        }

        public static bool TricksOk(in CampaignCurveConfig.LevelTarget t, in WeightRater.Report rep) => TrickProblem(t, rep) == null;

        /// <summary>Does a built level take the time its slot asks for?</summary>
        public static bool InTimeWindow(in CampaignCurveConfig.LevelTarget t, LevelData lvl) => InTimeWindow(t, SecondsOf(lvl));

        /// <summary>Is a built level what its slot asks for — the board size, the time AND the tricks?</summary>
        public static bool Fits(in CampaignCurveConfig.LevelTarget t, LevelData lvl) =>
            lvl.size == t.boardSize && DogOk(t, lvl) && InTimeWindow(t, lvl) && TricksOk(t, ReportOf(lvl));

        /// <summary>A dog slot holds a board with exactly one revealed queen; any other slot, a board with none.</summary>
        public static bool DogOk(in CampaignCurveConfig.LevelTarget t, LevelData lvl) =>
            (lvl.revealedRows == null ? 0 : lvl.revealedRows.Length) == (t.dog ? 1 : 0);

        /// <summary>
        /// Accept/reject one rated candidate for a slot. <paramref name="seconds"/> is its
        /// <see cref="TimeRater"/> rating, taken once per board. Board size must match separately.
        /// </summary>
        public static bool PassesGates(in GenerationGates c, in CampaignCurveConfig.LevelTarget t, in WeightRater.Report rep, double seconds)
        {
            if (!rep.solved || rep.trials > c.maxTrials) return false;
            if (!InTimeWindow(t, seconds)) return false;
            if (!TricksOk(t, rep)) return false;
            bool milestone = t.role == CampaignCurveConfig.Role.Milestone;
            if (rep.paidSteps > (milestone ? c.milestoneMaxPaidSteps : c.maxPaidSteps)) return false;
            // breathers skip the wall / grind / start gates, so onboarding is not rejected
            if (t.role != CampaignCurveConfig.Role.Breather && rep.paidSteps > 0)
            {
                if (rep.peak / (float)rep.weight > c.maxPeakShare) return false;            // wall
                double as10x10 = seconds / System.Math.Pow(c.secondsPerRow, rep.size - 10);
                if (as10x10 / rep.paidSteps < c.minSecondsPerStep) return false;            // grind
                // with few steps the first third always holds most of the rating, and that's
                // fine on an easy board — hence no start gate on breathers either
                bool hardSlot = milestone || t.role == CampaignCurveConfig.Role.Peak;
                if (rep.startShare > (hardSlot ? c.peakMaxStartShare : c.maxStartShare)) return false;
            }
            return true;
        }
    }
}
