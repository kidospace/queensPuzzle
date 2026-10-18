using System;

namespace QueensPuzzle
{
    /// <summary>
    /// Predicts how long an EXPERIENCED player takes to solve a level — learned from real play, not
    /// from preset trick prices. Seven measurements of the board and its solve, combined in log space:
    ///
    ///   size          board size n                                             (+ slower)
    ///   dryMoments    solve states with no easy move in sight — no last cell,
    ///                 no region confined to a line, no line of one colour        (+)
    ///   lineLastCell  queens found only as "last cell in a row/column"          (+)
    ///   singlesShare  share of queens placed by RegionSingle / LineSingle alone (queen shadows
    ///                 between them, no trick)                                    (− faster)
    ///   coverGain     extra share of queens "a queen here covers a whole colour" unlocks   (−)
    ///   subsetGain    extra share of queens the k-colours/k-lines subsets unlock          (−)
    ///   twist         how long and stringy the regions are (perimeter vs area)  (+)
    ///
    /// Fitted on ~1,050 campaign and daily boards against the active solve time of players already
    /// past level 100 (Jun–Oct 2026), with player speed factored out and hinted/boosted or abandoned
    /// solves counted as "at least this long". On boards it never saw it lands ~12 percentile points
    /// from the measured rank on average (the old weight: ~19). Refit after a regeneration.
    ///
    /// Pure C#; runs at build time.
    /// </summary>
    public static class DifficultyRater
    {
        // log(seconds) = C0 + Σ coefficient · measurement
        const double C0 = 1.985193;
        const double KSize = 0.249291;
        const double KDry = 0.033103;
        const double KLineLastCell = 0.043167;
        const double KCoverGain = -0.249969;
        const double KSinglesShare = -0.592189;
        const double KSubsetGain = -0.118673;
        const double KTwist = 0.361859;

        public struct Report
        {
            public bool solvable;      // false = the solver got stuck (the level would need a guess)
            public float seconds;      // predicted solve time of an average experienced player
            public int size;
            public int dryMoments;
            public int lineLastCell;
            public float singlesShare;
            public float coverGain;
            public float subsetGain;
            public float twist;
        }

        public static Report Rate(int n, int[] region, int[] solution)
        {
            var rep = new Report { size = n, solvable = true };

            // the solve itself: walk the solver's path and look before every move
            var b = new SolveTracer.Board(n, region, solution, null);
            for (int guard = 0; guard < 4000; guard++)
            {
                b.ApplyQueenScope();
                if (b.Solved || b.HasEmptyUnit()) break;
                if (!b.HasSimpleMove()) rep.dryMoments++;
                if (!b.NextStep(out var step)) { rep.solvable = false; break; }
                if (step.tech == SolveTechnique.LineSingle) rep.lineLastCell++;
            }

            // order-free: how much each trick set unlocks
            var probe = new SolveTracer.Board(n, region, solution, null);
            float Share(int mask) => probe.ProgressUnder(mask)[0] / (float)n;
            rep.singlesShare = Share(0);
            rep.coverGain = Share(1 << (int)SolveTechnique.RegionChoke) - rep.singlesShare;
            rep.subsetGain = Share((1 << (int)SolveTechnique.SubsetLineToRegion) | (1 << (int)SolveTechnique.SubsetRegionToLine)) - rep.singlesShare;

            rep.twist = Twist(n, region);

            double log = C0
                + KSize * n
                + KDry * rep.dryMoments
                + KLineLastCell * rep.lineLastCell
                + KCoverGain * rep.coverGain
                + KSinglesShare * rep.singlesShare
                + KSubsetGain * rep.subsetGain
                + KTwist * rep.twist;
            rep.seconds = (float)Math.Exp(log);
            return rep;
        }

        // Area-weighted stringiness: each region's perimeter over that of a square of the same
        // area (1 = compact, more = snaking), averaged over the board's cells.
        static float Twist(int n, int[] region)
        {
            var size = new int[n];
            var per = new int[n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int g = region[r * n + c];
                    size[g]++;
                    if (r == 0 || region[(r - 1) * n + c] != g) per[g]++;
                    if (r == n - 1 || region[(r + 1) * n + c] != g) per[g]++;
                    if (c == 0 || region[r * n + c - 1] != g) per[g]++;
                    if (c == n - 1 || region[r * n + c + 1] != g) per[g]++;
                }
            double sum = 0;
            for (int g = 0; g < n; g++)
                if (size[g] > 0) sum += per[g] / (4.0 * Math.Sqrt(size[g])) * size[g];
            return (float)(sum / (n * n));
        }
    }
}
