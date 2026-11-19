using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// Region colours of a board: which palette colour each region gets, and whether a colouring
    /// breaks the rules. No Unity types — the editor turns the palette into plain numbers
    /// (<see cref="Rules"/>) and this runs the same inside the editor and outside it.
    ///
    /// Two colours can confuse a player in two different ways, and each needs its own rule:
    ///  - physically alike (OKLab distance under colorMinDist): the eye can't tell them apart at
    ///    all, so they must never share a board;
    ///  - same colour *family* (shared XKCD name, or hue angles too close): easy to tell apart
    ///    side by side, but the player identifies regions categorically — "the pink one" — so
    ///    a light pink and a dark pink still collide. Such pairs may share a board but must
    ///    never touch. The two family tests cover each other's blind spots: names miss pairs
    ///    straddling a naming boundary, hue misses pairs people collapse into one name.
    /// </summary>
    public static class LevelColors
    {
        /// <summary>The palette and the thresholds, as plain numbers.</summary>
        public sealed class Rules
        {
            public float[] lab;          // the palette in OKLab: 3 numbers a colour (L, a, b)
            public float colorMinDist;   // OKLab distance below which two colours read as "the same"
            public float nameMinDist;    // XKCD name distance below which two colours share a name
            public float hueMinDeg;      // hue angle difference below which two colours share a family
            public int[] byEye;          // pairs a human eye confirmed as confusable: 2 numbers a pair
            public int Count => lab.Length / 3;
        }

        // XKCD colour-survey name distance between every SORegionsColors pair: 0 = people give
        // both colours the same name, 1 = never the same name. Computed offline from the C3
        // dataset (Heer & Stone) — recompute whenever a palette colour changes.
        public static readonly float[,] NameDist =
        {
            { 0.000f, 1.000f, 1.000f, 1.000f, 0.995f, 0.946f, 1.000f, 1.000f, 0.944f, 1.000f, 1.000f, 1.000f, 0.991f, 1.000f, 1.000f, 0.011f, 0.999f },
            { 1.000f, 0.000f, 0.984f, 0.943f, 0.995f, 0.999f, 0.941f, 0.794f, 1.000f, 0.903f, 0.976f, 0.636f, 1.000f, 0.326f, 0.893f, 1.000f, 0.991f },
            { 1.000f, 0.984f, 0.000f, 1.000f, 0.625f, 1.000f, 1.000f, 0.128f, 0.990f, 1.000f, 0.158f, 0.158f, 0.942f, 1.000f, 0.259f, 1.000f, 0.095f },
            { 1.000f, 0.943f, 1.000f, 0.000f, 1.000f, 0.930f, 0.029f, 1.000f, 1.000f, 0.043f, 1.000f, 1.000f, 1.000f, 0.306f, 1.000f, 1.000f, 1.000f },
            { 0.995f, 0.995f, 0.625f, 1.000f, 0.000f, 1.000f, 1.000f, 0.847f, 0.547f, 1.000f, 0.643f, 0.834f, 0.354f, 1.000f, 0.804f, 0.989f, 0.439f },
            { 0.946f, 0.999f, 1.000f, 0.930f, 1.000f, 0.000f, 0.947f, 1.000f, 0.996f, 0.975f, 1.000f, 1.000f, 1.000f, 0.982f, 1.000f, 0.950f, 1.000f },
            { 1.000f, 0.941f, 1.000f, 0.029f, 1.000f, 0.947f, 0.000f, 1.000f, 1.000f, 0.015f, 1.000f, 0.999f, 1.000f, 0.290f, 1.000f, 0.999f, 1.000f },
            { 1.000f, 0.794f, 0.128f, 1.000f, 0.847f, 1.000f, 1.000f, 0.000f, 1.000f, 0.993f, 0.379f, 0.052f, 0.997f, 0.902f, 0.423f, 1.000f, 0.242f },
            { 0.944f, 1.000f, 0.990f, 1.000f, 0.547f, 0.996f, 1.000f, 1.000f, 0.000f, 1.000f, 0.994f, 1.000f, 0.182f, 1.000f, 1.000f, 0.918f, 0.918f },
            { 1.000f, 0.903f, 1.000f, 0.043f, 1.000f, 0.975f, 0.015f, 0.993f, 1.000f, 0.000f, 1.000f, 0.987f, 1.000f, 0.248f, 0.997f, 1.000f, 1.000f },
            { 1.000f, 0.976f, 0.158f, 1.000f, 0.643f, 1.000f, 1.000f, 0.379f, 0.994f, 1.000f, 0.000f, 0.307f, 0.947f, 1.000f, 0.036f, 1.000f, 0.352f },
            { 1.000f, 0.636f, 0.158f, 1.000f, 0.834f, 1.000f, 0.999f, 0.052f, 1.000f, 0.987f, 0.307f, 0.000f, 0.996f, 0.813f, 0.309f, 1.000f, 0.291f },
            { 0.991f, 1.000f, 0.942f, 1.000f, 0.354f, 1.000f, 1.000f, 0.997f, 0.182f, 1.000f, 0.947f, 0.996f, 0.000f, 1.000f, 0.992f, 0.978f, 0.850f },
            { 1.000f, 0.326f, 1.000f, 0.306f, 1.000f, 0.982f, 0.290f, 0.902f, 1.000f, 0.248f, 1.000f, 0.813f, 1.000f, 0.000f, 0.949f, 1.000f, 1.000f },
            { 1.000f, 0.893f, 0.259f, 1.000f, 0.804f, 1.000f, 1.000f, 0.423f, 1.000f, 0.997f, 0.036f, 0.309f, 0.992f, 0.949f, 0.000f, 1.000f, 0.502f },
            { 0.011f, 1.000f, 1.000f, 1.000f, 0.989f, 0.950f, 0.999f, 1.000f, 0.918f, 1.000f, 1.000f, 1.000f, 0.978f, 1.000f, 1.000f, 0.000f, 0.998f },
            { 0.999f, 0.991f, 0.095f, 1.000f, 0.439f, 1.000f, 1.000f, 0.242f, 0.918f, 1.000f, 0.352f, 0.291f, 0.850f, 1.000f, 0.502f, 0.998f, 0.000f },
        };

        /// <summary>OKLab distance between two palette colours.</summary>
        public static float Dist(Rules r, int a, int b)
        {
            float x = r.lab[a * 3] - r.lab[b * 3], y = r.lab[a * 3 + 1] - r.lab[b * 3 + 1], z = r.lab[a * 3 + 2] - r.lab[b * 3 + 2];
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>Hue angle of a palette colour, degrees (-180..180).</summary>
        public static float HueDeg(Rules r, int c) => (float)Math.Atan2(r.lab[c * 3 + 2], r.lab[c * 3 + 1]) * 57.29578f;

        /// <summary>The shortest way round the wheel between two hue angles, as a positive number of degrees.</summary>
        public static float HueGap(Rules r, int a, int b)
        {
            float t = HueDeg(r, b) - HueDeg(r, a);
            float d = t - (float)Math.Floor(t / 360f) * 360f;
            d = d < 0f ? 0f : d > 360f ? 360f : d;
            if (d > 180f) d -= 360f;
            return Math.Abs(d);
        }

        // Assumes a saturated palette: a near-grey colour has no meaningful hue angle, so the
        // hue test would need a chroma guard if a pale colour ever joins the palette.
        /// <summary>May these two palette colours never sit on touching regions?</summary>
        public static bool ForbiddenNeighbor(Rules r, int a, int b)
        {
            if (a >= NameDist.GetLength(0) || b >= NameDist.GetLength(0)) return false;
            if (NameDist[a, b] < r.nameMinDist) return true;
            if (HueGap(r, a, b) < r.hueMinDeg) return true;
            for (int i = 0; i + 1 < r.byEye.Length; i += 2)
                if ((r.byEye[i] == a && r.byEye[i + 1] == b) || (r.byEye[i] == b && r.byEye[i + 1] == a)) return true;
            return false;
        }

        /// <summary>Which regions touch (orthogonal neighbours), as a bool matrix.</summary>
        public static bool[,] Adjacency(int n, int[] regions)
        {
            var adj = new bool[n, n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int a = regions[r * n + c];
                    if (c + 1 < n) { int b = regions[r * n + c + 1]; if (a != b) { adj[a, b] = adj[b, a] = true; } }
                    if (r + 1 < n) { int b = regions[(r + 1) * n + c]; if (a != b) { adj[a, b] = adj[b, a] = true; } }
                }
            return adj;
        }

        /// <summary>
        /// True when the board breaks either rule — a close (or identical) pair anywhere, or a
        /// forbidden family pair touching. <paramref name="regionColors"/>: palette index per region,
        /// null / empty = region k shows colour k.
        /// </summary>
        public static bool HasIssues(int n, int[] regionColors, Rules r, bool[,] adj)
        {
            int Of(int region) => regionColors != null && region < regionColors.Length ? regionColors[region] : region;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    int ca = Of(a), cb = Of(b);
                    if (ca >= r.Count || cb >= r.Count) continue;
                    if (ca == cb || Dist(r, ca, cb) < r.colorMinDist) return true;
                    if (adj[a, b] && ForbiddenNeighbor(r, ca, cb)) return true;
                }
            return false;
        }

        /// <summary>
        /// Region→colour assignment: n palette colours that are far apart AND look like a set,
        /// arranged so no forbidden pair lands on touching regions. Seeded (by level number), so a
        /// level always gets the same colours while neighbours get different ones. Null when no
        /// colour set fits the rules at these thresholds.
        ///
        /// Distance alone is not enough: a set can be perfectly distinguishable and still ugly —
        /// hues bunched on one side of the wheel, or a pale cream sitting next to a dark green.
        /// So many valid sets are generated and the one that also reads as balanced is kept.
        /// </summary>
        public static int[] Pick(int n, int seed, Rules r, bool[,] adj)
        {
            if (n > r.Count) return null;
            var rng = new Random(seed);
            int[] best = null;
            float bestScore = float.NegativeInfinity;

            for (int attempt = 0; attempt < 600; attempt++)
            {
                var set = TrySpreadSet(n, rng, r);
                if (set == null) continue;
                float s = HarmonyScore(set, r);
                if (s <= bestScore) continue;
                var assign = ArrangeOnRegions(set, adj, r);
                if (assign == null) continue;   // no arrangement keeps forbidden pairs apart
                bestScore = s; best = assign;
                if (attempt > 250) break;   // enough candidates seen
            }
            return best;
        }

        /// <summary>What to store on a level for an assignment: null when region k simply gets colour k.</summary>
        public static int[] Stored(int[] assign)
        {
            for (int i = 0; i < assign.Length; i++) if (assign[i] != i) return assign;
            return null;
        }

        // Place the picked colours on the regions so no forbidden pair touches.
        // Exhaustive backtracking — null only when no permutation works for this set.
        static int[] ArrangeOnRegions(int[] set, bool[,] adj, Rules r)
        {
            int n = set.Length;
            var assign = new int[n];
            var used = new bool[n];

            bool Place(int region)
            {
                if (region == n) return true;
                for (int i = 0; i < n; i++)
                {
                    if (used[i]) continue;
                    bool ok = true;
                    for (int k = 0; k < region && ok; k++)
                        if (adj[region, k] && ForbiddenNeighbor(r, set[i], assign[k])) ok = false;
                    if (!ok) continue;
                    used[i] = true; assign[region] = set[i];
                    if (Place(region + 1)) return true;
                    used[i] = false;
                }
                return false;
            }

            return Place(0) ? assign : null;
        }

        // One greedy attempt: shuffle the palette, take colours that clear colorMinDist.
        static int[] TrySpreadSet(int n, Random rng, Rules r)
        {
            var order = new int[r.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)   // Fisher-Yates
            {
                int j = rng.Next(i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            var picked = new int[n];
            int count = 0;
            foreach (int c in order)
            {
                bool ok = true;
                for (int k = 0; k < count && ok; k++)
                    if (Dist(r, c, picked[k]) < r.colorMinDist) ok = false;
                if (!ok) continue;
                picked[count++] = c;
                if (count == n) return picked;
            }
            return null;
        }

        // Higher is prettier. Rewards separation and even hue spacing; punishes mixing very
        // light with very dark, and mixing vivid with washed-out.
        static float HarmonyScore(int[] set, Rules r)
        {
            int n = set.Length;

            float minDist = float.MaxValue;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    minDist = Math.Min(minDist, Dist(r, set[i], set[j]));

            // lightness and chroma consistency — the two that decide whether a board looks like a set
            float lSum = 0f, cSum = 0f;
            var chroma = new float[n];
            for (int i = 0; i < n; i++)
            {
                float y = r.lab[set[i] * 3 + 1], z = r.lab[set[i] * 3 + 2];
                chroma[i] = (float)Math.Sqrt(y * y + z * z);
                lSum += r.lab[set[i] * 3]; cSum += chroma[i];
            }
            float lMean = lSum / n, cMean = cSum / n, lVar = 0f, cVar = 0f;
            for (int i = 0; i < n; i++)
            {
                float dl = r.lab[set[i] * 3] - lMean; lVar += dl * dl;
                float dc = chroma[i] - cMean;         cVar += dc * dc;
            }
            float lSd = (float)Math.Sqrt(lVar / n), cSd = (float)Math.Sqrt(cVar / n);

            // hue evenness: gaps around the wheel should be similar
            var hues = new float[n];
            for (int i = 0; i < n; i++)
            {
                float h = HueDeg(r, set[i]);
                hues[i] = h < 0f ? h + 360f : h;
            }
            Array.Sort(hues);
            float ideal = 360f / n, gapVar = 0f;
            for (int i = 0; i < n; i++)
            {
                float gap = (i == n - 1 ? hues[0] + 360f : hues[i + 1]) - hues[i];
                gapVar += (gap - ideal) * (gap - ideal);
            }
            float gapSd = (float)Math.Sqrt(gapVar / n) / ideal;

            return minDist * 2.0f - gapSd * 0.20f - lSd * 0.55f - cSd * 0.55f;
        }

        // ---- the rules as text, for a build running outside the editor -----------------------------

        static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static float PF(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        public static string Pack(Rules r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("colors ").Append(F(r.colorMinDist)).Append(' ').Append(F(r.nameMinDist)).Append(' ').Append(F(r.hueMinDeg)).Append('\n');
            sb.Append("lab");
            foreach (float v in r.lab) sb.Append(' ').Append(F(v));
            sb.Append("\nbyeye");
            foreach (int v in r.byEye) sb.Append(' ').Append(v);
            return sb.Append('\n').ToString();
        }

        /// <summary>Reads the rules out of a job text; null when the job carries none.</summary>
        public static Rules Unpack(string text)
        {
            Rules r = null;
            foreach (string line in text.Split('\n'))
            {
                string[] p = line.Trim().Split(' ');
                if (p[0] == "colors")
                    r = new Rules { colorMinDist = PF(p[1]), nameMinDist = PF(p[2]), hueMinDeg = PF(p[3]), lab = new float[0], byEye = new int[0] };
                else if (p[0] == "lab" && r != null)
                {
                    r.lab = new float[p.Length - 1];
                    for (int i = 1; i < p.Length; i++) r.lab[i - 1] = PF(p[i]);
                }
                else if (p[0] == "byeye" && r != null)
                {
                    var list = new List<int>();
                    for (int i = 1; i < p.Length; i++) if (p[i].Length > 0) list.Add(int.Parse(p[i]));
                    r.byEye = list.ToArray();
                }
            }
            return r;
        }
    }
}
