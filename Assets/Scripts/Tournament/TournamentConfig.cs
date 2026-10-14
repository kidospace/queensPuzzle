using System;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Tunables for the tournament, shipped as a single asset in Resources ("TournamentConfig").
    /// Only what the CLIENT decides lives here — when the feature opens up, when the card starts
    /// shouting that time is running out, and how many places get a gift. The tournament window
    /// and the group size come from the server, and the ranking is its call alone.
    ///
    /// Singleton: <see cref="Resources.Load"/> may only run on the main thread, and the tournament
    /// code awaits the server (a continuation can land on a background thread). So the asset is
    /// loaded once at boot, before any scene, and every later read is just the cached reference —
    /// safe from any thread. <see cref="Instance"/> is null when the asset is missing = feature off.
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Tournament Config", fileName = "TournamentConfig")]
    public class TournamentConfig : ScriptableObject {

        const string Resource = "TournamentConfig";

        [Tooltip("Display stage number (1-based) that unlocks the tournament — the lock card's text.")]
        public int unlockLevel = 30;

        [Tooltip("Minutes left when the card switches to 'ending soon'.")]
        public int endingSoonMinutes = 60;

        [Tooltip("How many top places get a gift. The prizes themselves are ours, not the server's.")]
        public int prizePlaces = 3;

        [Tooltip("Most wins kept while offline. Past this the oldest are dropped — they belong to a " +
                 "tournament that has closed anyway, and the feature is online-only by design.")]
        public int maxPendingWins = 200;

        public TimeSpan EndingSoonTime => TimeSpan.FromMinutes(endingSoonMinutes);

        // ---- what the rivals are called --------------------------------------------------
        //
        // The names themselves are in Resources/RivalNames.txt. These are only the proportions:
        // how many rivals look like players who named themselves, and what shape those names
        // take. Everyone else keeps the adjective-and-noun name the game hands out, which is
        // what a player who never opened the settings looks like — and most never do.

        [Tooltip("Share of rivals with a real-looking name at the unlock level. The rest keep the " +
                 "name the game generated, like a player who never changed it.")]
        [Range(0f, 1f)] public float realNamesAtUnlock = 0.30f;

        [Tooltip("The same share at veteranLevel and beyond. Higher, because a player who has been " +
                 "here that long has had far more chances to name themselves.")]
        [Range(0f, 1f)] public float realNamesAtVeteran = 0.70f;

        [Tooltip("The campaign level by which a player counts as a veteran. Between the unlock " +
                 "level and this one the share rises straight.")]
        public int veteranLevel = 1000;

        [Tooltip("Drawn once per tournament and added to the share, either way — so two groups at " +
                 "the same level are not the same group.")]
        [Range(0f, 0.5f)] public float realNamesSpread = 0.10f;

        [Tooltip("Of the real-looking names: how many carry a surname initial, as in 'Daniel K'.")]
        [Range(0f, 1f)] public float nameWithInitial = 0.16f;

        [Tooltip("Of the real-looking names: how many carry digits, as in 'Daniel22'. Keep it low " +
                 "— a table full of these is the one thing that reads as generated.")]
        [Range(0f, 1f)] public float nameWithDigits = 0.14f;

        /// <summary>How much of a group built for a player at <paramref name="level"/> should look
        /// like players who have named themselves. Straight line between the two anchors, plus
        /// this group's own draw.</summary>
        public float RealNameShare(int level) {
            float t = veteranLevel > unlockLevel
                ? Mathf.InverseLerp(unlockLevel, veteranLevel, level)
                : 1f;
            float centre = Mathf.Lerp(realNamesAtUnlock, realNamesAtVeteran, t);
            return Mathf.Clamp01(centre + UnityEngine.Random.Range(-realNamesSpread, realNamesSpread));
        }

        // ---- prizes ---------------------------------------------------------------------

        [Tooltip("What each place wins, best first: element 0 is 1st place. Uses the same Reward " +
                 "as the daily streak, so a gift granted here is granted the same way.")]
        public Prize[] prizes;

        /// <summary>What <paramref name="place"/> (1-based) wins, or null for a place that pays
        /// nothing. Shorter than <see cref="prizePlaces"/> is fine — it just means fewer winners.</summary>
        public Prize PrizeFor(int place) =>
            prizes != null && place >= 1 && place <= prizes.Length ? prizes[place - 1] : null;

        [Tooltip("Sprite shown for each boost type in the prize UI — swap here to change dynamically.")]
        public BoostSprite[] boostSprites;

        /// <summary>The configured sprite for a boost type, or null if none is set.</summary>
        public Sprite SpriteFor(EBoostType type) {
            if (boostSprites != null)
                foreach (var b in boostSprites)
                    if (b.type == type) return b.sprite;
            return null;
        }

        /// <summary>One place's winnings. A class and not a bare array because Unity will not
        /// serialize an array of arrays, and a place can pay more than one thing.</summary>
        [Serializable]
        public class Prize {
            public Reward[] rewards;
        }

        [Serializable]
        public class BoostSprite {
            public EBoostType type;
            public Sprite sprite;
        }

        // ---- singleton ------------------------------------------------------------------

        static TournamentConfig _instance;

        /// <summary>The loaded asset, or null if it isn't shipped. Readable from any thread.</summary>
        public static TournamentConfig Instance {
            get {
#if UNITY_EDITOR
                if (_instance == null && !Application.isPlaying) _load();   // editor tools, no boot ran
#endif
                return _instance;
            }
        }

        /// <summary>Loaded here, on the main thread, before anything can ask for it.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _load() {
            _instance = Resources.Load<TournamentConfig>(Resource);
            if (_instance == null)
                CDebug.LogError($"[TournamentConfig] Resources/{Resource}.asset not found - tournament is off.");
        }
    }
}
