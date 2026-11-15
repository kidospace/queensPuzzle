using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// The 10K-campaign curve as an EDITOR authoring asset — every tunable of the progression
    /// design in one place, editable in the inspector without touching code.
    ///
    /// Everything is in SECONDS. A value in this curve is the time a board takes an experienced
    /// regular player — the unit TimeRater measures a board in. Nothing is translated: a slot
    /// that says 106 asks for a board TimeRater rates 106.
    ///
    /// What the player at a level really takes is that × the level's place factor
    /// (<see cref="placeFactor"/>): the same board is slower early in the game, where players
    /// are new. A slot's target and window are stated in those REAL seconds, and a built board
    /// fits when its own seconds × the factor are within ± <see cref="timeTol"/> of the target.
    /// Levels requiring guessing are always rejected at build time.
    ///
    ///   L1..learnEnd   the learning levels, one by one: board size and seconds (<see cref="learnLevels"/>).
    ///   after          wave templates (one peak, breather right after) + hot/cool macro cycles
    ///                  + a milestone every milestoneEvery levels.
    ///
    /// Breathers are protected: no hot factor, no jitter — the flat floor is the fun engine.
    /// All randomness is a hash of the level number, so builds reproduce.
    ///
    /// A level's ROLE (breather / normal / build / peak) and the grind gate compare boards of
    /// different sizes, so they read seconds as if on a 10x10 board: a smaller board is the same
    /// kind of level in fewer seconds (<see cref="secondsPerRow"/>).
    ///
    /// <see cref="GetTarget"/> turns a level number into board size + time window + role.
    /// </summary>
    [CreateAssetMenu(fileName = "CampaignCurve", menuName = "QueensPuzzle/Campaign Curve", order = 1)]
    public class CampaignCurveConfig : ScriptableObject
    {
        public enum Role { Breather, Normal, Build, Peak, Milestone }

        public struct LevelTarget
        {
            public int level;
            public int boardSize;
            public float boardSeconds;  // the board the slot asks for, in the board's own seconds (TimeRater)
            public float placeFactor;   // real seconds at this level / the board's own seconds (1 = level ~300)
            public float targetSeconds; // boardSeconds × placeFactor: what the regular player at this level takes
            public float minSeconds;    // accept window (target ± timeTol, milestones wider), real seconds
            public float maxSeconds;
            public Role role;
        }

        [Serializable]
        public class PlacePoint
        {
            public int level;
            [Tooltip("Real seconds of the regular player at this level / the board's own seconds.")]
            public float factor;
        }

        // Measured on recordings (22 Oct – 10 Nov 2026): the regular player's real time on each level
        // against the board's own rating, middle value per stretch of levels. Levels 1-12 (boards
        // under 8x8) are not measured and stay at 1; nothing is measured past ~1,200, the last value holds.
        [Header("Place in the game")]
        [Tooltip("How much longer (or shorter) the same board takes at a level than it takes an experienced player. " +
                 "Read between the points on a log scale of the level; before the first and after the last point the value holds.")]
        public PlacePoint[] placeFactor =
        {
            new PlacePoint { level = 1,    factor = 1.00f },
            new PlacePoint { level = 12,   factor = 1.00f },
            new PlacePoint { level = 13,   factor = 1.32f },
            new PlacePoint { level = 40,   factor = 1.32f },
            new PlacePoint { level = 60,   factor = 1.28f },
            new PlacePoint { level = 85,   factor = 1.21f },
            new PlacePoint { level = 125,  factor = 1.16f },
            new PlacePoint { level = 185,  factor = 1.07f },
            new PlacePoint { level = 260,  factor = 1.01f },
            new PlacePoint { level = 350,  factor = 0.96f },
            new PlacePoint { level = 475,  factor = 0.94f },
            new PlacePoint { level = 650,  factor = 0.91f },
            new PlacePoint { level = 875,  factor = 0.89f },
            new PlacePoint { level = 1200, factor = 0.88f },
        };

        [Serializable]
        public struct LearnLevel
        {
            public int size;
            [Tooltip("The board's own seconds (experienced player). The player at this level takes this × the place factor.")]
            public float seconds;
        }

        static LearnLevel L(int size, float seconds) => new LearnLevel { size = size, seconds = seconds };

        [Header("Learning levels (level 1 .. the length of this list)")]
        [Tooltip("One entry per level: its board size and its seconds. A level on a milestone number is a milestone.")]
        public LearnLevel[] learnLevels =
        {
            L(4, 19.7f), L(4, 24.2f), L(6, 35.3f), L(6, 36.5f), L(8, 57.1f), L(6, 34.5f), L(6, 40.5f), L(8, 58.8f), L(8, 62.4f), L(7, 56.9f),   // 1-10
            L(6, 27.0f), L(6, 37.1f), L(8, 52.0f), L(8, 55.9f), L(8, 65.1f), L(8, 47.6f), L(8, 60.6f), L(8, 52.0f), L(8, 64.6f), L(9, 79.5f),   // 11-20
            L(8, 49.0f), L(8, 57.8f), L(8, 58.1f), L(8, 55.6f), L(8, 60.2f), L(8, 54.7f), L(9, 79.1f), L(8, 60.0f), L(10, 106.0f), L(10, 116.3f),   // 21-30
            L(8, 50.8f), L(8, 55.3f), L(8, 59.6f), L(8, 59.0f), L(10, 100.5f), L(8, 55.4f), L(9, 82.0f), L(8, 63.1f), L(10, 108.6f), L(10, 119.8f),   // 31-40
            L(8, 53.5f), L(8, 57.6f), L(8, 64.7f), L(9, 76.7f), L(10, 103.6f), L(8, 61.4f), L(9, 88.8f), L(8, 59.8f), L(10, 109.2f), L(11, 153.8f),   // 41-50
            L(8, 52.7f), L(8, 65.0f), L(10, 105.9f), L(9, 75.0f), L(10, 108.4f), L(8, 63.6f), L(9, 86.3f), L(8, 61.2f), L(10, 114.1f), L(10, 123.5f),   // 51-60
            L(8, 57.7f), L(8, 60.8f), L(10, 106.7f), L(9, 80.7f), L(10, 105.0f), L(8, 62.1f), L(9, 91.2f), L(8, 66.1f), L(10, 119.9f), L(10, 125.9f),   // 61-70
            L(8, 61.1f), L(10, 100.5f), L(10, 105.3f), L(9, 78.4f), L(10, 112.6f), L(8, 60.6f), L(9, 90.1f), L(10, 105.4f), L(10, 119.5f), L(10, 126.5f),   // 71-80
            L(8, 59.7f), L(10, 103.0f), L(10, 106.7f), L(9, 83.7f), L(10, 114.5f), L(8, 62.8f), L(9, 92.1f), L(10, 107.7f), L(10, 122.3f), L(10, 130.9f),   // 81-90
            L(8, 59.6f), L(8, 65.1f), L(10, 109.5f), L(9, 79.0f), L(10, 114.4f), L(8, 61.1f), L(9, 96.4f), L(10, 103.1f), L(10, 123.5f), L(11, 171.6f),   // 91-100
            L(8, 59.6f), L(10, 102.7f), L(10, 113.6f), L(9, 85.6f), L(10, 120.0f), L(10, 104.3f), L(9, 96.1f), L(10, 104.9f), L(10, 128.1f), L(10, 133.1f),   // 101-110
            L(8, 67.3f), L(10, 104.6f), L(10, 113.9f), L(9, 86.6f), L(10, 115.9f), L(10, 104.2f), L(9, 99.1f), L(10, 107.0f), L(10, 126.4f), L(10, 136.8f),   // 111-120
            L(8, 62.2f), L(10, 111.3f), L(10, 117.3f), L(9, 84.4f), L(10, 121.1f), L(10, 104.3f), L(9, 101.6f), L(10, 111.3f), L(10, 130.4f), L(10, 137.6f),   // 121-130
            L(8, 66.3f), L(10, 110.3f), L(10, 117.3f), L(9, 87.9f), L(10, 124.3f), L(10, 106.0f), L(9, 103.2f), L(10, 110.1f), L(10, 133.2f), L(10, 142.0f),   // 131-140
            L(8, 63.9f), L(10, 110.2f), L(10, 120.3f), L(9, 87.4f), L(10, 122.2f), L(10, 108.4f), L(9, 105.4f), L(10, 115.1f), L(10, 132.5f), L(11, 181.2f),   // 141-150
            L(8, 66.9f), L(10, 111.9f), L(10, 118.9f), L(9, 90.0f), L(10, 124.3f), L(10, 105.8f), L(9, 103.4f), L(10, 113.8f), L(10, 133.9f), L(10, 142.7f),   // 151-160
            L(8, 67.3f), L(10, 112.3f), L(10, 118.4f), L(9, 90.9f), L(10, 129.3f), L(10, 109.2f), L(9, 104.1f), L(10, 117.1f), L(10, 137.9f), L(10, 145.7f),   // 161-170
            L(8, 67.5f), L(10, 116.1f), L(10, 121.7f), L(9, 89.0f), L(10, 130.5f), L(10, 105.2f), L(9, 108.8f), L(10, 117.6f), L(10, 140.0f), L(10, 148.8f),   // 171-180
            L(8, 71.7f), L(10, 119.7f), L(10, 121.2f), L(9, 91.3f), L(10, 130.9f), L(10, 109.3f), L(9, 108.3f), L(10, 119.6f), L(10, 139.9f), L(10, 150.3f),   // 181-190
            L(8, 71.1f), L(10, 120.3f), L(10, 122.7f), L(9, 90.8f), L(10, 133.3f), L(10, 112.2f), L(9, 110.7f), L(10, 121.1f), L(10, 139.5f), L(11, 197.0f),   // 191-200
        };

        public int LearnEnd => learnLevels.Length;

        [Serializable]
        public class WaveTemplate
        {
            [Tooltip("Seconds per level of the wave. Exactly one peak, a breather right after it.")]
            public float[] seconds;
        }

        [Header("Wave phase (after the learning levels)")]
        [Tooltip("Picked per wave by level hash, never the same template twice in a row.")]
        public WaveTemplate[] waves =
        {
            new WaveTemplate { seconds = new[] { 64.4f, 112.0f, 118.9f, 101.5f, 133.2f, 103.7f, 96.6f, 128.2f, 136.8f, 143.4f,
                                                 65.8f, 122.6f, 133.2f, 116.5f, 152.0f, 64.4f, 93.4f, 130.2f, 139.4f, 124.9f } },
            new WaveTemplate { seconds = new[] { 85.9f, 139.4f, 101.8f, 96.6f, 130.2f, 122.6f, 109.7f, 65.8f, 124.9f, 136.8f,
                                                 75.8f, 130.2f, 141.8f, 84.2f, 124.9f, 136.8f, 117.7f, 152.0f, 65.8f, 118.9f } },
            new WaveTemplate { seconds = new[] { 65.8f, 108.9f, 114.9f, 98.6f, 127.1f, 131.2f, 82.7f, 124.9f, 135.0f, 140.2f,
                                                 95.6f, 152.0f, 101.8f, 99.6f, 130.2f, 118.9f, 109.7f, 124.9f, 112.0f, 130.2f } },
            new WaveTemplate { seconds = new[] { 67.2f, 122.6f, 136.8f, 116.5f, 103.7f, 124.9f, 113.2f, 147.2f, 152.0f, 65.8f,
                                                 78.5f, 133.2f, 124.9f, 113.2f, 130.2f, 103.7f, 101.5f, 139.4f, 130.2f, 118.9f } },
            new WaveTemplate { seconds = new[] { 68.4f, 130.2f, 112.0f, 111.1f, 118.9f, 141.8f, 82.7f, 133.2f, 122.6f, 143.4f,
                                                 73.9f, 138.5f, 124.9f, 123.5f, 65.8f, 130.2f, 96.6f, 139.4f, 124.9f, 114.9f } },
        };
        [Tooltip("Hot macro-cycle factor on the seconds — applied to non-breathers only.")]
        public float hotFactor = 1.036f;
        public int macroHotLen = 150;
        public int macroCoolLen = 100;

        [Header("Roles (seconds as on a 10x10 board)")]
        [Tooltip("At/below this a level is a protected breather: no heat, no jitter, no wall/grind/start gates.")]
        public float breatherSeconds = 105.5f;
        [Tooltip("From here a level is a build-up.")]
        public float buildSeconds = 124.9f;
        [Tooltip("From here a level is a peak.")]
        public float peakSeconds = 147.2f;

        [Header("Milestones")]
        public int milestoneEvery = 50;
        public int milestoneSize = 11;
        [Tooltip("A milestone after the learning levels asks for a board between these (its own seconds, on the milestone size).")]
        public float milestoneMinSeconds = 203.8f;
        public float milestoneMaxSeconds = 213.0f;
        [Tooltip("The longest board a milestone slot accepts.")]
        public float milestonePoolMaxSeconds = 234.0f;

        [Header("Matching & variation")]
        [Tooltip("Deterministic wobble (hash of level number) so the wave is never recognizable: ± this share of the seconds.")]
        public float jitter = 0.025f;
        [Tooltip("RELATIVE tolerance: accept a board within target ± (this × target seconds). 0.06 = ±6%. " +
                 "The rating itself is ~7% off on a board, so much tighter than this only slows the build.")]
        public float timeTol = 0.06f;

        [Serializable]
        public class SizeRange
        {
            public int size;
            [Tooltip("Shortest board random generation still produces at this size, in seconds.")]
            public float minSeconds;
            [Tooltip("Longest clean board worth asking of this size, in seconds.")]
            public float maxSeconds;
        }

        [Header("Board size")]
        [Tooltip("Decade slots X1..X0 of the wave phase.")]
        public int[] sizeRotation = { 8, 10, 10, 9, 10, 10, 9, 10, 10, 10 };
        [Tooltip("What each size can give, in seconds. When a slot's seconds fall outside its rotation size's range, " +
                 "the smallest size whose range fits is used instead.")]
        public SizeRange[] sizeRanges =
        {
            // floors reflect what RANDOM generation actually produces (measured), not hand-made boards
            new SizeRange { size = 8,  minSeconds = 45.5f,  maxSeconds = 119.0f },
            new SizeRange { size = 9,  minSeconds = 73.9f,  maxSeconds = 159.2f },
            new SizeRange { size = 10, minSeconds = 100.4f, maxSeconds = 213.3f },
            new SizeRange { size = 11, minSeconds = 146.4f, maxSeconds = 277.8f },
        };
        [Tooltip("The same kind of level takes this much longer per extra board row (1.231 = +23%). " +
                 "Used to read a level's role and the grind gate on any board size.")]
        public float secondsPerRow = 1.231f;

        [Header("Quality gates")]
        [Tooltip("No guessing, ever.")]
        public int maxTrials = 0;
        [Tooltip("The single most expensive deduction step may hold at most this share of the rating.")]
        public float maxPeakShare = 0.40f;
        [Tooltip("Grinder reject: a paid step must be worth at least this many seconds (as on a 10x10 board).")]
        public float minSecondsPerStep = 6.5f;
        [Tooltip("Marathon reject.")]
        public int maxPaidSteps = 18;
        public int milestoneMaxPaidSteps = 24;
        [Tooltip("Levels must open gently: share of the rating paid in the first third of the solve.")]
        public float maxStartShare = 0.55f;
        public float peakMaxStartShare = 0.65f;

        /// <summary>The gate fields above as plain data — what the pool builder consumes.</summary>
        public GenerationGates Gates => new GenerationGates
        {
            maxTrials = maxTrials,
            maxPeakShare = maxPeakShare,
            minSecondsPerStep = minSecondsPerStep,
            secondsPerRow = secondsPerRow,
            maxPaidSteps = maxPaidSteps,
            milestoneMaxPaidSteps = milestoneMaxPaidSteps,
            maxStartShare = maxStartShare,
            peakMaxStartShare = peakMaxStartShare,
        };

        public LevelTarget GetTarget(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            bool milestone = milestoneEvery > 0 && level % milestoneEvery == 0;
            float pf = PlaceFactorAt(level);

            if (level <= LearnEnd)
            {
                var e = learnLevels[level - 1];
                return MakeTarget(level, e.size, e.seconds, milestone ? Role.Milestone : RoleOf(e.seconds, e.size), pf);
            }

            if (milestone)
            {
                float ask = milestoneMinSeconds + (milestoneMaxSeconds - milestoneMinSeconds) * Hash01((uint)level * 2u + 1u);
                var m = MakeTarget(level, milestoneSize, ask, Role.Milestone, pf);
                // any board from the shortest milestone up to the longest hoarded one qualifies
                m.minSeconds = milestoneMinSeconds * pf * (1f - timeTol);
                m.maxSeconds = milestonePoolMaxSeconds * pf * (1f + timeTol);
                return m;
            }

            int at = level - LearnEnd - 1;
            var tpl = waves[TemplateIndex(at / WaveLen)].seconds;
            float s = tpl[at % WaveLen % tpl.Length];
            int preferred = sizeRotation[(level - 1) % sizeRotation.Length];
            Role role = RoleOf(s, FitSize(s, preferred));
            if (role != Role.Breather)   // breathers are protected: no heat, no jitter
            {
                if (IsHot(level)) s *= hotFactor;
                s *= 1f + JitterAt(level);
            }
            return MakeTarget(level, FitSize(s, preferred), s, role, pf);
        }

        /// <summary>The place factor of a level, read between the points of <see cref="placeFactor"/>.</summary>
        public float PlaceFactorAt(int level)
        {
            if (placeFactor == null || placeFactor.Length == 0) return 1f;
            if (level <= placeFactor[0].level) return placeFactor[0].factor;
            for (int i = 1; i < placeFactor.Length; i++)
            {
                var a = placeFactor[i - 1];
                var b = placeFactor[i];
                if (level > b.level) continue;
                double u = (Math.Log(level) - Math.Log(a.level)) / (Math.Log(b.level) - Math.Log(a.level));
                return (float)(a.factor + (b.factor - a.factor) * u);
            }
            return placeFactor[placeFactor.Length - 1].factor;
        }

        /// <summary>A slot asking for a board of these seconds on this size, with the ± timeTol window in
        /// real seconds (the campaign passes its level's place factor; a source whose players are not
        /// new, like the daily tiers, passes 1). Shared by every target source so they all ask in the same unit.</summary>
        public LevelTarget MakeTarget(int level, int size, float boardSeconds, Role role, float placeFactor)
        {
            float s = boardSeconds * placeFactor;
            return new LevelTarget
            {
                level = level,
                boardSize = size,
                boardSeconds = boardSeconds,
                placeFactor = placeFactor,
                targetSeconds = s,
                minSeconds = s * (1f - timeTol),
                maxSeconds = s * (1f + timeTol),
                role = role,
            };
        }

        int WaveLen => waves[0].seconds.Length;

        /// <summary>How many times longer the same kind of level is on this size than on 10x10.</summary>
        public float RowScale(int size) => (float)Math.Pow(secondsPerRow, size - 10);

        // Keep the rotation size when it can give these seconds; otherwise the smallest size that
        // can (so short slots drop to small boards).
        public int FitSize(float seconds, int preferred)
        {
            foreach (var r in sizeRanges)
                if (r.size == preferred && seconds >= r.minSeconds && seconds <= r.maxSeconds)
                    return preferred;
            int best = preferred, bestSize = int.MaxValue;
            foreach (var r in sizeRanges)
                if (seconds >= r.minSeconds && seconds <= r.maxSeconds && r.size < bestSize)
                { best = r.size; bestSize = r.size; }
            return best;
        }

        /// <summary>
        /// The size for a level stated as on a 10x10 board (the daily tiers): the rotation size when
        /// it can give that level, otherwise the smallest size that can. The board to ask for is then
        /// <paramref name="seconds10"/> × <see cref="RowScale"/> of the size returned.
        /// </summary>
        public int FitSizeAs10x10(float seconds10, int preferred)
        {
            bool Fits(SizeRange r) { float s = seconds10 * RowScale(r.size); return s >= r.minSeconds && s <= r.maxSeconds; }
            foreach (var r in sizeRanges)
                if (r.size == preferred && Fits(r)) return preferred;
            int best = preferred, bestSize = int.MaxValue;
            foreach (var r in sizeRanges)
                if (Fits(r) && r.size < bestSize) { best = r.size; bestSize = r.size; }
            return best;
        }

        /// <summary>The role of a board of these seconds on this size (read as on a 10x10 board).</summary>
        public Role RoleOf(float seconds, int size)
        {
            float s10 = seconds / RowScale(size);
            return s10 <= breatherSeconds ? Role.Breather :
                   s10 < buildSeconds ? Role.Normal :
                   s10 < peakSeconds ? Role.Build : Role.Peak;
        }

        bool IsHot(int level) =>
            (level - LearnEnd - 1) % (macroHotLen + macroCoolLen) < macroHotLen;

        // Template per wave: a step of 1..len-1 each wave guarantees no immediate repeat.
        int TemplateIndex(int wave)
        {
            int len = waves.Length;
            if (len < 2) return 0;
            int idx = (int)(Hash(0xC0FFEEu) % (uint)len);
            for (int w = 1; w <= wave; w++)
                idx = (idx + 1 + (int)(Hash((uint)w) % (uint)(len - 1))) % len;
            return idx;
        }

        float JitterAt(int level) => (Hash01((uint)level) * 2f - 1f) * jitter;

        static float Hash01(uint x) => Hash(x) / (float)uint.MaxValue;

        static uint Hash(uint x)   // lowbias32 — deterministic across runs and platforms
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }
}
