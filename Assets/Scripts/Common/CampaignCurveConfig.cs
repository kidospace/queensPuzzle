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
    ///   L1..learnEnd   the listed levels, one by one: board size and seconds (<see cref="learnLevels"/>) —
    ///                  the trick lessons and the climb to the game's cruising height.
    ///   after          ONE wave, the same to the end of the game (<see cref="waveShape"/> around
    ///                  <see cref="waveBaseSeconds"/>), + a milestone every milestoneEvery levels.
    ///
    /// Nothing here is random: the same level number always asks for the same thing.
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
        [Serializable]
        public struct TrickOpening
        {
            public Trick trick;
            [Tooltip("The first level whose board may need this trick. It and the two after it are the trick's lesson.")]
            public int level;
            [Tooltip("How many times the third lesson level must need the trick. 0 = the general number (lessonMinUses). " +
                     "For a trick too rare to come twice on one board.")]
            public int proveUses;
            [Tooltip("REAL seconds the first lesson level takes on top of its board, because the trick is new to the player. " +
                     "The board asked for is lighter by this much, so the level as a whole still takes what the list says. " +
                     "From the second meeting the time is back to the board's own.")]
            public float firstSeconds;
            [Tooltip("REAL seconds every queen that needs the trick still costs on top, on the player's NEXT meetings with it " +
                     "(a meeting = a level whose board needs the trick). How many meetings: earlyLevels.")]
            public float earlySeconds;
            [Tooltip("How many levels after the first one still carry earlySeconds. After them the trick costs what the rater says.")]
            public int earlyLevels;
        }

        [Serializable]
        public class PlacePoint
        {
            public int level;
            [Tooltip("Real seconds of the regular player at this level / the board's own seconds.")]
            public float factor;
        }

        // Measured on recordings (22 Oct – 10 Nov 2026): the regular player's real time on each level
        // against the board's own rating, middle value per stretch of levels. Levels 1-12 are a smooth line
        // through what was measured on the boards those levels had then (4.9 … 1.4): the per-level values
        // belonged to those boards (and to their sizes — the rating was not fitted under 8x8), so on other
        // boards they misled. Read the real seconds of levels 1-12 as rough. Nothing is measured past ~1,200, the last value holds.
        [Header("Place in the game")]
        [Tooltip("How much longer (or shorter) the same board takes at a level than it takes an experienced player. " +
                 "Read between the points on a log scale of the level; before the first and after the last point the value holds.")]
        public PlacePoint[] placeFactor =
        {
            // 1-9 lowered 24 Nov 2026 (1-7 by 8%, 8 by 5%, 9 by 2%): the rater now prices X marks openly and gives the small
            // boards more seconds than the one these points were measured with, so the forecast of levels 1-8 read 6-10%
            // long on both shipped sets (V2, V3). The listed boards were left as they are - nothing is asked to be harder.
            new PlacePoint { level = 1,   factor = 4.50f },
            new PlacePoint { level = 2,   factor = 3.68f },
            new PlacePoint { level = 3,   factor = 2.39f },
            new PlacePoint { level = 4,   factor = 2.12f },
            new PlacePoint { level = 5,   factor = 1.93f },
            new PlacePoint { level = 6,   factor = 1.79f },
            new PlacePoint { level = 7,   factor = 1.70f },
            new PlacePoint { level = 8,   factor = 1.66f },
            new PlacePoint { level = 9,   factor = 1.62f },
            new PlacePoint { level = 10,  factor = 1.58f },
            new PlacePoint { level = 11,  factor = 1.50f },
            new PlacePoint { level = 12,  factor = 1.42f },
            // 13-26: 1.32 was too low (24 Nov 2026) - on three level sets with different boards (V1, V2, V3) the forecast
            // there was 6-10% short against the levels before and after, and queens that need no trick (the new player
            // alone) read +10% there. The drop from 1.42 came too early: held to level 26, back on the old line by 40.
            new PlacePoint { level = 13,   factor = 1.43f },
            new PlacePoint { level = 26,   factor = 1.43f },
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

        // When each trick enters the game. Before its level a board that needs the trick is rejected. The
        // opening level and the two after it are the trick's LESSON: learn (the trick once, and no other
        // recently opened trick beside it), practise, prove — the seconds of those three levels in
        // learnLevels are what make them easy → harder → hard. A trick missing from the list is never restricted.
        // The first interstitial shows when level 10 is won (GameConfig.StartShowInterAtLevel = 11), and that is where
        // most players of every speed stop. So level 10 closes a lesson (a win worth having) and levels 11-13 are
        // easy boards with nothing new in them: a player who gets an interstitial after a win there stops 6 times
        // in 100, against 1 without it (recordings, Nov 2026), so no lesson stands on those levels. Move the
        // openings together with that number.
        // The order (24 Nov 2026): 4-6 a line with one place left · 7 a rest · 8-10 line→region · 11-13 easy ·
        // 14-16 region→line · 17 easy · 18-20 squeeze · 21-24 nothing new (three lessons in ten levels
        // was too dense) · 25-27 "the whole colour is closed" · 28-30 nothing new · 31-33 the first subset.
        //
        // What a NEW trick adds to a level's time — firstSeconds on the first meeting, earlySeconds a use on the
        // next earlyLevels meetings (a meeting = a level whose board needs the trick) — is one rule for every
        // trick (NewTrickSeconds). MEASURED, it is 0 for all of them (1,247 players recorded from level 1,
        // Nov 2026, by the player's 1st, 2nd, 3rd … level that needs the trick, 90% ranges, with and without
        // the time of wrong queens): against the forecast this curve makes — the board × the place factor —
        // no trick's queen takes longer on the first meetings. The place factor of the early levels was itself
        // measured on levels where those tricks were new, so that time is already in it; adding seconds here
        // counts it twice. (In raw seconds a first meeting does look slower — line→region +2, region→line +7,
        // the colour read +8, a subset +10 — but that is the early levels' place factor, not the trick.)
        // What does stay up is the share who misplace a queen first, 6–14 points for about three meetings in
        // every trick, which is why a lesson level limits what stands beside its trick (lessonMaxOthers).
        // Keep the fields at 0 unless a new measurement, made against the forecast, says otherwise.
        [Header("Tricks")]
        [Tooltip("The level each trick opens at. Its lesson is that level and the two after it.")]
        public TrickOpening[] trickOpenings =
        {
            new TrickOpening { trick = Trick.LineSingle,            level = 4 },
            new TrickOpening { trick = Trick.LineToRegion,          level = 8 },   // its third level (10) is the one before the first ad
            new TrickOpening { trick = Trick.RegionToLine,          level = 14 },   // not before: 11-13 are the levels of the first ads
            new TrickOpening { trick = Trick.Squeeze,               level = 18 },
            new TrickOpening { trick = Trick.ColourClosed,          level = 25 },   // until then a line single shows without reading a colour
            new TrickOpening { trick = Trick.Subset2LinesToRegions, level = 31 },
            new TrickOpening { trick = Trick.Subset2RegionsToLines, level = 41 },
            new TrickOpening { trick = Trick.Subset3,               level = 61, proveUses = 1 },   // twice on one board made it far longer than the level asks
            new TrickOpening { trick = Trick.Choke,                 level = 81 },
            new TrickOpening { trick = Trick.Fish,                  level = 101, proveUses = 1 },   // two on one board: 1 in ~1,300 boards
        };
        [Tooltip("How many times the board must need the lesson's trick, on lesson level 1, 2, 3.")]
        public int[] lessonMinUses = { 1, 1, 2 };
        [Tooltip("At most this many times, on lesson level 1, 2, 3 (0 = no limit).")]
        public int[] lessonMaxUses = { 1, 2, 0 };
        [Tooltip("On the first lesson level the new trick stands alone: a trick that opened fewer than this many " +
                 "levels earlier is not allowed beside it (a line with one place left always is).")]
        public int lessonAloneFor = 8;
        [Tooltip("On lesson level 1, 2, 3: at most this many uses of other YOUNG tricks beside the lesson's own (-1 = no limit). " +
                 "While a trick is new, players err 8-10 points more on it for about three meetings, so it should not come " +
                 "piled under other tricks they are still learning.")]
        public int[] lessonMaxOthers = { 1, 1, 2 };
        [Tooltip("A trick is YOUNG at a level while it opened fewer than this many levels earlier. Tricks the player has had " +
                 "for longer stand beside a lesson freely (a long board cannot do without them); a plain line with one place left always does.")]
        public int lessonYoungFor = 12;

        [Serializable]
        public struct LearnLevel
        {
            public int size;
            [Tooltip("The board's own seconds (experienced player). The player at this level takes this × the place factor.")]
            public float seconds;
            [Tooltip("The level opens with one queen already on the board.")]
            public bool dog;
        }

        static LearnLevel L(int size, float seconds, bool dog = false) => new LearnLevel { size = size, seconds = seconds, dog = dog };

        [Header("Listed levels (level 1 .. the length of this list; the wave takes over after it)")]
        [Tooltip("One entry per level: its board size and its seconds. A level on a milestone number is a milestone.")]
        public LearnLevel[] learnLevels =
        {
            L(4, 4.5f), L(4, 3.5f, true), L(5, 8.0f, true), L(6, 19.5f, true), L(6, 21.5f, true), L(6, 25.0f, true), L(6, 16.0f, true), L(6, 28.0f, true), L(6, 30.0f, true), L(7, 38.5f, true),   // 1-10
            // 11-13 easy after the first ad; then the lessons that stood on 12-18, two levels later
            L(6, 26.0f), L(6, 28.0f), L(7, 33.2f), L(7, 38.3f), L(7, 46.2f), L(8, 53.5f), L(8, 46.2f), L(7, 42.5f), L(7, 51.7f), L(8, 60.9f),   // 11-20
            L(8, 52.8f), L(8, 61.4f), L(9, 68.6f), L(9, 76.2f), L(8, 52.4f), L(8, 59.4f), L(8, 65.5f), L(9, 71.2f), L(9, 77.8f), L(9, 84.4f),   // 21-30
            L(8, 64.7f), L(9, 72.3f), L(9, 84.6f), L(9, 72.7f), L(9, 74.0f), L(8, 71.2f), L(9, 80.5f), L(9, 87.8f), L(9, 95.1f), L(8, 72.7f),   // 31-40
            L(8, 73.6f), L(9, 81.9f), L(9, 95.5f), L(9, 81.8f), L(9, 82.9f), L(9, 79.4f), L(9, 89.5f), L(9, 97.2f), L(10, 104.9f), L(10, 131.0f),   // 41-50
            L(9, 80.2f), L(9, 87.5f), L(9, 83.5f), L(9, 93.5f), L(10, 101.1f), L(10, 108.6f), L(9, 82.3f), L(9, 89.8f), L(9, 85.6f), L(9, 96.0f),   // 51-60
            L(9, 84.0f), L(9, 93.1f), L(10, 108.1f), L(9, 92.1f), L(9, 92.9f), L(9, 88.4f), L(9, 99.7f), L(10, 108.2f), L(10, 116.9f), L(9, 87.1f),   // 61-70
            L(9, 95.4f), L(9, 90.5f), L(10, 102.2f), L(10, 111.1f), L(10, 120.0f), L(9, 88.2f), L(9, 97.0f), L(9, 91.8f), L(10, 104.2f), L(10, 113.5f),   // 71-80
            L(9, 91.1f), L(10, 100.7f), L(10, 116.6f), L(9, 99.2f), L(9, 99.1f), L(9, 93.3f), L(10, 106.5f), L(10, 116.4f), L(10, 126.2f), L(9, 90.3f),   // 81-90
            L(10, 100.1f), L(9, 94.1f), L(10, 107.9f), L(10, 118.2f), L(10, 128.5f), L(9, 90.9f), L(10, 101.1f), L(9, 94.8f), L(10, 109.2f), L(10, 155.6f),   // 91-100
            L(9, 95.2f), L(10, 115.0f), L(10, 121.6f), L(10, 103.3f), L(10, 102.5f), L(9, 95.8f), L(10, 111.0f), L(10, 122.3f), L(10, 133.7f), L(9, 92.1f),   // 101-110
            L(10, 103.4f), L(9, 96.4f), L(10, 112.3f), L(10, 124.1f), L(10, 135.9f), L(9, 92.5f), L(10, 104.2f), L(9, 96.8f), L(10, 113.2f), L(10, 125.3f),   // 111-120
            L(10, 137.4f), L(9, 92.5f), L(10, 104.6f), L(9, 97.0f), L(10, 113.8f), L(10, 126.5f), L(10, 139.1f), L(9, 92.6f), L(10, 105.2f), L(9, 97.4f),   // 121-130
            L(10, 115.0f), L(10, 128.0f), L(10, 141.1f), L(9, 92.9f), L(10, 105.9f), L(9, 97.8f), L(10, 116.0f), L(10, 129.6f), L(10, 143.1f), L(9, 93.2f),   // 131-140
            L(10, 106.7f), L(9, 98.3f), L(10, 117.1f), L(10, 131.1f), L(10, 145.1f), L(9, 93.4f), L(10, 107.4f), L(9, 98.7f), L(10, 118.1f), L(11, 165.5f),   // 141-150
        };

        public int LearnEnd => learnLevels.Length;

        [Header("The wave (every level after the list)")]
        [Tooltip("REAL seconds the middle of the wave takes the regular player — the same at level 151 and at level 9,000. " +
                 "The board asked for is this ÷ the level's place factor, so boards get longer as players get faster.")]
        public float waveBaseSeconds = 130f;
        [Tooltip("One wave, level by level, as shares of the base (mean 1): up, up, a small dip, up, up, the peak — " +
                 "then back to the first. The step from the last to the first is the only real drop. " +
                 "The listed levels use a narrower wave early (±15% up to level 60) and widen to this one by level 150.")]
        public float[] waveShape = { 0.805f, 0.926f, 0.850f, 1.018f, 1.141f, 1.262f };
        [Tooltip("Board size of a wave level, by the board's own seconds: 9x9 from the first value, 10x10 from the second, " +
                 "11x11 from the third (8x8 under the first).")]
        public float[] waveSizeFromSeconds = { 74f, 100f, 160f };

        [Header("Roles (seconds as on a 10x10 board)")]
        [Tooltip("At/below this a level is a protected breather: no wall/grind/start gates.")]
        public float breatherSeconds = 105.5f;
        [Tooltip("From here a level is a build-up.")]
        public float buildSeconds = 124.9f;
        [Tooltip("From here a level is a peak.")]
        public float peakSeconds = 147.2f;

        [Header("Milestones")]
        public int milestoneEvery = 50;
        public int milestoneSize = 11;
        [Tooltip("REAL seconds a milestone after the list takes the regular player — the same at every one. " +
                 "The board asked for is this ÷ the level's place factor, on the milestone size.")]
        public float milestoneSeconds = 185f;

        // A drawn level is built around a drawing from the pool (Sets/DRAWINGS): the shape stays, the rest
        // of the board is completed to the level's seconds. Which levels are drawn is written here, so it is
        // known before anything is built; WHICH drawing a level gets is the builder's choice, kept on the level.
        // A trick's lesson level, a milestone and a level that opens with a queen stay ordinary.
        public const int DrawnCycle = 50;
        [Header("Drawn levels")]
        [Tooltip("No level before this one is drawn.")]
        public int drawnFromLevel = 15;
        [Tooltip("The drawn places in every 50 levels: 23 = levels 23, 73, 123 … Ten places = 20% of the levels.")]
        public int[] drawnPlaces = { 3, 8, 14, 17, 23, 29, 34, 38, 43, 47 };
        [Tooltip("Two levels this close (in levels) are never built from the same kind of drawing.")]
        public int drawnKindApart = 40;

        /// <summary>Is this level number one of the drawn places?</summary>
        public bool IsDrawnPlace(int level) =>
            drawnPlaces != null && level >= drawnFromLevel && Array.IndexOf(drawnPlaces, level % DrawnCycle) >= 0;

        // A hard level is shown as one in the game (the Hard skin on the level button and in play). It is
        // a matter of the PLACE, not of the board: the same board is hard at level 24 and plain at 200. So
        // it is decided here and written into the exported pack per place. Every milestone is hard. Before
        // the wave the listed levels below are too — the real peaks of the list, each the longest level
        // around it (17-24% above its neighbours; a milestone is 40-50% above), one every 11-25 levels:
        // a player there wins about 8 levels a day, so he meets one every day and a half to three days.
        // Marking a level does not make it harder: its seconds are whatever the list says.
        [Header("Hard levels")]
        [Tooltip("Levels shown as hard besides the milestones. Pick real peaks: the longest level around.")]
        public int[] hardLevels = { 24, 39, 75, 89, 115, 127, 139 };

        /// <summary>Is this level shown as a hard one?</summary>
        public bool IsHard(int level) =>
            (milestoneEvery > 0 && level % milestoneEvery == 0) || (hardLevels != null && Array.IndexOf(hardLevels, level) >= 0);

        [Header("Matching & variation")]
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
        // All in the board's SECONDS (TimeRater), spread over its queens. The values were set on 1,624
        // random boards, 8x8 to 11x11: wall rejects ~5 of 100, start ~27 (peak slots ~11), steps ~8
        // (milestones ~0), seconds per step none — it only guards against a kind of board the
        // generator does not make today.
        // Re-set 24 Nov 2026, when TimeRater began to price the X marks where they are made: the same
        // boards spread their seconds a little differently over the queens, so wall 2.7 → 2.95 and
        // start 1.5 → 1.55 (peak 1.65 → 1.7) reject the same shares as before (3 / 28 / 11 of 100)
        // and give the same verdict on 98 of 100 boards.
        // Re-set again 26 Nov 2026, when a trick began to be paid for once (its X marks carried to the queens after
        // it): the trick queen now holds the whole price, so the heaviest queen stands higher — wall 2.95 → 3.05,
        // start and peak start unchanged; same shares rejected, the same verdict on 94 of 100 boards.
        [Tooltip("Wall reject: the heaviest queen may take at most this many times the average queen's seconds.")]
        public float maxWall = 3.05f;
        [Tooltip("Grinder reject: a use of a trick must be worth at least this many seconds (as on a 10x10 board).")]
        public float minSecondsPerStep = 8.5f;
        [Tooltip("Marathon reject: at most this many uses of a trick in the whole solve.")]
        public int maxSteps = 14;
        public int milestoneMaxSteps = 20;
        [Tooltip("Levels must open gently: the first third of the queens may take at most this many times " +
                 "what an even spread of the board's seconds would put there.")]
        public float maxStart = 1.55f;
        public float peakMaxStart = 1.7f;

        /// <summary>The gate fields above as plain data — what the pool builder consumes.</summary>
        public GenerationGates Gates => new GenerationGates
        {
            maxTrials = maxTrials,
            maxWall = maxWall,
            minSecondsPerStep = minSecondsPerStep,
            secondsPerRow = secondsPerRow,
            maxSteps = maxSteps,
            milestoneMaxSteps = milestoneMaxSteps,
            maxStart = maxStart,
            peakMaxStart = peakMaxStart,
        };

        public LevelTarget GetTarget(int level)
        {
            var t = SlotAt(level);
            t.tricks = TrickRuleAt(level);
            // The first lesson level of a trick: part of the level's time is the trick being new, not the board.
            // The level still takes what the list says; the board asked for is lighter by that part.
            float first = FirstSecondsAt(level, t.tricks);
            if (first > 0 && first < t.targetSeconds * 0.5f)
            {
                t.firstSeconds = first;
                t.targetSeconds -= first;
                t.minSeconds -= first;
                t.maxSeconds -= first;
                t.boardSeconds = t.targetSeconds / t.placeFactor;
            }
            t.hard = IsHard(level);
            t.drawn = IsDrawnPlace(level) && !t.hard && !t.tricks.lesson && !t.dog;
            return t;
        }

        /// <summary>
        /// The real seconds a meeting with a trick costs on top of the board, by which meeting it is for the player
        /// (1 = the first level that needs it) and how many queens of the level need it. THE one rule for it — the
        /// plan (a lesson's levels) and the built levels (counted as they really come) both ask here.
        /// </summary>
        public float NewTrickSeconds(Trick trick, int meeting, int uses)
        {
            if (trickOpenings == null || meeting < 1 || uses < 1) return 0f;
            foreach (var o in trickOpenings)
                if (o.trick == trick)
                    return meeting == 1 ? o.firstSeconds : meeting - 1 <= o.earlyLevels ? o.earlySeconds * uses : 0f;
            return 0f;
        }

        /// <summary>The real seconds a level takes on top of its board because it is one of the first meetings with a
        /// trick: by the plan, a lesson's three levels are the trick's meetings 1, 2 and 3.</summary>
        public float FirstSecondsAt(int level, in TrickRule rule)
        {
            if (!rule.lesson) return 0f;
            return NewTrickSeconds(rule.teach, rule.stage, Math.Max(1, rule.minUses));
        }

        /// <summary>What a level asks of its board's tricks, from <see cref="trickOpenings"/>.</summary>
        public TrickRule TrickRuleAt(int level)
        {
            var r = new TrickRule { maxOthers = -1 };
            if (trickOpenings == null) return r;
            int lessonAt = -1;
            for (int i = 0; i < trickOpenings.Length; i++)
            {
                var o = trickOpenings[i];
                if (level < o.level) r.forbidden |= 1 << (int)o.trick;                                   // not opened yet
                else if (level <= o.level + 2 && (lessonAt < 0 || o.level > trickOpenings[lessonAt].level)) lessonAt = i;
            }
            if (lessonAt < 0) return r;
            var l = trickOpenings[lessonAt];
            r.lesson = true;
            r.teach = l.trick;
            r.stage = level - l.level + 1;
            r.minUses = lessonMinUses != null && lessonMinUses.Length >= r.stage ? lessonMinUses[r.stage - 1] : 1;
            r.maxUses = lessonMaxUses != null && lessonMaxUses.Length >= r.stage ? lessonMaxUses[r.stage - 1] : 0;
            r.maxOthers = lessonMaxOthers != null && lessonMaxOthers.Length >= r.stage ? lessonMaxOthers[r.stage - 1] : -1;
            foreach (var o in trickOpenings)
                if (o.trick != l.trick && o.level <= level && o.level > level - lessonYoungFor) r.young |= 1 << (int)o.trick;
            if (r.stage == 3 && l.proveUses > 0) r.minUses = l.proveUses;
            if (r.stage == 1)   // the new trick alone: nothing that opened lately beside it
                foreach (var o in trickOpenings)
                    if (o.trick != l.trick && o.trick != Trick.LineSingle && o.level > level - lessonAloneFor)
                        r.forbidden |= 1 << (int)o.trick;
            return r;
        }

        LevelTarget SlotAt(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            bool milestone = milestoneEvery > 0 && level % milestoneEvery == 0;
            float pf = PlaceFactorAt(level);

            if (level <= LearnEnd)
            {
                var e = learnLevels[level - 1];
                var slot = MakeTarget(level, e.size, e.seconds, milestone ? Role.Milestone : RoleOf(e.seconds, e.size), pf);
                slot.dog = e.dog;
                return slot;
            }

            if (milestone)
            {
                return MakeTarget(level, milestoneSize, milestoneSeconds / pf, Role.Milestone, pf);
            }

            // the wave starts over after every milestone, so the level after one is always the wave's lowest
            int at = milestoneEvery > 0 ? level % milestoneEvery - 1 : level - LearnEnd - 1;
            float s = waveBaseSeconds * waveShape[at % waveShape.Length] / pf;
            int size = WaveSize(s);
            return MakeTarget(level, size, s, RoleOf(s, size), pf);
        }

        /// <summary>The board size of a wave level whose board takes these seconds.</summary>
        public int WaveSize(float boardSeconds)
        {
            int size = 8;
            for (int i = 0; i < waveSizeFromSeconds.Length; i++)
                if (boardSeconds >= waveSizeFromSeconds[i]) size = 9 + i;
            return size;
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
    }
}
