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
    /// </summary>
    public static class PoolBuilder
    {
        public struct Options
        {
            public int threadCount;
            public int seedBase;
            public float maxSeconds;  // stop after this long and leave the rest open (0 = until done or stopped)
            public LevelColors.Rules colors;   // every built level is coloured by these (null = left uncoloured)
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
        }

        // shared with worker threads during generation
        sealed class SharedState
        {
            public int nextSeed;
            public long attempts;
            public bool stop;
            public int[] sizes;
        }

        /// <summary>
        /// Fills the open slots until done, the time is up, or <paramref name="keepGoing"/> says stop.
        /// Mutates <paramref name="open"/> (filled slots are removed). Both callbacks run on the
        /// calling thread: <paramref name="onFilled"/> once per filled slot, <paramref name="keepGoing"/>
        /// (levels filled, slots open, boards generated) about every 10 ms — return false to stop.
        /// A board that fits a slot but that no colour set fits is discarded like any other misfit.
        /// Returns (levels filled, boards generated, boards discarded for their colours).
        /// </summary>
        public static (int made, long attempts, int uncolourable) Run(GenerationGates gates, List<LevelTarget> open, Options opt,
            Action<Filled> onFilled, Func<int, int, long, bool> keepGoing)
        {
            var queue = new System.Collections.Concurrent.ConcurrentQueue<(int[] region, int[] cols, int seed, WeightRater.Report rep, TimeRater.Time time, int size)>();
            int made = 0, uncolourable = 0;

            // workers: generate + rate off the calling thread (TryGenerateRaw, Rate and TimeRater are thread-safe)
            var st = new SharedState { nextSeed = opt.seedBase - 1, sizes = SizesOf(open) };
            var workers = new Thread[opt.threadCount];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = new Thread(() =>
                {
                    while (!Volatile.Read(ref st.stop))
                    {
                        if (queue.Count > 256) { Thread.Sleep(5); continue; }   // backpressure
                        int[] sizes = Volatile.Read(ref st.sizes);
                        if (sizes.Length == 0) break;
                        int s = Interlocked.Increment(ref st.nextSeed);
                        int size = sizes[(s - opt.seedBase) % sizes.Length];
                        bool ok = LevelGenerator.TryGenerateRaw(size, s, 250, out int[] region, out int[] cols, out _);
                        Interlocked.Increment(ref st.attempts);
                        if (!ok) continue;
                        var rep = WeightRater.Rate(size, region, cols);
                        if (rep.trials > 0) continue;   // guess wall — discarded on the worker
                        queue.Enqueue((region, cols, s, rep, TimeRater.Rate(size, region, cols, rep), size));
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

                    bool filled = false;
                    while (open.Count > 0 && queue.TryDequeue(out var c))
                    {
                        int slot = FindSlot(gates, open, c.size, c.region, c.cols, c.rep, c.time, out int[] revealed, out int weight);
                        if (slot >= 0)
                        {
                            int[] colors = null;
                            if (opt.colors != null)
                            {
                                // seeded by the level number, so a level always gets the same colours
                                var adj = LevelColors.Adjacency(c.size, c.region);
                                var assign = LevelColors.Pick(c.size, open[slot].level, opt.colors, adj);
                                // no set fits, or (never expected) the set picked breaks a rule: the board is not used
                                if (assign == null || LevelColors.HasIssues(c.size, assign, opt.colors, adj)) { uncolourable++; continue; }
                                colors = LevelColors.Stored(assign);
                            }
                            onFilled(new Filled { slot = open[slot], region = c.region, cols = c.cols, weight = weight, seed = c.seed, revealedRows = revealed, regionColors = colors });
                            open.RemoveAt(slot);
                            made++;
                            filled = true;
                        }
                        // no fit at the fixed window → the board is discarded; the window never widens
                    }

                    if (filled) Volatile.Write(ref st.sizes, SizesOf(open));   // workers switch to the sizes still needed

                    Thread.Sleep(10);   // let workers produce; keep the caller responsive
                }
            }
            finally
            {
                Volatile.Write(ref st.stop, true);
                foreach (var t in workers) t.Join(2000);
            }
            return (made, Interlocked.Read(ref st.attempts), uncolourable);
        }

        // ---- the job as text: what the editor hands to a build running outside it ----------------
        // One line of gates, one of options, then one line per slot. Floats are written round-trip,
        // so the builder outside works on exactly the numbers the curve produced.

        static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static float PF(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        public static string Pack(in GenerationGates g, IList<LevelTarget> open, in Options opt)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("gates ").Append(g.maxTrials).Append(' ').Append(F(g.maxWall)).Append(' ').Append(F(g.minSecondsPerStep)).Append(' ')
              .Append(F(g.secondsPerRow)).Append(' ').Append(g.maxSteps).Append(' ').Append(g.milestoneMaxSteps).Append(' ')
              .Append(F(g.maxStart)).Append(' ').Append(F(g.peakMaxStart)).Append('\n');
            sb.Append("options ").Append(opt.threadCount).Append(' ').Append(opt.seedBase).Append(' ').Append(F(opt.maxSeconds)).Append('\n');
            foreach (var t in open)
                sb.Append("slot ").Append(t.level).Append(' ').Append(t.boardSize).Append(' ').Append(F(t.boardSeconds)).Append(' ')
                  .Append(F(t.placeFactor)).Append(' ').Append(F(t.targetSeconds)).Append(' ').Append(F(t.minSeconds)).Append(' ').Append(F(t.maxSeconds)).Append(' ')
                  .Append((int)t.role).Append(' ').Append(t.dog ? 1 : 0).Append(' ')
                  .Append(t.tricks.forbidden).Append(' ').Append(t.tricks.lesson ? 1 : 0).Append(' ').Append((int)t.tricks.teach).Append(' ')
                  .Append(t.tricks.stage).Append(' ').Append(t.tricks.minUses).Append(' ').Append(t.tricks.maxUses).Append('\n');
            if (opt.colors != null) sb.Append(LevelColors.Pack(opt.colors));
            return sb.ToString();
        }

        public static void Unpack(string text, out GenerationGates g, out List<LevelTarget> open, out Options opt)
        {
            g = default; opt = default; open = new List<LevelTarget>();
            var colors = LevelColors.Unpack(text);
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
                    opt = new Options { threadCount = int.Parse(p[1]), seedBase = int.Parse(p[2]), maxSeconds = PF(p[3]), colors = colors };
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
                        },
                    });
            }
        }

        static int[] SizesOf(List<LevelTarget> open)
        {
            var sizes = new int[open.Count];
            for (int i = 0; i < open.Count; i++) sizes[i] = open[i].boardSize;
            return sizes;
        }

        /// <summary>
        /// The open slot a rated board goes into, or -1. Rarest fit wins (milestone > peak > build >
        /// normal > breather) so easy slots don't eat the rare heavy boards. A slot that opens with a
        /// queen on the board (dog) is tried with each row's queen revealed, and takes the one that
        /// lands closest to its seconds.
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
                if (t.boardSize != size) continue;
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
