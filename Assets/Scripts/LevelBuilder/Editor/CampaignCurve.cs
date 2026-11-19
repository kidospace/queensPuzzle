namespace QueensPuzzle
{
    /// <summary>
    /// Build-time accept/reject for generated levels against <see cref="GenerationGates"/>.
    /// All tunables live on the curve config asset (exposed as its Gates property); this only
    /// wires them to <see cref="WeightRater"/> reports (editor-only, hence not on the config class).
    ///
    /// A board fits a slot BY TIME. The slot states the seconds it asks for and its window
    /// (<see cref="LevelTarget"/>); a board is measured in calibrated seconds
    /// over the roads players take (<see cref="TimeRater"/>). Two boards of one weight can be 20% apart
    /// in what players take — matching on seconds is what keeps a plain slot from coming out a peak.
    ///
    /// This file has no Unity types: the pool builder uses it inside the editor and outside it.
    /// What reads a built level asset is in PoolBuildRunner.
    /// </summary>
    public static class CampaignCurve
    {
        /// <summary>
        /// Is a board of these calibrated seconds what this slot asks for? The slot asks in real
        /// seconds at its level, so the board's own seconds are first turned into those.
        /// </summary>
        public static bool InTimeWindow(in LevelTarget t, double boardSeconds)
        {
            double real = boardSeconds * t.placeFactor;
            return real >= t.minSeconds && real <= t.maxSeconds;
        }

        /// <summary>How many times a solve needed one of the curve's tricks.</summary>
        public static int Uses(in WeightRater.Report r, Trick k)
        {
            switch (k)
            {
                case Trick.LineSingle:            return r.lineSingles;
                case Trick.LineToRegion:          return r.lineToRegionUses;
                case Trick.RegionToLine:          return r.regionToLineUses;
                case Trick.Squeeze:               return r.squeezeUses;
                case Trick.Subset2LinesToRegions: return r.subsetLineToRegionUses - r.subsetLineToRegion3Uses;
                case Trick.Subset2RegionsToLines: return r.subsetRegionToLineUses - r.subsetRegionToLine3Uses;
                case Trick.Subset3:               return r.subsetLineToRegion3Uses + r.subsetRegionToLine3Uses;
                case Trick.Choke:                 return r.regionChokeUses + r.lineChokeUses;
                case Trick.Fish:                  return r.fishUses;
            }
            return 0;
        }

        static readonly Trick[] AllTricks = (Trick[])System.Enum.GetValues(typeof(Trick));

        /// <summary>
        /// What is wrong with a board's tricks for this slot, or null when nothing is: a trick the level
        /// forbids (not opened yet, or too young to stand beside a new one), or the lesson's own trick
        /// needed too few / too many times.
        /// </summary>
        public static string TrickProblem(in LevelTarget t, in WeightRater.Report rep)
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

        public static bool TricksOk(in LevelTarget t, in WeightRater.Report rep) => TrickProblem(t, rep) == null;

        /// <summary>
        /// Accept/reject one rated candidate for a slot. <paramref name="seconds"/> is its
        /// <see cref="TimeRater"/> rating, taken once per board. Board size must match separately.
        /// </summary>
        public static bool PassesGates(in GenerationGates c, in LevelTarget t, in WeightRater.Report rep, double seconds)
        {
            if (!rep.solved || rep.trials > c.maxTrials) return false;
            if (!InTimeWindow(t, seconds)) return false;
            if (!TricksOk(t, rep)) return false;
            bool milestone = t.role == Role.Milestone;
            if (rep.paidSteps > (milestone ? c.milestoneMaxPaidSteps : c.maxPaidSteps)) return false;
            // breathers skip the wall / grind / start gates, so onboarding is not rejected
            if (t.role != Role.Breather && rep.paidSteps > 0)
            {
                if (rep.peak / (float)rep.weight > c.maxPeakShare) return false;            // wall
                double as10x10 = seconds / System.Math.Pow(c.secondsPerRow, rep.size - 10);
                if (as10x10 / rep.paidSteps < c.minSecondsPerStep) return false;            // grind
                // with few steps the first third always holds most of the rating, and that's
                // fine on an easy board — hence no start gate on breathers either
                bool hardSlot = milestone || t.role == Role.Peak;
                if (rep.startShare > (hardSlot ? c.peakMaxStartShare : c.maxStartShare)) return false;
            }
            return true;
        }
    }
}
