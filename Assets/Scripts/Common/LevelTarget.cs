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
        public float targetSeconds; // boardSeconds × placeFactor: what the regular player at this level takes ON THE BOARD
        public float minSeconds;    // accept window (target ± timeTol, milestones wider), real seconds
        public float maxSeconds;
        public float firstSeconds;  // on top of the board: the level teaches a trick for the first time (real seconds).
                                    // The level as a whole takes targetSeconds + firstSeconds; the board asked for is the lighter part.
        public Role role;
        public bool dog;            // the level opens with one queen already on the board (the builder picks which)
        public TrickRule tricks;    // which tricks the board may / must need (default: anything goes)
        public bool drawn;          // the board is built around a drawing from the pool (the builder picks which)
        public bool hard;           // the game shows this level as a hard one (the curve's word; the seconds above already say how hard)

        /// <summary>
        /// What the regular player at this level takes when the level holds a board of these (own) seconds:
        /// the board at this place in the game, and what a trick met for the first time adds on top.
        /// THE one place that turns a board's seconds into a level's — a view or a report that shows a
        /// level's seconds goes through here, so nothing the curve adds can go missing from it.
        /// <paramref name="first"/>: the first-time seconds that belong to THIS board — for a board the curve
        /// asks for that is <see cref="firstSeconds"/>; for a board that is already built it is what the built
        /// set really does (the level where that set first needs a trick), never what a later plan says.
        /// </summary>
        public double LevelSeconds(double boardSeconds, double first) => boardSeconds * placeFactor + first;

        /// <summary>The level's seconds as the curve asks for them (its board at the asked seconds).</summary>
        public double AskedLevelSeconds => targetSeconds + firstSeconds;
    }

    public enum Role { Breather, Normal, Build, Peak, Milestone }

    /// <summary>The tricks a curve can schedule. Subsets are split by how many lines they span:
    /// two is a small step up from the one-line tricks, three is a different animal.
    /// ColourClosed: a row or column is down to one cell only once the player sees that a queen's WHOLE
    /// colour is closed — cells that share its colour but not its row or column and do not touch it.
    /// Players cross out 4 of 5 row / column / touching cells and 1 of 5 of those; once learnt it is
    /// nearly free, before that it is a guess (recordings, Nov 2026).
    /// New tricks go at the END: the numbers are kept in the curve asset and in packed build jobs.</summary>
    public enum Trick
    {
        LineSingle, LineToRegion, RegionToLine, Squeeze,
        Subset2LinesToRegions, Subset2RegionsToLines, Subset3, Choke, Fish,
        ColourClosed,
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
        public int maxOthers;   // lesson levels: at most this many uses of other YOUNG tricks beside the lesson's (-1 = no limit)
        public int young;       // bit per Trick: the tricks counted for maxOthers — opened lately, still being learnt
        public bool Forbids(Trick k) => (forbidden & (1 << (int)k)) != 0;
    }
}
