using System;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Daily Challenge tuning as an EDITOR authoring asset — the daily counterpart of
    /// <see cref="CampaignCurveConfig"/>, but not a curve: a target FACTORY for pre-built pools.
    ///
    /// DailyProgress = campaign level reached + daily boards solved → a tier; each tier owns a
    /// pool of weeks*7 levels. Selection is calendar-driven and deterministic (fair leaderboards):
    ///   dayIndex = days since epoch (UTC) · slot = dayIndex % poolSize
    /// Pools are whole weeks and the epoch is a Monday, so slot % 7 IS the weekday and the weekly
    /// wave below stays aligned forever as the pool rotates.
    ///
    /// Everything is in SECONDS, like the campaign curve — the board's own seconds, the unit
    /// TimeRater measures a board in. A tier says what its board takes and the board size follows
    /// from the seconds, as in the campaign's wave (a slot that says 152 asks for a board rated 152).
    ///
    /// THE DAILY IS HARDER THAN THE CAMPAIGN WHERE ITS PLAYERS ARE. What a player really takes is
    /// the board's seconds × the campaign's place factor at his level, so a tier's seconds are
    /// set for the middle of its players: the regular campaign level there takes them ~115-130 s,
    /// the daily ~145 s in the first tier (+25%), ~170 s in the second (+30%), ~180 s in the last
    /// (+40%, a milestone a day). That is also why there are tiers at all: everyone in a tier gets
    /// the same board on the same day, and the same board is a third longer for a new player than
    /// for an old one. Three tiers cover that; the first is not made harder than this, because up
    /// to level ~80 a hard level costs players (measured, Nov 2026), and 1 in 10 who start the
    /// daily there do not finish it (1 in 30 in the last tier).
    ///
    /// Size ranges, tolerance and quality gates come from the referenced campaign curve. Jitter
    /// is a hash of (tier, slot) so same-weekday slots aren't clones and builds reproduce. No
    /// milestones in dailies.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyChallengeCurve", menuName = "QueensPuzzle/Daily Challenge Curve", order = 2)]
    public class DailyChallengeCurveConfig : ScriptableObject
    {
        [Serializable]
        public class Tier
        {
            [Tooltip("Pool folder = Assets/Levels/Sets/Daily{name}; also the future pack suffix.")]
            public string name;
            [Tooltip("DailyProgress range (inclusive). The last tier is open-ended upward.")]
            public int progressMin;
            public int progressMax;
            [Tooltip("The tier's band in the board's seconds — the weekly wave is clamped into it.")]
            public float secondsMin;
            public float secondsMax;
            [Tooltip("The wave's center in the board's seconds: weekday factors apply around this. The player takes this × the place factor at his level.")]
            public float baseSeconds;
            [Tooltip("Pool length in WHOLE WEEKS (slots = weeks*7) so rotation keeps weekday alignment.")]
            public int weeks;

            public int PoolSize => weeks * 7;
        }

        [Tooltip("Shared source of truth: size ranges, seconds per row, match tolerance, quality gates.")]
        public CampaignCurveConfig campaign;

        [Tooltip("Day 0, UTC, yyyy-MM-dd — MUST be a Monday so slot % 7 = weekday.")]
        public string epochDate = "2026-01-05";

        // The week must be felt: the easiest and the hardest day are a quarter apart. A spread under the
        // rating's own error (~7%) and the match tolerance (±6%) is no wave at all.
        [Tooltip("Mon..Sun factors on each tier's baseSeconds. Saturday = weekly peak, Sunday and Monday = breathers.")]
        public float[] weekdayFactor = { 0.89f, 0.965f, 1.03f, 0.93f, 1.065f, 1.125f, 0.89f };

        [Tooltip("± spread (share of the seconds) so same-weekday slots aren't clones. Hash of (tier, slot) — builds reproduce.")]
        public float jitter = 0.025f;

        [Tooltip("Daily unlocks at campaign 21, so there is no tier below DailyProgress 21.")]
        public Tier[] tiers =
        {
            // place factor in the middle of the tier ~1.28 / ~1.12 / ~0.93  →  ~145 / ~170 / ~180 s for the player
            new Tier { name = "A", progressMin = 21,  progressMax = 80,           secondsMin = 98f,  secondsMax = 130f, baseSeconds = 113f, weeks = 8 },
            new Tier { name = "B", progressMin = 81,  progressMax = 250,          secondsMin = 132f, secondsMax = 175f, baseSeconds = 152f, weeks = 8 },
            new Tier { name = "C", progressMin = 251, progressMax = int.MaxValue, secondsMin = 169f, secondsMax = 223f, baseSeconds = 194f, weeks = 52 },
        };

        public DateTime Epoch =>
            DateTime.TryParseExact(epochDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var d) ? d.Date : new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Tier index for a player's DailyProgress. Progress below the first tier still
        /// maps to tier 0 — whether the feature is unlocked at all is the caller's gate.</summary>
        public int TierOf(int dailyProgress)
        {
            for (int i = 0; i < tiers.Length; i++)
                if (dailyProgress <= tiers[i].progressMax) return i;
            return tiers.Length - 1;
        }

        /// <summary>Days since epoch for a UTC time — the calendar index all players share.</summary>
        public int DayIndex(DateTime utc) => (int)(utc.Date - Epoch).TotalDays;

        /// <summary>Rotating pool slot for a calendar day (poolSize % 7 == 0 keeps weekdays aligned).</summary>
        public int SlotOf(int tier, int dayIndex)
        {
            int n = tiers[tier].PoolSize;
            return ((dayIndex % n) + n) % n;
        }

        /// <summary>
        /// Build-time target for one pool slot — the same LevelTarget shape the campaign curve
        /// produces, so PoolBuildRunner is blind to the source. level = slot + 1 (1-based assets,
        /// pack index = slot).
        /// </summary>
        public LevelTarget GetSlotTarget(int tier, int slot)
        {
            var tr = tiers[tier];
            float seconds = tr.baseSeconds * weekdayFactor[slot % 7];
            seconds *= 1f + (Hash01((uint)((tier + 1) * 8191 + slot)) * 2f - 1f) * jitter;
            seconds = Mathf.Clamp(seconds, tr.secondsMin, tr.secondsMax);

            int size = campaign.WaveSize(seconds);   // the board size follows the seconds, as in the campaign's wave
            return campaign.MakeTarget(slot + 1, size, seconds, campaign.RoleOf(seconds, size), 1f);   // the tier's seconds already stand for its players
        }

        /// <summary>All targets of one tier's pool — the builder's shopping list.</summary>
        public System.Collections.Generic.List<LevelTarget> GetTierTargets(int tier)
        {
            int n = tiers[tier].PoolSize;
            var list = new System.Collections.Generic.List<LevelTarget>(n);
            for (int s = 0; s < n; s++) list.Add(GetSlotTarget(tier, s));
            return list;
        }

        static float Hash01(uint x)   // lowbias32 — same hash family as the campaign curve
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x / (float)uint.MaxValue;
        }
    }
}
