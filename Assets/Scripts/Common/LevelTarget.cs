namespace QueensPuzzle
{
    /// <summary>
    /// What one level asks of its board — plain data, no Unity types. A curve
    /// (<see cref="CampaignCurveConfig"/>, the daily tiers) turns a level number into one of these,
    /// and the pool builder fills it without knowing which curve it came from: the builder runs
    /// inside the editor and outside it on exactly this.
    /// </summary>
    public struct LevelTarget
    {
        public int level;
        public int boardSize;
        public float boardSeconds;  // the board the slot asks for, in the board's own seconds (TimeRater)
        public float placeFactor;   // real seconds at this level / the board's own seconds (1 = level ~300)
        public float targetSeconds; // boardSeconds × placeFactor: what the regular player at this level takes
        public float minSeconds;    // accept window (target ± timeTol, milestones wider), real seconds
        public float maxSeconds;
        public Role role;
        public bool dog;            // the level opens with one queen already on the board (the builder picks which)
        public TrickRule tricks;    // which tricks the board may / must need (default: anything goes)
        public bool drawn;          // the board is built around a drawing from the pool (the builder picks which)
    }

    public enum Role { Breather, Normal, Build, Peak, Milestone }

    /// <summary>The tricks a curve can schedule. Subsets are split by how many lines they span:
    /// two is a small step up from the one-line tricks, three is a different animal.</summary>
    public enum Trick
    {
        LineSingle, LineToRegion, RegionToLine, Squeeze,
        Subset2LinesToRegions, Subset2RegionsToLines, Subset3, Choke, Fish,
    }

    /// <summary>What a level asks of its board's tricks.</summary>
    public struct TrickRule
    {
        public int forbidden;   // bit per Trick: the board must not need it
        public bool lesson;     // this level is one of a trick's three lesson levels
        public Trick teach;     // the trick of the lesson
        public int stage;       // 1 learn · 2 practise · 3 prove
        public int minUses;     // how many times the board must need the lesson's trick
        public int maxUses;     // 0 = no limit
        public bool Forbids(Trick k) => (forbidden & (1 << (int)k)) != 0;
    }
}
