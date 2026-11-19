using System;

namespace QueensPuzzle
{
    /// <summary>
    /// Solves a board the way a human would and reports what the solve needed: how often each
    /// trick, how many steps, whether it had to guess. The board's rating is its TIME
    /// (<see cref="TimeRater"/>), which is built on this report.
    ///
    /// Queens revealed at the start of a level (LevelData.revealedRows) are on the board before the
    /// player's first move, so the solve starts from them — a trick they make unnecessary is not counted.
    ///
    /// <see cref="Report.weight"/> is the older score of a board: the sum of a hand-set cost per step
    /// (<see cref="TrickWeights"/>), plus the cost of guessing where the solve is stuck. Nothing in
    /// the level tools reads it any more. It is still computed because the GAME does: it is stamped on
    /// every level and shipped in the level pack (the daily challenge's "faster than" line, the win-stats
    /// server). When the game stops using it, it goes — and TrickWeights with it.
    ///
    /// Pure C# (no Unity types) so it runs at build time, inside the editor and outside it.
    /// </summary>
    public static class WeightRater
    {
        public const int GuessSetup = 200;   // flat cost of a guess — always above every trick (the worst move in the game)
        const int Nest = 3;            // multiplier per nesting level past the first
        const int MaxProbe = 3;        // beyond this a stuck branch pays a flat wall
        const int DeepWall = 300;
        const int StepCap = 1_500_000; // safety net against pathological recursion

        public struct Report
        {
            public bool solved;
            public string technique;
            public int weight;           // the older score, kept for the game only (see the class note)
            public int size;
            public int cycles;
            public int placements;
            public int regionSingles;
            public int lineSingles;
            public int eliminations;
            public int lineToRegionUses;
            public int regionToLineUses;
            public int squeezeUses;
            public int regionChokeUses;
            public int subsetLineToRegionUses;
            public int subsetRegionToLineUses;
            public int subsetLineToRegion3Uses;   // of the subset uses above, those over 3 lines or more
            public int subsetRegionToLine3Uses;
            public int fishUses;
            public int lineChokeUses;
            public int trials;           // guesses the solve needed (a board with any is never built)
            public int[] techUses;       // uses per SolveTechnique
        }

        /// <param name="revealedRows">Rows whose queen starts revealed on the board (null / empty = none).</param>
        public static Report Rate(int n, int[] region, int[] solution, int[] revealedRows = null)
        {
            var m = new Metrics();
            var ctx = new Ctx();
            var s = new SolveTracer.Board(n, region);
            if (revealedRows != null)
                foreach (int row in revealedRows) s.SeedQueen(row * n + solution[row]);
            int weight = 0;
            bool solved = true;
            var prevTech = SolveTechnique.None; // last paid trick — a repeat halves the think cost

            bool prevWasGuess = false;   // consecutive guesses at one stuck point pay the setup once

            while (!s.Solved)
            {
                if (ctx.aborted) break;
                if (s.HasEmptyUnit()) { solved = false; break; } // not expected on the real path
                m.cycles++;

                int open = s.OpenCells();
                if (s.NextStep(out var step))
                {
                    bool streak = step.tech == prevTech && TrickWeights.Streakable(step.tech);
                    if (TrickWeights.Streakable(step.tech)) prevTech = step.tech;
                    weight += Tally(step, open, streak, m);
                    prevWasGuess = false;   // a deduction resumed — the guess run (if any) is over
                    continue;
                }
                prevTech = SolveTechnique.None; // a guess is a context switch — streak broken

                // stuck → guess in the most constrained UNIT (row/column/region — fewest options
                // wins): X the wrong cell that is cheapest to disprove
                var unit = s.MostConstrainedUnitCells(out _);
                if (unit == null) { solved = false; break; }
                int best = -1, bestCost = int.MaxValue;
                foreach (int cell in unit)
                {
                    if (solution[cell / n] == cell % n) continue; // never eliminate the real cell
                    var b = s.Clone();
                    b.Place(cell / n, cell % n);
                    int cost = GuessCost(b, m, ctx, 1);
                    if (cost < bestCost) { bestCost = cost; best = cell; }
                }
                if (best < 0) { solved = false; break; }
                m.trials++;
                // a human can't know which option is the true one — one inconclusive test of
                // the true cell is paid once per stuck point (folded into the run's first X)
                if (!prevWasGuess) bestCost += GuessSetup;
                weight += bestCost;
                prevWasGuess = true;
                s.KnownX(best);
            }

            if (ctx.aborted) { solved = true; weight = Math.Max(weight, 500); }

            var rep = new Report
            {
                solved = solved && s.Solved,
                size = n,
                weight = weight,
                cycles = m.cycles,
                placements = m.placements,
                regionSingles = m.regionSingles,
                lineSingles = m.lineSingles,
                eliminations = m.eliminations,
                lineToRegionUses = m.lineToRegionUses,
                regionToLineUses = m.regionToLineUses,
                squeezeUses = m.squeezeUses,
                regionChokeUses = m.regionChokeUses,
                subsetLineToRegionUses = m.subsetLineToRegionUses,
                subsetRegionToLineUses = m.subsetRegionToLineUses,
                subsetLineToRegion3Uses = m.subsetLineToRegion3Uses,
                subsetRegionToLine3Uses = m.subsetRegionToLine3Uses,
                fishUses = m.fishUses,
                lineChokeUses = m.lineChokeUses,
                trials = m.trials,
                techUses = m.techUses,
            };
            rep.technique = TechniqueName(m, rep.solved);
            return rep;
        }

        static string TechniqueName(Metrics m, bool solved)
        {
            if (!solved) return "unsolved";
            if (m.trials > 0) return m.maxTrialDepth >= 2 ? "trial (nested)" : "trial";
            if (m.lineChokeUses > 0) return "line choke";
            if (m.fishUses > 0) return "positional fish";
            if (m.regionChokeUses > 0) return "region choke";
            if (m.subsetRegionToLineUses > 0) return "subset (region→line)";
            if (m.subsetLineToRegionUses > 0) return "subset (line→region)";
            if (m.squeezeUses > 0) return "squeeze";
            if (m.regionToLineUses > 0) return "region→line";
            if (m.lineToRegionUses > 0) return "line→region";
            if (m.lineSingles > 0) return "line single";
            return "region single";
        }

        // Cost of one deduction step found on a board with `open` candidate cells, plus metrics.
        // streak = same trick as the previous paid step → think cost halved (pattern still loaded).
        static int Tally(SolveTracer.Board.DeductionStep step, int open, bool streak, Metrics m)
        {
            m.techUses[(int)step.tech]++;
            switch (step.tech)
            {
                case SolveTechnique.QueenScope:         m.eliminations += step.cells.Length; return 0; // free — no scan
                case SolveTechnique.RegionSingle:       m.placements++; m.regionSingles++;   break;
                case SolveTechnique.LineSingle:         m.placements++; m.lineSingles++;     break;
                case SolveTechnique.LineToRegion:       m.lineToRegionUses++;       m.eliminations += step.cells.Length; break;
                case SolveTechnique.RegionToLine:       m.regionToLineUses++;       m.eliminations += step.cells.Length; break;
                case SolveTechnique.Squeeze:            m.squeezeUses++;            m.eliminations += step.cells.Length; break;
                case SolveTechnique.RegionChoke:        m.regionChokeUses++;        m.eliminations += step.cells.Length; break;
                case SolveTechnique.SubsetLineToRegion: m.subsetLineToRegionUses++; if (step.k >= 3) m.subsetLineToRegion3Uses++; m.eliminations += step.cells.Length; break;
                case SolveTechnique.SubsetRegionToLine: m.subsetRegionToLineUses++; if (step.k >= 3) m.subsetRegionToLine3Uses++; m.eliminations += step.cells.Length; break;
                case SolveTechnique.Fish:               m.fishUses++;               m.eliminations += step.cells.Length; break;
                case SolveTechnique.LineChoke:         m.lineChokeUses++;         m.eliminations += step.cells.Length; break;
            }
            return TrickWeights.StepCost(step.tech, step.k, open, streak);
        }

        // Cost of following one wrong hypothesis to its contradiction (cheapest path wins).
        // Chain work is tallied into a throwaway Metrics — hypothetical moves aren't solve steps.
        static int GuessCost(SolveTracer.Board s, Metrics m, Ctx ctx, int depth)
        {
            if (ctx.aborted) return 0;
            m.maxTrialDepth = Math.Max(m.maxTrialDepth, depth);

            var t = new Metrics();
            int chain = 0;
            var prevTech = SolveTechnique.None;
            while (true)
            {
                if (++ctx.steps > StepCap) { ctx.aborted = true; return GuessSetup + chain; }
                if (s.HasEmptyUnit()) return GuessSetup + chain;   // contradiction reached
                if (s.Solved) return GuessSetup + chain;           // cannot happen on a wrong branch
                int open = s.OpenCells();
                if (!s.NextStep(out var step)) break;              // stuck → must nest deeper
                bool streak = step.tech == prevTech && TrickWeights.Streakable(step.tech);
                if (TrickWeights.Streakable(step.tech)) prevTech = step.tech;
                chain += Tally(step, open, streak, t);
            }

            if (depth >= MaxProbe) return GuessSetup + chain + DeepWall;

            var unit = s.MostConstrainedUnitCells(out _);
            if (unit == null) return GuessSetup + chain;
            int best = int.MaxValue;
            foreach (int cell in unit)
            {
                var b = s.Clone();
                b.Place(cell / s.N, cell % s.N);
                int cost = GuessCost(b, m, ctx, depth + 1);
                if (cost < best) best = cost;
            }
            return best == int.MaxValue ? GuessSetup + chain : GuessSetup + chain + Nest * best;
        }

        class Ctx { public int steps; public bool aborted; }

        class Metrics
        {
            public int cycles, placements, regionSingles, lineSingles, eliminations,
                lineToRegionUses, regionToLineUses, squeezeUses, regionChokeUses,
                subsetLineToRegionUses, subsetRegionToLineUses, subsetLineToRegion3Uses, subsetRegionToLine3Uses,
                fishUses, lineChokeUses, trials, maxTrialDepth;
            public int[] techUses = new int[16];
        }
    }
}
