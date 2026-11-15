using System;
using System.Collections.Generic;
using System.Linq;

namespace QueensPuzzle
{
    /// <summary>
    /// The calibrated rating: seconds a regular player needs for a board.
    ///
    /// <see cref="WeightRater"/> solves a board along ONE road, cheapest trick first. Players are on
    /// that road for only about 6 queens in 10 — most take the queen with the fewest places left,
    /// and reach positions the rater never stood in. So a board is rated over MANY roads:
    ///
    ///   1. <see cref="Roads"/> walks the board the way players do: from each position, the next
    ///      queen is mostly the one with the fewest places left. That gives, queen by queen, the
    ///      positions players are likely to stand in and the share of players in each.
    ///   2. In every such position the solver is asked what it takes to place the next queen from
    ///      THERE, and that is priced in seconds (<see cref="QueenSeconds"/>) with prices measured
    ///      on the recordings — 2.5 million queens, each player's gap divided by his own pace.
    ///   3. The seconds are summed over the positions by their share, and joined with the rater's
    ///      own report (<see cref="Ln"/>).
    ///
    /// Fitted on recordings of 22 Oct – 10 Nov 2026: 1,017 campaign levels, boards 8x8 to 11x11,
    /// first-try wins, the level's place in the game fitted apart and left out — so this is the
    /// BOARD ALONE, whichever slot it lands in. A slot asks in the same unit:
    /// <see cref="CampaignCurveConfig.SecondsOf"/>.
    ///
    /// On levels kept out of the fit (both stages):
    ///                         typical miss   off 20%+ of 100   off 30%+ of 100
    ///   the weight alone          15%              36                19
    ///   one road, re-weighed       7.8%            11                 2
    ///   many roads (this)          7.0%             4                 0
    ///
    /// The numbers below are a fit, not prices: some are negative because the fields overlap. Do
    /// not read them one by one, and refit them all together when the rater, the trick list or the
    /// level set changes.
    ///
    /// Pure C# (no Unity types): thread-safe, runs on the pool-build workers. It costs a few dozen
    /// short solves per board; where a search compares hundreds of near-identical boards a step,
    /// <see cref="QuickSeconds"/> is the one-road estimate from the report alone.
    /// </summary>
    public static class TimeRater
    {
        /// <summary>Seconds for a regular player, rounded.</summary>
        public static int Seconds(int n, int[] region, int[] sol, in WeightRater.Report r)
            => (int)Math.Round(SecondsExact(n, region, sol, r));

        /// <summary>The same, not rounded — what the slot windows are checked against.</summary>
        public static double SecondsExact(int n, int[] region, int[] sol, in WeightRater.Report r)
            => Math.Exp(Ln(r, Roads(n, region, sol)));

        static double Ln(in WeightRater.Report r, double roads) =>
            - 0.32580
            + 0.67963 * Math.Log(roads)
            + 0.14294 * r.size
            + 0.29139 * Math.Log(1 + r.weight)
            - 0.20205 * Math.Log(1 + r.findCost)
            - 0.08050 * Math.Log(1 + r.thinkCost)
            + 0.02052 * Math.Log(1 + r.peak)
            + 0.00389 * r.cycles
            - 0.02084 * r.paidSteps
            + 0.01863 * r.lineSingles
            + 0.02436 * r.lineToRegionUses
            + 0.01199 * r.regionToLineUses
            + 0.00697 * r.squeezeUses
            + 0.01817 * (r.subsetLineToRegionUses + r.subsetRegionToLineUses + r.fishUses)
            - 0.02969 * (r.regionChokeUses + r.lineChokeUses);

        /// <summary>
        /// One road only, from the report alone (typically 7.8% off, against 7.0%). For a search
        /// that has to compare many boards cheaply — not a board's rating.
        /// </summary>
        public static double QuickSeconds(in WeightRater.Report r) => Math.Exp(
              1.16955
            + 0.19286 * r.size
            + 0.32067 * Math.Log(1 + r.weight)
            + 0.09986 * Math.Log(1 + r.findCost)
            - 0.09196 * Math.Log(1 + r.thinkCost)
            + 0.04365 * Math.Log(1 + r.peak)
            + 0.00358 * r.cycles
            - 0.03108 * r.paidSteps
            + 0.04106 * r.lineSingles
            + 0.04323 * r.lineToRegionUses
            + 0.01860 * r.regionToLineUses
            + 0.01867 * r.squeezeUses
            + 0.04320 * (r.subsetLineToRegionUses + r.subsetRegionToLineUses + r.fishUses)
            - 0.01702 * (r.regionChokeUses + r.lineChokeUses));

        // ---- many roads ---------------------------------------------------------------

        const int MaxPositions = 25;       // per queen: the likeliest positions kept
        const double MinShare = 0.01;      // a position fewer than 1 player in 100 reaches is dropped

        sealed class Position { public int[] placed; public double share; }

        /// <summary>
        /// Seconds summed over the positions players are likely to stand in, by their share.
        /// </summary>
        static double Roads(int n, int[] region, int[] sol)
        {
            var open = new bool[n * n];
            int[] row = new int[n], col = new int[n], reg = new int[n];
            var here = new List<Position> { new Position { placed = new int[0], share = 1 } };
            double total = 0;

            for (int queen = 0; queen < n; queen++)
            {
                here.RemoveAll(p => p.share < MinShare);
                here = here.OrderByDescending(p => p.share).ToList();   // stable: ties keep their order
                if (here.Count > MaxPositions) here.RemoveRange(MaxPositions, here.Count - MaxPositions);
                double kept = 0;
                foreach (var p in here) kept += p.share;

                var next = new List<Position>();
                var index = new Dictionary<string, int>();
                foreach (var p in here)
                {
                    double share = p.share / kept;
                    Count(n, region, p.placed, open, row, col, reg);

                    // the unplaced queens, each with the fewest places left in its row, column or region
                    int best = int.MaxValue;
                    var cand = new List<(int cell, int places, bool byRegion)>();
                    for (int r = 0; r < n; r++)
                    {
                        int cell = r * n + sol[r];
                        if (Array.IndexOf(p.placed, cell) >= 0) continue;
                        int places = Math.Min(row[r], Math.Min(col[sol[r]], reg[region[cell]]));
                        cand.Add((cell, places, reg[region[cell]] == places));
                        if (places < best) best = places;
                    }

                    total += share * Math.Max(1.0, QueenSeconds(n, region, sol, p.placed, best));

                    // who goes where: mostly the tightest queen, a colour read before a line
                    double sum = 0;
                    var w = new double[cand.Count];
                    for (int i = 0; i < cand.Count; i++)
                    {
                        w[i] = cand[i].places == best ? (cand[i].byRegion ? 3.0 : 1.0)
                             : cand[i].places == best + 1 ? 0.3 : 0.05;
                        sum += w[i];
                    }
                    for (int i = 0; i < cand.Count; i++)
                    {
                        var placed = new int[p.placed.Length + 1];
                        Array.Copy(p.placed, placed, p.placed.Length);
                        placed[p.placed.Length] = cand[i].cell;
                        Array.Sort(placed);
                        string key = string.Join(",", placed);
                        double s = p.share * w[i] / sum;
                        if (index.TryGetValue(key, out int at)) next[at].share += s;
                        else { index[key] = next.Count; next.Add(new Position { placed = placed, share = s }); }
                    }
                }
                here = next;
            }
            return total;
        }

        // What a player sees: a cell is open while no placed queen rules it out (same row, column
        // or colour, or touching). Counts the open cells of every row, column and region.
        static void Count(int n, int[] region, int[] placed, bool[] open, int[] row, int[] col, int[] reg)
        {
            for (int i = 0; i < n * n; i++) open[i] = true;
            foreach (int q in placed)
            {
                int qr = q / n, qc = q % n, g = region[q];
                for (int i = 0; i < n * n; i++)
                {
                    int r = i / n, c = i % n;
                    if (r == qr || c == qc || region[i] == g || (Math.Abs(r - qr) <= 1 && Math.Abs(c - qc) <= 1)) open[i] = false;
                }
            }
            Array.Clear(row, 0, n); Array.Clear(col, 0, n); Array.Clear(reg, 0, n);
            for (int i = 0; i < n * n; i++)
                if (open[i]) { row[i / n]++; col[i % n]++; reg[region[i]]++; }
        }

        /// <summary>
        /// Seconds the next queen costs from a position: the solver is run from there until it can
        /// place a queen, and what it needed on the way is priced. <paramref name="easiest"/> is
        /// the fewest places any unplaced queen has left — 1 means a queen is plainly forced.
        /// </summary>
        static double QueenSeconds(int n, int[] region, int[] sol, int[] placed, int easiest)
        {
            var s = new SolveTracer.Board(n, region);
            foreach (int c in placed) s.SeedQueen(c);

            int paid = 0, open = 0;
            int lineToRegion = 0, regionToLine = 0, squeeze = 0, subsets = 0, chokes = 0;
            bool byLine = false;
            double cleared = 0;   // sum of ln(1 + cells a paid trick cleared)

            for (int guard = 0; !s.Solved && guard < 2000; guard++)
            {
                if (s.HasEmptyUnit()) break;
                int openNow = s.OpenCells();
                if (s.NextStep(out var st))
                {
                    if (st.tech == SolveTechnique.QueenScope) continue;
                    if (st.kind == NodeKind.Placement) { byLine = st.tech == SolveTechnique.LineSingle; open = openNow; break; }
                    paid++;
                    cleared += Math.Log(1 + (st.cells == null ? 0 : st.cells.Length));
                    switch (st.tech)
                    {
                        case SolveTechnique.LineToRegion: lineToRegion++; break;
                        case SolveTechnique.RegionToLine: regionToLine++; break;
                        case SolveTechnique.Squeeze: squeeze++; break;
                        case SolveTechnique.SubsetLineToRegion:
                        case SolveTechnique.SubsetRegionToLine:
                        case SolveTechnique.Fish: subsets++; break;
                        case SolveTechnique.RegionChoke:
                        case SolveTechnique.LineChoke: chokes++; break;
                    }
                    continue;
                }
                // stuck: a guess — one wrong option is crossed out and the solve goes on
                var unit = s.MostConstrainedUnitCells(out _);
                if (unit == null) break;
                int wrong = -1;
                foreach (int c in unit) if (sol[c / n] != c % n) { wrong = c; break; }
                if (wrong < 0) break;
                paid++;
                s.KnownX(wrong);
            }
            if (byLine) paid++;   // reading a line costs like a trick

            return -10.60713
                + (placed.Length == 0 ? -8.10833 : 0)          // the first queen: the board is still being read
                + (open < 6 ? -0.65191 : 0)
                + (open < 12 ? -1.74372 : 0)
                + 5.68797 * Math.Log(1 + open)
                + (paid > 0 ? -0.82367 : 0)
                + 1.63883 * Math.Min(paid, 6)
                + 6.18022 * Math.Log(1 + paid)
                + (byLine ? 1.41840 : 0)
                - 3.81141 * lineToRegion
                - 3.99086 * regionToLine
                - 0.34381 * squeeze
                - 2.02558 * subsets
                + 0.89525 * chokes
                + 2.00458 * cleared
                + 17.90616 * placed.Length / n
                + (n == 10 ? -0.70828 : 0)
                + (n == 9 ? -1.13882 : 0)
                + (easiest == 1 ? -2.50242 : easiest == 2 ? 1.16930 : easiest == 3 ? 5.97434 : 7.97545);
        }
    }
}
