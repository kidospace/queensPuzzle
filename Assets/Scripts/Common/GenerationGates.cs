namespace QueensPuzzle
{
    /// <summary>
    /// Build-time accept/reject rules as plain data — what the pool builder consumes, blind to
    /// which curve produced the targets. Assembled on demand from <see cref="CampaignCurveConfig.Gates"/>;
    /// never serialized on its own, so the curve asset stays the single source of truth.
    /// </summary>
    public struct GenerationGates
    {
        public int maxTrials;
        public float maxWall;             // the heaviest queen's seconds ÷ the average queen's
        public float minSecondsPerStep;   // as on a 10x10 board; a step = one use of a trick
        public float secondsPerRow;       // to read any board size as 10x10
        public int maxSteps;              // uses of a trick in the whole solve
        public int milestoneMaxSteps;
        public float maxStart;            // the first third of the queens' seconds ÷ an even spread's
        public float peakMaxStart;
    }
}
