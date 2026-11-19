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
    /// Everything is in SECONDS, like the campaign curve — the board's own seconds, for an
    /// experienced player (daily players are not new, so no place factor). A tier states its
    /// seconds as on a 10x10 board; a slot whose rotation size is smaller asks for the same kind
    /// of level in fewer seconds (the campaign curve's secondsPerRow). Size ranges, tolerance and
    /// quality gates come from the referenced campaign curve. Jitter is a hash of (tier, slot)
    /// so same-weekday slots aren't clones and builds reproduce. No milestones in dailies.
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
            [Tooltip("The tier's band in seconds (as on a 10x10 board) — the weekly wave is clamped into it.")]
            public float secondsMin;
            public float secondsMax;
            [Tooltip("The wave's center in seconds (as on a 10x10 board): weekday factors apply around this.")]
            public float baseSeconds;
            [Tooltip("Pool length in WHOLE WEEKS (slots = weeks*7) so rotation keeps weekday alignment.")]
            public int weeks;

            public int PoolSize => weeks * 7;
        }

        [Tooltip("Shared source of truth: size ranges, seconds per row, match tolerance, quality gates.")]
        public CampaignCurveConfig campaign;

        [Tooltip("Day 0, UTC, yyyy-MM-dd — MUST be a Monday so slot % 7 = weekday.")]
        public string epochDate = "2026-01-05";

        [Tooltip("Mon..Sun factors on each tier's baseSeconds. Saturday = weekly peak, Sunday = breather.")]
        public float[] weekdayFactor = { 0.957f, 0.986f, 1.013f, 0.972f, 1.026f, 1.050f, 0.957f };

        [Tooltip("± spread (share of the seconds) so same-weekday slots aren't clones. Hash of (tier, slot) — builds reproduce.")]
        public float jitter = 0.025f;

        [Tooltip("Daily unlocks at campaign 21, so there is no tier below DailyProgress 21.")]
        public Tier[] tiers =
        {
            new Tier { name = "Tier2",   progressMin = 21,  progressMax = 50,           secondsMin = 97.7f,  secondsMax = 114.9f, baseSeconds = 107.2f, weeks = 5 },
            new Tier { name = "Tier3",   progressMin = 51,  progressMax = 100,          secondsMin = 108.9f, secondsMax = 124.9f, baseSeconds = 117.6f, weeks = 8 },
            new Tier { name = "Tier4",   progressMin = 101, progressMax = 150,          secondsMin = 118.9f, secondsMax = 135.0f, baseSeconds = 127.1f, weeks = 8 },
            new Tier { name = "Tier5",   progressMin = 151, progressMax = 200,          secondsMin = 130.2f, secondsMax = 147.2f, baseSeconds = 139.4f, weeks = 8 },
            new Tier { name = "Endless", progressMin = 201, progressMax = int.MaxValue, secondsMin = 139.4f, secondsMax = 154.0f, baseSeconds = 144.9f, weeks = 52 },
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
            float s10 = tr.baseSeconds * weekdayFactor[slot % 7];   // as on a 10x10 board
            s10 *= 1f + (Hash01((uint)((tier + 1) * 8191 + slot)) * 2f - 1f) * jitter;
            s10 = Mathf.Clamp(s10, tr.secondsMin, tr.secondsMax);

            int size = campaign.FitSizeAs10x10(s10, campaign.sizeRotation[slot % campaign.sizeRotation.Length]);
            float seconds = s10 * campaign.RowScale(size);
            return campaign.MakeTarget(slot + 1, size, seconds, campaign.RoleOf(seconds, size), 1f);   // place factor 1: daily players are not new
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
