using System;
using UnityEngine;

namespace qp {

    /// <summary>
    /// A boost reward — plain data configured inline in a feature's config (DailyStreakConfig,
    /// TournamentConfig); each feature keeps its own list. Granting goes through
    /// <see cref="GiftsManager"/>; the sprite comes from <see cref="GiftsManagerConfig"/>.
    /// </summary>
    [Serializable]
    public class BoostReward {

        public EBoostType type;
        [Min(1)] public int amount = 1;

        /// <summary>Short amount label for the reward UI, e.g. "+3".</summary>
        public string Label => $"+{amount}";

        /// <summary>This boost's sprite for the reward UI, or null if none is configured.</summary>
        public Sprite Sprite => GiftsManagerConfig.Instance?.SpriteFor(type);
    }
}
