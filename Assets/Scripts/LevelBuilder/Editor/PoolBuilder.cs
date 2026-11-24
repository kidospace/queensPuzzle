using System;
using System.Collections.Generic;
using System.Threading;

namespace QueensPuzzle
{
    /// <summary>
    /// The pool build itself: generate → rate → fit into any open slot at the slot's FIXED
    /// window — no widening, a board that fits nothing is discarded. No Unity types, so it runs the
    /// same inside the editor (<see cref="PoolBuildRunner"/>) and outside it (Tools/LevelGen).
    ///
    /// It only knows a list of <see cref="LevelTarget"/>s and the <see cref="GenerationGates"/>:
    /// whoever calls it reads the curve and decides what happens to a filled slot.
    ///
    /// A DRAWN slot (<see cref="LevelTarget.drawn"/>) takes no random board: a drawing from the pool
    /// is completed again and again until a completion passes the slot's window and gates like any
    /// other board. Which drawing: one of the slot's board size, of a kind no level nearby was built
    /// from, the kinds used least first — so a kind with many look-alikes gets no more turns than
    /// any other. A drawing that gives the slot nothing in <see cref="TriesPerDrawing"/> tries
    /// makes way for the next one.
    /// </summary>
    public static class PoolBuilder
    {
        public const int TriesPerDrawing = 3000;

        public struct Options
        {
            public int threadCount;
            public int seedBase;
            public float maxSeconds;  // stop after this long and leave the rest open (0 = until done or stopped)
            public LevelColors.Rules colors;   // every built level is coloured by these (null = left uncoloured)
            public List<StencilFiller.Drawing> drawings;   // the pool the drawn slots are built from (null = none)
            public List<(int level, int drawing)> drawn;   // the set's levels already built from a drawing: their kinds are taken nearby, and counted
            public int kindApart;                          // two levels this close are never built from the same kind of drawing
        }

        /// <summary>A slot that found its board.</summary>
        public struct Filled
        {
            public LevelTarget slot;
            public int[] region;
            public int[] cols;
            public int weight;
            public int seed;
            public int[] revealedRows;   // null = no queen revealed at the start
            public int[] regionColors;   // palette colour per region; null = region k shows colour k
            public int drawing;          // the drawing the board was built around; 0 = an ordinary board
        }

        // a drawing as the workers use it
        sealed class Stencil
        {
            public StencilFiller.Drawing d;
            public int[] ids;      // region id per cell, -1 = empty
            public int[] colors;   // palette colour of each drawn region
        }

        // one drawn slot working on one drawing
        sealed class DrawTask
        {
            public Stencil s;
            public int level;
            public int tries;
        }

        // shared with worker threads during generation
        sealed class SharedState
        {
            public int nextSeed;
            public long attempts;
            public bool stop;
            public int[] sizes;
            public DrawTask[] tasks;
        }

        // what the drawn slots have used and given up on — the calling thread's only
        sealed class DrawnState
        {
            public readonly List<Stencil> pool = new List<Stencil>();
            public readonly List<(int level, string kind)> placed = new List<(int, string)>();
            public readonly Dictionary<int, int> drawingUses = new Dictionary<int, int>();
            public readonly Dictionary<int, int> gaveUp = new Dictionary<int, int>();   // level → drawings it moved on from
            public readonly Dictionary<(int level, int drawing), DrawTask> tasks = new Dictionary<(int, int), DrawTask>();

            public void Place(int level, StencilFiller.Drawing d)
            {
                placed.Add((level, d.Kind));
                drawingUses.TryGetValue(d.number, out int n);
                drawingUses[d.number] = n + 1;
            }
        }

        /// <summary>
        /// Fills the open slots until done, the time is up, or <paramref name="keepGoing"/> says stop.
        /// Mutates <paramref name="open"/> (filled slots are removed). Both callbacks run on the
        /// calling thread: <paramref name="onFilled"/> once per filled slot, <paramref name="keepGoing"/>
        /// (levels filled, slots open, boards generated) about every 10 ms — return false to stop.
        /// A board that fits a slot but that no colour set fits is discarded like any other misfit.
        /// Returns (levels filled, boards generated, boards discarded for their colours, drawn slots
        /// built as ordinary ones because the pool has no drawing for them).
        /// </summary>
        public static (int made, long attempts, int uncolourable, int undrawn) Run(GenerationGates gates, List<LevelTarget> open, Options opt,
            Action<Filled> onFilled, Func<int, int, long, bool> keepGoing)
        {
            var queue = new System.Collections.Concurrent.ConcurrentQueue<(int[] region, int[] cols, int seed, WeightRater.Report rep, TimeRater.Time time, int size, Stencil stencil)>();
            int made = 0, uncolourable = 0, undrawn = 0;

            var ds = new DrawnState();
            if (opt.drawings != null)
                foreach (var d in opt.drawings)
                {
                    int[] ids = StencilFiller.FromPalette(d.paint, out int[] colors);
                    if (colors.Length == 0 || colors.Length >= d.size || StencilFiller.ValidateStencil(d.size, ids) != null) continue;   // cannot be completed
                    ds.pool.Add(new Stencil { d = d, ids = ids, colors = colors });
                    if (opt.drawn != null)
                        foreach (var (level, number) in opt.drawn)
                            if (number == d.number) ds.Place(level, d);
                }

            // workers: generate + rate off the calling thread (TryGenerateRaw, TryFillRaw, Rate and TimeRater are thread-safe)
            var st = new SharedState { nextSeed = opt.seedBase - 1, tasks = Plan(open, ds, opt.kindApart, ref undrawn) };
            st.sizes = SizesOf(open);   // after the plan: a drawn slot the pool has nothing for is an ordinary one
            var workers = new Thread[opt.threadCount];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = new Thread(() =>
                {
                    while (!Volatile.Read(ref st.stop))
                    {
                        if (queue.Count > 256) { Thread.Sleep(5); continue; }   // backpressure
                        int[] sizes = Volatile.Read(ref st.sizes);
                        DrawTask[] tasks = Volatile.Read(ref st.tasks);
                        int turns = sizes.Length + tasks.Length;   // every open slot gets its turn, drawn or not
                        if (turns == 0) break;
                        int s = Interlocked.Increment(ref st.nextSeed);
                        int turn = (s - opt.seedBase) % turns;
                        Interlocked.Increment(ref st.attempts);
                        if (turn >= sizes.Length)
                        {
                            var task = tasks[turn - sizes.Length];
                            Interlocked.Increment(ref task.tries);
                            int n = task.s.d.size;
                            if (!StencilFiller.TryFillRaw(n, task.s.ids, 1, s, out int[] drawnRegion)) continue;
                            if (!QueensSolver.TrySolve(n, drawnRegion, out int[] sol, out bool unique) || !unique) continue;
                            var r = WeightRater.Rate(n, drawnRegion, sol);
                            if (r.trials > 0) continue;
                            queue.Enqueue((drawnRegion, sol, s, r, TimeRater.Rate(n, drawnRegion, sol, r), n, task.s));
                            continue;
                        }
                        int size = sizes[turn];
                        bool ok = LevelGenerator.TryGenerateRaw(size, s, 250, out int[] region, out int[] cols, out _);
                        if (!ok) continue;
                        var rep = WeightRater.Rate(size, region, cols);
                        if (rep.trials > 0) continue;   // guess wall — discarded on the worker
                        queue.Enqueue((region, cols, s, rep, TimeRater.Rate(size, region, cols, rep), size, null));
                    }
                }) { IsBackground = true, Name = $"PoolGen{w}" };
                workers[w].Start();
            }

            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (open.Count > 0)
                {
                    if (opt.maxSeconds > 0 && clock.Elapsed.TotalSeconds >= opt.maxSeconds) break;
                    if (!keepGoing(made, open.Count, Interlocked.Read(ref st.attempts))) break;

                    bool replan = false;
                    while (open.Count > 0 && queue.TryDequeue(out var c))
                    {
                        int[] revealed = null;
                        int weight = c.rep.weight;
                        int slot = c.stencil == null
                            ? FindSlot(gates, open, c.size, c.region, c.cols, c.rep, c.time, out revealed, out weight)
                            : FindDrawnSlot(gates, open, c.stencil.d, c.rep, c.time, ds.placed, opt.kindApart);
                        if (slot >= 0)
                        {
                            int[] painted = c.stencil?.colors;
                            // a drawing uncoloured: what was drawn keeps its colours, the rest take the unused ones in order
                            int[] colors = painted == null ? null : LevelColors.Stored(StencilFiller.Colors(c.size, painted));
                            if (opt.colors != null)
                            {
                                // seeded by the level number, so a level always gets the same colours
                                var adj = LevelColors.Adjacency(c.size, c.region);
                                var assign = LevelColors.Pick(c.size, open[slot].level, opt.colors, adj, painted);
                                // no set fits, or (never expected) the set picked breaks a rule: the board is not used
                                if (assign == null || LevelColors.HasIssues(c.size, assign, opt.colors, adj, painted == null ? 0 : painted.Length)) { uncolourable++; continue; }
                                colors = LevelColors.Stored(assign);
                            }
                            onFilled(new Filled
                            {
                                slot = open[slot], region = c.region, cols = c.cols, weight = weight, seed = c.seed, revealedRows = revealed,
                                regionColors = colors, drawing = c.stencil == null ? 0 : c.stencil.d.number,
                            });
                            if (c.stencil != null) ds.Place(open[slot].level, c.stencil.d);
                            open.RemoveAt(slot);
                            made++;
                            replan = true;
                        }
                        // no fit at the fixed window → the board is discarded; the window never widens
                    }

                    // a drawing that gave its slot nothing makes way for the slot's next one
                    foreach (var task in Volatile.Read(ref st.tasks))
                        if (Volatile.Read(ref task.tries) >= TriesPerDrawing)
                        {
                            ds.gaveUp.TryGetValue(task.level, out int n);
                            ds.gaveUp[task.level] = n + 1;
                            ds.tasks.Remove((task.level, task.s.d.number));
                            replan = true;
                        }

                    if (replan)   // workers switch to the sizes and the drawings still needed
                    {
                        Volatile.Write(ref st.tasks, Plan(open, ds, opt.kindApart, ref undrawn));
                        Volatile.Write(ref st.sizes, SizesOf(open));
                    }

                    Thread.Sleep(10);   // let workers produce; keep the caller responsive
                }
            }
            finally
            {
                Volatile.Write(ref st.stop, true);
                foreach (var t in workers) t.Join(2000);
            }
            return (made, Interlocked.Read(ref st.attempts), uncolourable, undrawn);
        }

        // ---- drawn slots ---------------------------------------------------------------------------

        // No level within kindApart of this one was built from this kind of drawing.
        static bool KindFree(List<(int level, string kind)> taken, int level, string kind, int kindApart)
        {
            foreach (var (l, k) in taken)
                if (l != level && Math.Abs(l - level) <= kindApart && k == kind) return false;
            return true;
        }

        // The kinds of drawing the levels within kindApart of this one were built from.
        static HashSet<string> KindsNear(List<(int level, string kind)> taken, int level, int kindApart)
        {
            var kinds = new HashSet<string>();
            foreach (var (l, k) in taken)
                if (l != level && Math.Abs(l - level) <= kindApart) kinds.Add(k);
            return kinds;
        }

        /// <summary>
        /// The drawing each open drawn slot works on now. Slots are taken in order; a slot's choice is,
        /// of the drawings of its size whose kind is free around it: one whose measured seconds reach
        /// what the slot asks before one whose do not, then the kind used least, then the drawing used
        /// least. A slot with no drawing to choose from stops being a drawn slot (counted in
        /// <paramref name="undrawn"/>).
        /// </summary>
        static DrawTask[] Plan(List<LevelTarget> open, DrawnState ds, int kindApart, ref int undrawn)
        {
            var tasks = new List<DrawTask>();
            var taken = new List<(int level, string kind)>(ds.placed);   // + what the slots before this one are working on
            var kindUses = new Dictionary<string, int>();
            foreach (var (_, kind) in ds.placed) { kindUses.TryGetValue(kind, out int n); kindUses[kind] = n + 1; }

            for (int i = 0; i < open.Count; i++)
            {
                var t = open[i];
                if (!t.drawn) continue;
                var built = KindsNear(ds.placed, t.level, kindApart);
                var can = ds.pool.FindAll(s => s.d.size == t.boardSize && !built.Contains(s.d.Kind));
                if (can.Count == 0) { t.drawn = false; open[i] = t; undrawn++; continue; }
                var working = KindsNear(taken, t.level, kindApart);
                var apart = can.FindAll(s => !working.Contains(s.d.Kind));
                if (apart.Count > 0) can = apart;

                can.Sort((a, b) =>
                {
                    int c = OutOfReach(a.d, t.boardSeconds).CompareTo(OutOfReach(b.d, t.boardSeconds));
                    if (c != 0) return c;
                    kindUses.TryGetValue(a.d.Kind, out int ka); kindUses.TryGetValue(b.d.Kind, out int kb);
                    if (ka != kb) return ka.CompareTo(kb);
                    ds.drawingUses.TryGetValue(a.d.number, out int da); ds.drawingUses.TryGetValue(b.d.number, out int db);
                    if (da != db) return da.CompareTo(db);
                    return Mix(t.level, a.d.number).CompareTo(Mix(t.level, b.d.number));   // no drawing is first for every level
                });

                ds.gaveUp.TryGetValue(t.level, out int skip);
                var pick = can[skip % can.Count];
                if (!ds.tasks.TryGetValue((t.level, pick.d.number), out var task))
                    ds.tasks[(t.level, pick.d.number)] = task = new DrawTask { s = pick, level = t.level };
                tasks.Add(task);
                taken.Add((t.level, pick.d.Kind));
                kindUses.TryGetValue(pick.d.Kind, out int uses);
                kindUses[pick.d.Kind] = uses + 1;
            }
            return tasks.ToArray();
        }

        // A drawing measured, whose shortest and longest completions are both far from what the slot asks.
        static bool OutOfReach(StencilFiller.Drawing d, float boardSeconds) =>
            d.secondsTo > 0 && (boardSeconds < d.secondsFrom * 0.85f || boardSeconds > d.secondsTo * 1.15f);

        static int Mix(int level, int number) => unchecked((level * 73856093) ^ (number * 19349663)) & 0x7fffffff;

        /// <summary>
        /// The open drawn slot a completed drawing goes into, or -1: the board size, the slot's window
        /// and gates like any board, and no level nearby built from the same kind. Rarest fit wins,
        /// as in <see cref="FindSlot"/>.
        /// </summary>
        static int FindDrawnSlot(in GenerationGates gates, List<LevelTarget> open, StencilFiller.Drawing d,
            in WeightRater.Report rep, in TimeRater.Time time, List<(int level, string kind)> placed, int kindApart)
        {
            int best = -1, bestPrio = -1;
            for (int i = 0; i < open.Count; i++)
            {
                var t = open[i];
                if (!t.drawn || t.boardSize != d.size || (int)t.role <= bestPrio) continue;
                if (!CampaignCurve.PassesGates(gates, t, rep, time) || !KindFree(placed, t.level, d.Kind, kindApart)) continue;
                bestPrio = (int)t.role; best = i;
            }
            return best;
        }

        // ---- the job as text: what the editor hands to a build running outside it ----------------
        // One line of gates, one of options, then one line per slot, per drawing of the pool and per
        // level already built from a drawing. Floats are written round-trip, so the builder outside
        // works on exactly the numbers the curve produced.

        static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static float PF(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        public static string Pack(in GenerationGates g, IList<LevelTarget> open, in Options opt)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("gates ").Append(g.maxTrials).Append(' ').Append(F(g.maxWall)).Append(' ').Append(F(g.minSecondsPerStep)).Append(' ')
              .Append(F(g.secondsPerRow)).Append(' ').Append(g.maxSteps).Append(' ').Append(g.milestoneMaxSteps).Append(' ')
              .Append(F(g.maxStart)).Append(' ').Append(F(g.peakMaxStart)).Append('\n');
            sb.Append("options ").Append(opt.threadCount).Append(' ').Append(opt.seedBase).Append(' ').Append(F(opt.maxSeconds)).Append(' ').Append(opt.kindApart).Append('\n');
            foreach (var t in open)
                sb.Append("slot ").Append(t.level).Append(' ').Append(t.boardSize).Append(' ').Append(F(t.boardSeconds)).Append(' ')
                  .Append(F(t.placeFactor)).Append(' ').Append(F(t.targetSeconds)).Append(' ').Append(F(t.minSeconds)).Append(' ').Append(F(t.maxSeconds)).Append(' ')
                  .Append((int)t.role).Append(' ').Append(t.dog ? 1 : 0).Append(' ')
                  .Append(t.tricks.forbidden).Append(' ').Append(t.tricks.lesson ? 1 : 0).Append(' ').Append((int)t.tricks.teach).Append(' ')
                  .Append(t.tricks.stage).Append(' ').Append(t.tricks.minUses).Append(' ').Append(t.tricks.maxUses).Append(' ')
                  .Append(t.drawn ? 1 : 0).Append(' ').Append(F(t.firstSeconds)).Append(' ').Append(t.tricks.maxOthers).Append(' ').Append(t.tricks.young).Append('\n');
            if (opt.drawings != null)
                foreach (var d in opt.drawings)
                    sb.Append("drawing ").Append(d.number).Append(' ').Append(d.size).Append(' ').Append(d.secondsFrom).Append(' ').Append(d.secondsTo).Append(' ')
                      .Append(string.Join(",", d.paint)).Append(' ').Append(d.name).Append('\n');
            if (opt.drawn != null)
                foreach (var (level, number) in opt.drawn)
                    sb.Append("drawnlevel ").Append(level).Append(' ').Append(number).Append('\n');
            if (opt.colors != null) sb.Append(LevelColors.Pack(opt.colors));
            return sb.ToString();
        }

        public static void Unpack(string text, out GenerationGates g, out List<LevelTarget> open, out Options opt)
        {
            g = default; opt = default; open = new List<LevelTarget>();
            var colors = LevelColors.Unpack(text);
            var drawings = new List<StencilFiller.Drawing>();
            var drawn = new List<(int level, int drawing)>();
            foreach (string line in text.Split('\n'))
            {
                string[] p = line.Trim().Split(' ');
                if (p[0] == "gates")
                    g = new GenerationGates
                    {
                        maxTrials = int.Parse(p[1]), maxWall = PF(p[2]), minSecondsPerStep = PF(p[3]), secondsPerRow = PF(p[4]),
                        maxSteps = int.Parse(p[5]), milestoneMaxSteps = int.Parse(p[6]), maxStart = PF(p[7]), peakMaxStart = PF(p[8]),
                    };
                else if (p[0] == "options")
                    opt = new Options { threadCount = int.Parse(p[1]), seedBase = int.Parse(p[2]), maxSeconds = PF(p[3]), kindApart = int.Parse(p[4]), colors = colors };
                else if (p[0] == "slot")
                    open.Add(new LevelTarget
                    {
                        level = int.Parse(p[1]), boardSize = int.Parse(p[2]), boardSeconds = PF(p[3]), placeFactor = PF(p[4]),
                        targetSeconds = PF(p[5]), minSeconds = PF(p[6]), maxSeconds = PF(p[7]),
                        role = (Role)int.Parse(p[8]), dog = p[9] == "1",
                        tricks = new TrickRule
                        {
                            forbidden = int.Parse(p[10]), lesson = p[11] == "1", teach = (Trick)int.Parse(p[12]),
                            stage = int.Parse(p[13]), minUses = int.Parse(p[14]), maxUses = int.Parse(p[15]),
                            maxOthers = p.Length > 18 ? int.Parse(p[18]) : -1,   // a job packed before this field: no limit
                            young = p.Length > 19 ? int.Parse(p[19]) : 0,
                        },
                        drawn = p[16] == "1",
                        firstSeconds = p.Length > 17 ? PF(p[17]) : 0f,
                    });
                else if (p[0] == "drawing")
                    drawings.Add(new StencilFiller.Drawing
                    {
                        number = int.Parse(p[1]), size = int.Parse(p[2]), secondsFrom = int.Parse(p[3]), secondsTo = int.Parse(p[4]),
                        paint = Array.ConvertAll(p[5].Split(','), int.Parse), name = string.Join(" ", p, 6, p.Length - 6),
                    });
                else if (p[0] == "drawnlevel")
                    drawn.Add((int.Parse(p[1]), int.Parse(p[2])));
            }
            opt.drawings = drawings;   // the options line comes before them
            opt.drawn = drawn;
        }

        // the board sizes the ordinary open slots ask for, one per slot
        static int[] SizesOf(List<LevelTarget> open)
        {
            var sizes = new List<int>(open.Count);
            foreach (var t in open) if (!t.drawn) sizes.Add(t.boardSize);
            return sizes.ToArray();
        }

        /// <summary>
        /// The open slot a rated board goes into, or -1. Rarest fit wins (milestone > peak > build >
        /// normal > breather) so easy slots don't eat the rare heavy boards. A slot that opens with a
        /// queen on the board (dog) is tried with each row's queen revealed, and takes the one that
        /// lands closest to its seconds. A drawn slot takes no random board.
        /// </summary>
        public static int FindSlot(in GenerationGates gates, List<LevelTarget> open, int size,
            int[] region, int[] cols, in WeightRater.Report rep, in TimeRater.Time time, out int[] revealed, out int weight)
        {
            int best = -1, bestPrio = -1;
            revealed = null;
            weight = rep.weight;
            for (int i = 0; i < open.Count; i++)
            {
                var t = open[i];
                if (t.drawn || t.boardSize != size) continue;
                int prio = (int)t.role;
                if (prio <= bestPrio) continue;
                if (!t.dog)
                {
                    if (!CampaignCurve.PassesGates(gates, t, rep, time)) continue;
                    bestPrio = prio; best = i; revealed = null; weight = rep.weight;
                    continue;
                }
                int row = BestDog(gates, t, size, region, cols, out int w);
                if (row < 0) continue;
                bestPrio = prio; best = i; revealed = new[] { row }; weight = w;
            }
            return best;
        }

        // The row whose queen, revealed, makes the board pass the slot's gates closest to its seconds (-1 = none).
        static int BestDog(in GenerationGates gates, in LevelTarget t, int size, int[] region, int[] cols, out int weight)
        {
            int best = -1;
            double bestGap = double.MaxValue;
            weight = 0;
            var rows = new int[1];
            for (int row = 0; row < size; row++)
            {
                rows[0] = row;
                var rep = WeightRater.Rate(size, region, cols, rows);
                if (rep.trials > 0) continue;
                var time = TimeRater.Rate(size, region, cols, rep, rows);
                if (!CampaignCurve.PassesGates(gates, t, rep, time)) continue;
                double gap = Math.Abs(time.seconds - t.boardSeconds);
                if (gap < bestGap) { bestGap = gap; best = row; weight = rep.weight; }
            }
            return best;
        }
    }
}
