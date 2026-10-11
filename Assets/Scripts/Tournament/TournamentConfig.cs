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
