using System;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Shared settings for rewards, shipped as a single asset in Resources ("GiftsManagerConfig"):
    /// how each boost looks in any reward UI, which box each gift opens and the card a boost shows
    /// on in the gift popup. What a feature gives stays in that feature's config.
    ///
    /// Singleton, loaded once at boot like <see cref="TournamentConfig"/>, so reads are safe from
    /// any thread. <see cref="Instance"/> is null when the asset is missing.
    /// </summary>
    [CreateAssetMenu(menuName = "QP/Gifts Manager Config", fileName = "GiftsManagerConfig")]
    public class GiftsManagerConfig : ScriptableObject {

        const string Resource = "GiftsManagerConfig";

        [Tooltip("Sprite shown for each boost type in the reward UI — swap here to change it everywhere.")]
        public BoostSprite[] boostSprites;

        /// <summary>The configured sprite for a boost type, or null if none is set.</summary>
        public Sprite SpriteFor(EBoostType type) {
            if (boostSprites != null)
                foreach (var b in boostSprites)
                    if (b.type == type) return b.sprite;
            return null;
        }

        [Tooltip("Box prefab each gift opens in the gift popup (Gifts/Boxes).")]
        public GiftBox[] boxes;

        [Tooltip("Card a boost reward shows on in the gift popup; its $Icon and $Amount are filled from the reward.")]
        public GameObject boostCard;

        /// <summary>The box prefab configured for a gift, or null if none is set.</summary>
        public GameObject BoxFor(EGiftBox type) {
            if (boxes != null)
                foreach (var b in boxes)
                    if (b.type == type) return b.prefab;
            return null;
        }

        [Serializable]
        public class BoostSprite {
            public EBoostType type;
            public Sprite sprite;
        }

        [Serializable]
        public class GiftBox {
            public EGiftBox type;
            public GameObject prefab;
        }

        // ---- singleton ------------------------------------------------------------------

        static GiftsManagerConfig _instance;

        /// <summary>The loaded asset, or null if it isn't shipped. Readable from any thread.</summary>
        public static GiftsManagerConfig Instance {
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
            _instance = Resources.Load<GiftsManagerConfig>(Resource);
            if (_instance == null)
                CDebug.LogError($"[GiftsManagerConfig] Resources/{Resource}.asset not found - reward sprites are off.");
        }
    }
}
