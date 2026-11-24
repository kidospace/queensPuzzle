using System;
using System.Collections.Generic;
using System.Linq;

namespace QueensPuzzle
{
    /// <summary>
    /// The rating of a board: the seconds a regular player needs for it. Time is the only measure —
    /// there is no other score of a board.
    ///
    /// Players do not solve a board along one road: most take the queen with the fewest places
    /// left, and reach positions a single solve never stood in. So a board is rated over MANY roads:
    ///
    ///   1. <see cref="Roads"/> walks the board the way players do: from each position, the next
    ///      queen is mostly the one with the fewest places left. That gives, queen by queen, the
    ///      positions players are likely to stand in and the share of players in each.
    ///   2. In every such position the solver is asked what it takes to place the next queen from
    ///      THERE, and that is priced in seconds (<see cref="QueenSeconds"/>) with prices measured
    ///      on the recordings — 650,000 queens in 16,000 positions (Android 1.5.4 and 1.5.6,
    ///      16–24 Nov 2026), each player's gap with his own pace and the level's place taken out.
    ///      A queen's price is two things: FINDING it, and the X MARKS the player makes first. The
    ///      game marks nothing by itself, so after a queen the player crosses out its row, column,
    ///      colour and neighbours — and that time lands before the NEXT queen. So every position
    ///      carries how many cells the queen placed just before newly closed. The first queen of an
    ///      empty board has none, which is the whole reason it is quick; a board that opens with a
    ///      queen on it has that queen's cells to cross out before the first move.
    ///      The same holds for a TRICK: the cells it crosses out stay crossed out. So a position also
    ///      carries what the tricks before it left on the board, the solver starts from the queens AND
    ///      those, and a trick is paid for once — at the queen it leads to — not again at the queens after.
    ///   3. The seconds are summed over the positions by their share, and set to the players' real
    ///      level times with the board size and how often each trick is needed (<see cref="Ln"/>).
    ///
    /// The walk also gives the board's SHAPE in seconds (<see cref="Time"/>): how far the heaviest
    /// queen stands above the average one, and how much of the time falls on the first third.
    ///
    /// <see cref="Ln"/> is fitted on 843 campaign boards, 8x8 to 11x11, played 1 Sep – 9 Nov 2026:
    /// first-try wins, each player's time divided by his own pace, the level's place in the game
    /// taken out — so this is the BOARD ALONE, whichever slot it lands in. Boards under 8x8 (the
    /// first twelve levels) are held to what the first twelve levels of the 1.5.4 set really took,
    /// with the curve's place factor out. On boards kept out of the fit: typically 7.7% off,
    /// 8 of 100 off by 20%+.
    ///
    /// The numbers in <see cref="QueenSeconds"/> and <see cref="Ln"/> are a fit, not prices — do
    /// not read them one by one, and refit them together when the solver, the trick list or the
    /// level set changes. The whole fit, start to end, is kept in the reports repo:
    /// pawdoku-reports/research/rater-fit (README there; this is its model "N_carry").
    ///
    /// Pure C# (no Unity types): thread-safe, runs on the pool-build workers. It costs a few dozen
    /// short solves per board.
    /// </summary>
    public static class TimeRater
    {
        /// <summary>What the rating says about a board.</summary>
        public struct Time
        {
            public double seconds;      // a regular, experienced player
            public float wall;          // the heaviest queen's seconds ÷ the average queen's (1 = all alike, 3 = one queen is a wall)
            public float start;         // the first third of the queens' seconds ÷ an even spread's (1 = even, above = starts hard)
        }

        /// <summary>Rates a board. <paramref name="revealedRows"/>: rows whose queen starts revealed on
        /// the board (null / empty = none) — pass the same ones the report was rated with.</summary>
        public static Time Rate(int n, int[] region, int[] sol, in WeightRater.Report r, int[] revealedRows = null)
        {
            var perQueen = new double[n - (revealedRows == null ? 0 : revealedRows.Length)];
            double roads = Roads(n, region, sol, revealedRows, perQueen);
            double wall = 0, start = 0;
            int third = (perQueen.Length + 2) / 3;
            for (int i = 0; i < perQueen.Length; i++)
            {
                if (perQueen[i] > wall) wall = perQueen[i];
                if (i < third) start += perQueen[i];
            }
            // both against an even spread of the time over the queens, so a small board (fewer queens,
            // each a bigger share) is not read as walled or front-loaded just for its size
            return new Time
            {
                seconds = Math.Exp(Ln(r, roads)),
                wall = (float)(wall / (roads / perQueen.Length)),
                start = (float)(start / (roads * third / perQueen.Length)),
            };
        }

        /// <summary>Seconds for a regular player, rounded.</summary>
        public static int Seconds(int n, int[] region, int[] sol, in WeightRater.Report r, int[] revealedRows = null)
            => (int)Math.Round(Rate(n, region, sol, r, revealedRows).seconds);

        /// <summary>The same, not rounded — what the slot windows are checked against.</summary>
        public static double SecondsExact(int n, int[] region, int[] sol, in WeightRater.Report r, int[] revealedRows = null)
            => Rate(n, region, sol, r, revealedRows).seconds;

        static double Ln(in WeightRater.Report r, double roads) =>
            - 0.05746
            + 0.95369 * Math.Log(roads)
            + 0.04121 * r.size
            + 0.01178 * r.lineSingles
            + 0.01337 * r.lineToRegionUses
            - 0.00959 * r.regionToLineUses
            + 0.00036 * r.squeezeUses
            + 0.02505 * (r.subsetLineToRegionUses + r.subsetRegionToLineUses + r.fishUses)
            + 0.06058 * (r.regionChokeUses + r.lineChokeUses);

        /// <summary>
        /// A cheap estimate from one solve alone, no roads walked (typically 6% from the rating). For a
        /// search that has to compare hundreds of near-identical boards a step — not a board's rating.
        /// </summary>
        public static double QuickSeconds(in WeightRater.Report r) => Math.Exp(
              2.17791
            + 0.23035 * r.size
            + 0.00111 * r.cycles
            + 0.05282 * r.lineSingles
            + 0.04247 * r.lineToRegionUses
            + 0.03093 * r.regionToLineUses
            + 0.04239 * r.squeezeUses
            + 0.10124 * (r.subsetLineToRegionUses + r.subsetRegionToLineUses + r.fishUses)
            + 0.06077 * (r.regionChokeUses + r.lineChokeUses));

        // ---- many roads ---------------------------------------------------------------

        const int MaxPositions = 25;       // per queen: the likeliest positions kept
        const double MinShare = 0.01;      // a position fewer than 1 player in 100 reaches is dropped

        // marks: the cells the queen placed just before newly closed — the X marks waiting before the
        // next queen — summed by share over the ways players reach this position
        // known: the cells tricks crossed out on the way here. A player's X marks stay on his board, so
        // a trick is paid for once — at the queen it led to — and the queens after it start from what
        // it left. Positions reached by several ways keep what the commonest way knows (knownShare).
        sealed class Position { public int[] placed; public double share; public double marks; public int[] known; public double knownShare; }
        static readonly int[] NothingKnown = new int[0];

        /// <summary>
        /// Seconds summed over the positions players are likely to stand in, by their share.
        /// The walk starts from the queens revealed at the start, and prices only the ones the
        /// player places himself. <paramref name="perQueen"/> gets the seconds of each of those, in order.
        /// </summary>
        static double Roads(int n, int[] region, int[] sol, int[] revealedRows, double[] perQueen)
        {
            var open = new bool[n * n];
            int[] row = new int[n], col = new int[n], reg = new int[n];
            int given = revealedRows == null ? 0 : revealedRows.Length;
            var start = new int[given];
            for (int i = 0; i < given; i++) start[i] = revealedRows[i] * n + sol[revealedRows[i]];
            Array.Sort(start);
            // a board that opens with a queen: its cells are there to cross out before the first move
            double startMarks = given == 0 ? 0 : n * n - Count(n, region, start, open, row, col, reg) - given;
            var here = new List<Position> { new Position { placed = start, share = 1, marks = startMarks, known = NothingKnown, knownShare = 1 } };
            double total = 0;

            for (int queen = given; queen < n; queen++)
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
                    int openHere = Count(n, region, p.placed, open, row, col, reg);

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

                    var learnt = new List<int>();
                    double q = share * Math.Max(1.0, QueenSeconds(n, region, sol, p.placed, best, p.marks / p.share, p.known, learnt, out int led));
                    total += q;
                    perQueen[queen - given] += q;
                    // Only the player who places the queen the tricks LED to has made them: he goes on with
                    // their X marks on his board. A player who places another queen keeps what he knew.
                    int[] afterTricks = learnt.Count == 0 ? p.known : p.known.Concat(learnt).Distinct().ToArray();

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
                        // what this queen closes (itself aside): the X marks before the queen after it
                        double marks = s * (openHere - Count(n, region, placed, open, row, col, reg) - 1);
                        int[] known = cand[i].cell == led ? afterTricks : p.known;
                        if (index.TryGetValue(key, out int at))
                        {
                            var t = next[at];
                            t.share += s; t.marks += marks;
                            if (s > t.knownShare) { t.known = known; t.knownShare = s; }
                        }
                        else { index[key] = next.Count; next.Add(new Position { placed = placed, share = s, marks = marks, known = known, knownShare = s }); }
                    }
                }
                here = next;
            }
            return total;
        }

        // What a player sees: a cell is open while no placed queen rules it out (same row, column
        // or colour, or touching). Counts the open cells of every row, column and region, and returns
        // how many are open on the whole board.
        static int Count(int n, int[] region, int[] placed, bool[] open, int[] row, int[] col, int[] reg)
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
            int all = 0;
            for (int i = 0; i < n * n; i++)
                if (open[i]) { row[i / n]++; col[i % n]++; reg[region[i]]++; all++; }
            return all;
        }

        /// <summary>
        /// Seconds the next queen costs from a position: the solver is run from there until it can
        /// place a queen, and what it needed on the way is priced. <paramref name="easiest"/> is
        /// the fewest places any unplaced queen has left — 1 means a queen is plainly forced.
        /// <paramref name="marks"/>: the X marks waiting before this queen — the cells the queen
        /// placed just before newly closed (0 on an empty board; a given queen's cells at the start).
        /// <paramref name="known"/>: the cells tricks crossed out earlier on the way to this position —
        /// they are still crossed out on the player's board, so the solver starts with them and only what
        /// it STILL needs is priced. <paramref name="learnt"/> gets the cells the tricks of this position
        /// cross out, <paramref name="led"/> the queen they lead to (-1 = none).
        /// </summary>
        static double QueenSeconds(int n, int[] region, int[] sol, int[] placed, int easiest, double marks,
                                   int[] known, List<int> learnt, out int led)
        {
            var s = new SolveTracer.Board(n, region);
            foreach (int c in placed) s.SeedQueen(c);
            foreach (int c in known) s.KnownX(c);
            led = -1;

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
                    if (st.kind == NodeKind.Placement)
                    {
                        byLine = st.tech == SolveTechnique.LineSingle; open = openNow;
                        if (st.cells != null && st.cells.Length > 0) led = st.cells[0];
                        break;
                    }
                    paid++;
                    cleared += Math.Log(1 + (st.cells == null ? 0 : st.cells.Length));
                    if (st.cells != null) learnt.AddRange(st.cells);
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
                learnt.Add(wrong);
                s.KnownX(wrong);
            }
            if (byLine) paid++;   // reading a line costs like a trick

            return -1.49426
                + 2.21303 * Math.Log(1 + marks)                // the X marks made before looking for this queen
                + (open < 6 ? -0.16611 : 0)
                + (open < 12 ? -0.75666 : 0)
                + 2.70162 * Math.Log(1 + open)
                + (paid > 0 ? -1.89857 : 0)
                + 0.93751 * Math.Min(paid, 6)
                + 9.05210 * Math.Log(1 + paid)
                + (byLine ? 0.24915 : 0)
                - 3.26160 * lineToRegion
                - 2.75330 * regionToLine
                - 0.69712 * squeeze
                - 1.29730 * subsets
                - 0.79043 * chokes
                + 1.10688 * cleared
                + 11.96861 * placed.Length / n
                + (n == 10 ? 0.65671 : 0)
                + (n == 9 ? -0.12458 : 0)
                + (easiest == 1 ? -9.64024 : easiest == 2 ? -7.23712 : easiest == 3 ? -4.19196 : 0);
        }
    }
}
