using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// Completes a partly painted board into a valid unique level. What was painted is LOCKED:
    /// a painted cell keeps its colour, and a painted region takes no further cell — the drawing
    /// survives exactly. The empty cells (-1) are covered by NEW regions, one for each colour the
    /// drawing did not use, each seeded at a freshly searched queen. So a drawing must leave
    /// colours unused: at least one for every separate empty area.
    ///
    /// Per attempt: search a solution whose queens give every painted colour exactly one queen
    /// (the leftover queens land on empty cells and seed the new regions) → flood the empty
    /// cells from those seeds → repair to uniqueness, moving cells only between the new regions.
    /// Any stage failing re-rolls, up to the caller's attempt budget.
    ///
    /// No Unity types: runs in the editor (Level Builder → Fill) and outside it (LevelGen fill).
    /// </summary>
    public static class StencilFiller
    {
        /// <summary>The folder of drawings, beside the level sets. It holds no levels, so it is not a set.</summary>
        public const string DrawingsFolder = "DRAWINGS";

        /// <summary>
        /// A drawing: the painted shape, and what is known about it. As a file it is labelled lines —
        ///     name stairs
        ///     seconds 121 238        the shortest and the longest level it was completed into
        ///     completions 80         how many completions that range was measured on
        ///     grid
        ///     . . 3 3 .              one line a row: a palette colour per painted cell, "." = empty
        /// The board size is the number of rows. Lines starting with # are notes. A file with no
        /// "grid" line is read as rows only.
        /// </summary>
        public sealed class Drawing
        {
            public string name = "";
            public int size;
            public int[] paint;               // palette index per cell, -1 = empty
            public int secondsFrom, secondsTo; // 0 = not measured
            public int completions;
            public int number;                // the file's number in the pool — set by whoever read the file, not written in it

            /// <summary>
            /// What a player would call the shape: the last word of the name, without a trailing number —
            /// "corner rainbow" and "rainbow" are one kind, so are "ring1" and "ring2". A build keeps
            /// levels of one kind apart and gives every kind the same number of turns.
            /// </summary>
            public string Kind
            {
                get
                {
                    string k = name.Trim();
                    k = k.Substring(k.LastIndexOf(' ') + 1).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                    return k.Length > 0 ? k : "drawing " + number;
                }
            }

            public static Drawing Parse(string text, out string error)
            {
                error = null;
                var d = new Drawing();
                var rows = new List<string[]>();
                bool grid = text.IndexOf("\ngrid", StringComparison.Ordinal) < 0 && !text.StartsWith("grid");   // no "grid" line: rows only
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (grid) { rows.Add(t); continue; }
                    if (t[0] == "grid") grid = true;
                    else if (t[0] == "name") d.name = line.Substring(4).Trim();
                    else if (t[0] == "seconds" && t.Length >= 3) { int.TryParse(t[1], out d.secondsFrom); int.TryParse(t[2], out d.secondsTo); }
                    else if (t[0] == "completions" && t.Length >= 2) int.TryParse(t[1], out d.completions);
                    else { error = $"\"{t[0]}\" is not a line a drawing has (name, seconds, completions, grid)"; return null; }
                }
                int n = d.size = rows.Count;
                d.paint = new int[n * n];
                for (int r = 0; r < n; r++)
                {
                    if (rows[r].Length != n) { error = $"row {r + 1} has {rows[r].Length} cells, the drawing has {n} rows"; return null; }
                    for (int c = 0; c < n; c++)
                    {
                        if (rows[r][c] == ".") d.paint[r * n + c] = -1;
                        else if (!int.TryParse(rows[r][c], out d.paint[r * n + c]) || d.paint[r * n + c] < 0) { error = $"row {r + 1}: \"{rows[r][c]}\" is not a colour"; return null; }
                    }
                }
                return d;
            }

            public string ToText()
            {
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(name)) sb.Append("name ").Append(name).Append('\n');
                if (secondsTo > 0) sb.Append("seconds ").Append(secondsFrom).Append(' ').Append(secondsTo).Append('\n');
                if (completions > 0) sb.Append("completions ").Append(completions).Append('\n');
                sb.Append("grid\n");
                for (int r = 0; r < size; r++)
                {
                    for (int c = 0; c < size; c++) sb.Append(c > 0 ? " " : "").Append(paint[r * size + c] < 0 ? "." : paint[r * size + c].ToString());
                    sb.Append('\n');
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Palette indexes as painted → region ids 0..k-1 (ascending palette order); -1 (empty) passes
        /// through. <paramref name="idToColor"/> gives back the palette colour of each painted region.
        /// </summary>
        public static int[] FromPalette(int[] paint, out int[] idToColor)
        {
            var distinct = new SortedSet<int>();
            foreach (int v in paint) if (v >= 0) distinct.Add(v);
            idToColor = new int[distinct.Count];
            var map = new Dictionary<int, int>();
            int next = 0;
            foreach (int v in distinct) { idToColor[next] = v; map[v] = next; next++; }
            var ids = new int[paint.Length];
            for (int i = 0; i < paint.Length; i++) ids[i] = paint[i] < 0 ? -1 : map[paint[i]];
            return ids;
        }

        /// <summary>
        /// The colours of a filled drawing: painted regions keep their palette colours, the new
        /// regions take the unused ones in order.
        /// </summary>
        public static int[] Colors(int n, int[] idToColor)
        {
            var colors = new int[n];
            Array.Copy(idToColor, colors, idToColor.Length);
            var taken = new HashSet<int>(idToColor);
            int nextClr = 0;
            for (int id = idToColor.Length; id < n; id++)
            {
                while (taken.Contains(nextClr)) nextClr++;
                colors[id] = nextClr;
                taken.Add(nextClr);
            }
            return colors;
        }

        /// <summary>
        /// Drawings that can never fill, caught before burning attempts: a colour owning two or
        /// more FULL rows (or columns) — every full row forces that row's queen into the colour,
        /// and a colour gets exactly one queen. Returns the reason, or null when the stencil is fine.
        /// </summary>
        public static string ValidateStencil(int n, int[] paint)
        {
            var rowsOf = new Dictionary<int, List<int>>();
            var colsOf = new Dictionary<int, List<int>>();

            for (int r = 0; r < n; r++)
            {
                int id = paint[r * n];
                if (id < 0) continue;
                bool full = true;
                for (int c = 1; c < n; c++) if (paint[r * n + c] != id) { full = false; break; }
                if (!full) continue;
                if (!rowsOf.TryGetValue(id, out var list)) rowsOf[id] = list = new List<int>();
                list.Add(r);
            }
            for (int c = 0; c < n; c++)
            {
                int id = paint[c];
                if (id < 0) continue;
                bool full = true;
                for (int r = 1; r < n; r++) if (paint[r * n + c] != id) { full = false; break; }
                if (!full) continue;
                if (!colsOf.TryGetValue(id, out var list)) colsOf[id] = list = new List<int>();
                list.Add(c);
            }

            foreach (var kv in rowsOf)
                if (kv.Value.Count >= 2)
                    return $"colour {kv.Key + 1} completely fills rows {string.Join(", ", kv.Value)} — "
                         + "each full row forces its queen into that colour, and a colour holds only one queen.";
            foreach (var kv in colsOf)
                if (kv.Value.Count >= 2)
                    return $"colour {kv.Key + 1} completely fills columns {string.Join(", ", kv.Value)} — "
                         + "each full column forces its queen into that colour, and a colour holds only one queen.";

            // ---- pockets: each sealed-off empty area needs its own new region, seeded by a queen inside it

            var usedIds = new HashSet<int>();
            foreach (int id in paint) if (id >= 0) usedIds.Add(id);
            int unusedColours = n - usedIds.Count;

            var pocketOf = new int[n * n];   // -1 = painted, else pocket index
            int pockets = 0;
            for (int i = 0; i < paint.Length; i++) pocketOf[i] = -1;
            var stack = new Stack<int>();
            for (int i = 0; i < paint.Length; i++)
            {
                if (paint[i] >= 0 || pocketOf[i] >= 0) continue;
                stack.Push(i);
                pocketOf[i] = pockets;
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    int r = idx / n, c = idx % n;
                    Visit(r - 1, c); Visit(r + 1, c); Visit(r, c - 1); Visit(r, c + 1);

                    void Visit(int rr, int cc)
                    {
                        if (rr < 0 || rr >= n || cc < 0 || cc >= n) return;
                        int j = rr * n + cc;
                        if (paint[j] < 0 && pocketOf[j] < 0) { pocketOf[j] = pockets; stack.Push(j); }
                    }
                }
                pockets++;
            }

            if (pockets > unusedColours)
                return $"the drawing seals the empty space into {pockets} separate areas, but only "
                     + $"{unusedColours} unused colour(s) remain to fill them — open a passage or use fewer colours.";

            // a pocket confined to one row can only be seeded by that row's queen — two in a row is impossible
            var pocketRows = new int[pockets]; var pocketCols = new int[pockets];
            for (int p = 0; p < pockets; p++) { pocketRows[p] = -2; pocketCols[p] = -2; }   // -2 = unseen, -1 = mixed
            for (int i = 0; i < paint.Length; i++)
            {
                int p = pocketOf[i];
                if (p < 0) continue;
                int r = i / n, c = i % n;
                pocketRows[p] = pocketRows[p] == -2 || pocketRows[p] == r ? r : -1;
                pocketCols[p] = pocketCols[p] == -2 || pocketCols[p] == c ? c : -1;
            }
            var rowPockets = new Dictionary<int, int>();
            var colPockets = new Dictionary<int, int>();
            for (int p = 0; p < pockets; p++)
            {
                if (pocketRows[p] >= 0)
                {
                    rowPockets.TryGetValue(pocketRows[p], out int k);
                    if (k == 1) return $"row {pocketRows[p]} has two separate sealed empty areas — each needs "
                                     + "that row's queen, and a row has only one. Open a passage between them.";
                    rowPockets[pocketRows[p]] = 1;
                }
                if (pocketCols[p] >= 0)
                {
                    colPockets.TryGetValue(pocketCols[p], out int k);
                    if (k == 1) return $"column {pocketCols[p]} has two separate sealed empty areas — each needs "
                                     + "that column's queen, and a column has only one. Open a passage between them.";
                    colPockets[pocketCols[p]] = 1;
                }
            }
            return null;
        }

        /// <summary>One level a drawing was completed into.</summary>
        public struct Completion
        {
            public int[] region, cols;
            public int seed;
            public WeightRater.Report rep;
            public TimeRater.Time time;
        }

        /// <summary>
        /// Completes a drawing <paramref name="tries"/> times, each from its own seed, and returns the
        /// levels that came out with one solution and no guess — shortest first. That list is what a
        /// drawing is worth: how many, and from how many seconds to how many.
        /// Works on all cores but two, in batches; after each batch <paramref name="keepGoing"/>
        /// (tries done, completions so far) may return false to stop. Stops by itself at
        /// <paramref name="enough"/> completions.
        /// </summary>
        public static List<Completion> Complete(int n, int[] ids, int tries, int seedBase,
            Func<int, int, bool> keepGoing = null, int enough = int.MaxValue)
        {
            var found = new System.Collections.Concurrent.ConcurrentBag<Completion>();
            var cores = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
            const int Batch = 500;
            for (int start = 0; start < tries; start += Batch)
            {
                int first = seedBase + start;
                System.Threading.Tasks.Parallel.For(0, Math.Min(Batch, tries - start), cores, i =>
                {
                    if (!TryFillRaw(n, ids, 1, first + i, out int[] region)) return;
                    if (!QueensSolver.TrySolve(n, region, out int[] cols, out bool unique) || !unique) return;
                    var rep = WeightRater.Rate(n, region, cols);
                    if (rep.trials > 0) return;
                    found.Add(new Completion { region = region, cols = cols, seed = first + i, rep = rep, time = TimeRater.Rate(n, region, cols, rep) });
                });
                if (found.Count >= enough) break;
                if (keepGoing != null && !keepGoing(Math.Min(tries, start + Batch), found.Count)) break;
            }
            var list = new List<Completion>(found);
            list.Sort((a, b) => a.time.seconds != b.time.seconds ? a.time.seconds.CompareTo(b.time.seconds) : a.seed.CompareTo(b.seed));
            return list;
        }

        /// <summary>Pure core (no Unity types): completes the stencil into a unique region map.</summary>
        public static bool TryFillRaw(int n, int[] paint, int attempts, int seed, out int[] region,
            Action<float> onProgress = null)
        {
            var usedIds = new HashSet<int>();
            foreach (int id in paint) if (id >= 0) usedIds.Add(id);

            var newIds = new List<int>();           // colours the drawing didn't use — they fill the space
            for (int k = 0; k < n; k++) if (!usedIds.Contains(k)) newIds.Add(k);
            if (newIds.Count == 0) { region = null; return false; }   // nothing left to fill the empty cells with

            var rng = new Random(seed);
            for (int a = 0; a < attempts; a++)
            {
                onProgress?.Invoke(a / (float)attempts);
                if (TryOnce(n, paint, newIds, rng, out region)) return true;
            }
            region = null;
            return false;
        }

        static bool TryOnce(int n, int[] paint, List<int> newIds, Random rng, out int[] region)
        {
            region = null;
            int[] cols = FindSolution(n, paint, newIds.Count, rng);
            if (cols == null) return false;

            var r = (int[])paint.Clone();
            // seed each unused colour at one of the free-cell queens (row order is as good as any)
            int next = 0;
            for (int row = 0; row < n; row++)
            {
                int idx = row * n + cols[row];
                if (paint[idx] < 0) r[idx] = newIds[next++];
            }

            if (!RegionGrower.GrowPartial(n, r, newIds, rng)) return false;   // unreachable pocket

            // repair to uniqueness, never touching a painted cell or a painted region
            var locked = new bool[n * n];
            for (int i = 0; i < locked.Length; i++) locked[i] = paint[i] >= 0;
            int budget = n * n * 4;
            for (int i = 0; i < budget; i++)
            {
                if (!QueensSolver.TryFindAlternative(n, r, cols, out int[] alt)) { region = r; return true; }
                if (!RegionGrower.TryBreakAlternative(n, r, cols, alt, rng, locked)) return false;
            }
            return false;
        }

        /// <summary>
        /// Backtracking queen search honouring the stencil: valid Queens placement (one per
        /// row/column, no touching) where every painted colour gets exactly ONE queen, and at most
        /// <paramref name="maxFreeQueens"/> queens land on empty cells (those seed the new regions —
        /// with each painted colour at exactly one, the free count comes out exact by itself).
        /// </summary>
        static int[] FindSolution(int n, int[] paint, int maxFreeQueens, Random rng)
        {
            // last row each painted colour appears in — for the "must place now" pruning
            var lastRow = new Dictionary<int, int>();
            for (int i = 0; i < paint.Length; i++)
                if (paint[i] >= 0) lastRow[paint[i]] = i / n;

            var cols = new int[n];
            var usedCol = new bool[n];
            var heroCount = new Dictionary<int, int>();
            foreach (int id in lastRow.Keys) heroCount[id] = 0;
            int freeUsed = 0;

            bool Place(int row)
            {
                if (row == n)
                {
                    foreach (var kv in heroCount) if (kv.Value != 1) return false;
                    return true;
                }

                // colours whose final chance is this row and still have no queen
                int mustId = int.MinValue;
                foreach (var kv in lastRow)
                    if (kv.Value == row && heroCount[kv.Key] == 0)
                    {
                        if (mustId != int.MinValue) return false;   // two colours need this row — dead end
                        mustId = kv.Key;
                    }

                var candidates = new List<int>(n);
                for (int c = 0; c < n; c++)
                {
                    if (usedCol[c]) continue;
                    if (row > 0 && Math.Abs(c - cols[row - 1]) < 2) continue;

                    int id = paint[row * n + c];
                    if (mustId != int.MinValue && id != mustId) continue;
                    if (id >= 0 && heroCount[id] >= 1) continue;    // colour already has its queen
                    if (id < 0 && freeUsed >= maxFreeQueens) continue;
                    candidates.Add(c);
                }
                Shuffle(candidates, rng);

                foreach (int c in candidates)
                {
                    int id = paint[row * n + c];
                    cols[row] = c;
                    usedCol[c] = true;
                    if (id >= 0) heroCount[id]++; else freeUsed++;

                    if (Place(row + 1)) return true;

                    usedCol[c] = false;
                    if (id >= 0) heroCount[id]--; else freeUsed--;
                }
                return false;
            }

            return Place(0) ? cols : null;
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
