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
        static readonly System.Collections.Generic.Dictionary<int, (int weight, int size, double secs)> _secs
            = new System.Collections.Generic.Dictionary<int, (int, int, double)>();

        /// <summary>Calibrated seconds of a built level — the board's own, for an experienced player
        /// (rates the board; cached per asset).</summary>
        public static double SecondsOf(LevelData lvl)
        {
            int id = lvl.GetInstanceID();
            if (_secs.TryGetValue(id, out var c) && c.weight == lvl.weight && c.size == lvl.size) return c.secs;
            double s = TimeRater.SecondsExact(lvl.size, lvl.regions, lvl.solutionColumns,
                WeightRater.Rate(lvl.size, lvl.regions, lvl.solutionColumns));
            _secs[id] = (lvl.weight, lvl.size, s);
            return s;
        }

        /// <summary>Does a built level take the time its slot asks for?</summary>
        public static bool InTimeWindow(in CampaignCurveConfig.LevelTarget t, LevelData lvl) => InTimeWindow(t, SecondsOf(lvl));

        /// <summary>
        /// Accept/reject one rated candidate for a slot. <paramref name="seconds"/> is its
        /// <see cref="TimeRater"/> rating, taken once per board. Board size must match separately.
        /// </summary>
        public static bool PassesGates(in GenerationGates c, in CampaignCurveConfig.LevelTarget t, in WeightRater.Report rep, double seconds)
        {
            if (!rep.solved || rep.trials > c.maxTrials) return false;
            if (!InTimeWindow(t, seconds)) return false;
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
