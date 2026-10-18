#if UNITY_EDITOR
using System.Collections.Generic;

namespace QueensPuzzle
{
    // The parts of SolveTracer only the level tools use — compiled into the editor, never into a
    // player build. It sits next to SolveTracer.cs (not in an Editor folder) because a partial class
    // must be compiled in one assembly.
    public static partial class SolveTracer
    {
        /// <summary>Trace depth limit: how many guesses may nest before a branch is left unresolved.</summary>
        public const int MaxDepth = 4;

        /// <summary>
        /// The full, human-readable solve trace for a level (every deduction and placement, not just
        /// queens) and, at each trial, the whole tree of options the player could pick — bounded to
        /// <see cref="MaxDepth"/> levels of nested guessing. Returned flattened (each node points to
        /// its parent) so it serializes and rebuilds into a tree. For the level builder's display.
        /// </summary>
        public static TraceNode[] Build(int n, int[] region, int[] solution)
        {
            var board = new Board(n, region, solution, new List<TraceNode>());
            board.Run(-1, 0, guided: true);
            return board.Nodes.ToArray();
        }

        public partial class Board
        {
            public Board(int n, int[] region) : this(n, region, null, null) { }

            public int OpenCells() { int k = 0; for (int i = 0; i < cand.Length; i++) if (C(i)) k++; return k; }

            // ---- the recursive trace ----

            int Add(int parent, NodeKind kind, SolveTechnique tech, Outcome outcome, TraceMark[] marks, string note, int cost = 0, bool streak = false)
            {
                Nodes.Add(new TraceNode { parent = parent, technique = tech, kind = kind, outcome = outcome, marks = marks, note = note, cost = cost, streak = streak });
                return Nodes.Count - 1;
            }

            void SetOutcome(int idx, Outcome o) { var nd = Nodes[idx]; nd.outcome = o; Nodes[idx] = nd; }

            static readonly TraceMark[] NoMarks = new TraceMark[0];
            static TraceMark[] QueenAt(int idx) => new[] { new TraceMark { cell = idx, mark = CellMark.Queen } };

            static TraceMark[] XMarks(int[] cells)
            {
                var a = new TraceMark[cells.Length];
                for (int i = 0; i < cells.Length; i++) a[i] = new TraceMark { cell = cells[i], mark = CellMark.X };
                return a;
            }

            public Outcome Run(int parent, int depth, bool guided)
            {
                int cur = parent;
                var prevTech = SolveTechnique.None; // last paid trick — a repeat halves the think cost
                while (true)
                {
                    if (HasEmptyUnit()) return Outcome.DeadEnd;
                    if (placed == n) return Outcome.Solved;

                    int open = OpenCells();
                    if (NextStep(out var step))
                    {
                        bool streak = step.tech == prevTech && TrickWeights.Streakable(step.tech);
                        if (TrickWeights.Streakable(step.tech)) prevTech = step.tech;
                        var marks = step.kind == NodeKind.Placement ? QueenAt(step.cells[0]) : XMarks(step.cells);
                        cur = Add(cur, step.kind, step.tech, Outcome.Continues, marks, step.note,
                            TrickWeights.StepCost(step.tech, step.k, open, streak), streak);
                        continue;
                    }

                    // stuck → trial
                    if (depth >= MaxDepth)
                    {
                        Add(cur, NodeKind.TrialRoot, SolveTechnique.Trial, Outcome.Unresolved,
                            NoMarks, $"would need a deeper guess (depth limit {MaxDepth})");
                        return Outcome.Unresolved;
                    }
                    var unit = MostConstrainedUnitCells(out string uname);
                    if (unit == null) return Outcome.DeadEnd;
                    int trial = Add(cur, NodeKind.TrialRoot, SolveTechnique.Trial, Outcome.Continues,
                        NoMarks, $"stuck — guess in {uname} ({unit.Count} options)");

                    int rightBranch = -1, rightCell = -1;
                    foreach (int cell in unit)
                    {
                        int branch = Add(trial, NodeKind.TrialBranch, SolveTechnique.Trial, Outcome.Continues,
                            QueenAt(cell), $"try a queen in {uname}");
                        if (guided && sol[cell / n] == cell % n) { rightBranch = branch; rightCell = cell; continue; } // the real path continues here
                        var clone = Clone();
                        clone.Place(cell / n, cell % n);
                        Outcome outc = clone.Run(branch, depth + 1, guided: false);
                        SetOutcome(branch, outc == Outcome.Solved ? Outcome.Continues : outc);
                    }

                    if (!guided || rightCell < 0) return Outcome.DeadEnd; // an impossible line — every option exhausted
                    Place(rightCell / n, rightCell % n);
                    cur = rightBranch;
                    prevTech = SolveTechnique.None;      // a guess is a context switch — streak broken
                }
            }

            // ---- difficulty probes (DifficultyRater) ----

            /// <summary>Applies every pending queen shadow (row/column/region/touch eliminations).</summary>
            public void ApplyQueenScope() { while (TryQueenElim(out _)) { } }

            /// <summary>
            /// Is an easy move in sight — a last cell (row, column or region), or a region confined to
            /// one line / a line that is one colour? Call after <see cref="ApplyQueenScope"/>.
            /// Does not change the board.
            /// </summary>
            public bool HasSimpleMove()
            {
                if (RegionSingle() >= 0 || LineSingle(out _, out _) >= 0) return true;
                return Clone().TryLineToRegion(out _) || Clone().TryRegionToLine(out _);
            }

            /// <summary>
            /// How far the board gets using ONLY singles, queen shadows and the tricks in
            /// <paramref name="mask"/> (bit = 1 &lt;&lt; (int)SolveTechnique). Eliminations only ever remove
            /// candidates, so the result does not depend on the order the tricks fire in.
            /// <paramref name="maxSubsetK"/> caps the size of the subset tricks (2 = only two colours/lines).
            /// Returns { queens placed, open cells left }. Does not change the board.
            /// </summary>
            public int[] ProgressUnder(int mask, int maxSubsetK = int.MaxValue)
            {
                var b = Clone();
                bool On(SolveTechnique t) => (mask & (1 << (int)t)) != 0;
                for (int guard = 0; guard < 5000; guard++)
                {
                    if (b.placed == n || b.HasEmptyUnit()) break;
                    if (b.TryQueenElim(out _)) continue;
                    int idx = b.RegionSingle(); if (idx >= 0) { b.Place(idx / n, idx % n); continue; }
                    idx = b.LineSingle(out _, out _); if (idx >= 0) { b.Place(idx / n, idx % n); continue; }
                    if (On(SolveTechnique.LineToRegion) && b.TryLineToRegion(out _)) continue;
                    if (On(SolveTechnique.RegionToLine) && b.TryRegionToLine(out _)) continue;
                    if (On(SolveTechnique.Squeeze) && b.TrySqueeze(out _)) continue;
                    if (On(SolveTechnique.RegionChoke) && b.TryRegionChoke(out _)) continue;
                    bool fired = false;
                    for (int k = 2; k < n && !fired; k++)
                        fired = (k <= maxSubsetK && On(SolveTechnique.SubsetLineToRegion) && b.TrySubsetLineToRegion(k, out _))
                             || (k <= maxSubsetK && On(SolveTechnique.SubsetRegionToLine) && b.TrySubsetRegionToLine(k, out _))
                             || (On(SolveTechnique.Fish) && b.TryFish(k, out _));
                    if (fired) continue;
                    if (On(SolveTechnique.LineChoke))
                        for (int k = 2; k <= 6 && !fired; k++) fired = b.TryLineChoke(k, out _);
                    if (fired) continue;
                    break;
                }
                return new[] { b.placed, b.OpenCells() };
            }
        }
    }
}
#endif
