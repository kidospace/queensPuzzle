using System;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// So many of one boost — plain data configured inline in a feature's config
    /// (DailyStreakConfig, TournamentConfig). Its card shows the boost's picture and "+N".
    /// </summary>
    [Serializable]
    public class BoostReward : IReward {

        public EBoostType type;
        [Min(1)] public int amount = 1;

        /// <summary>Short amount label for the reward UI, e.g. "+3".</summary>
        public string Label => $"+{amount}";

        /// <summary>This boost's sprite for the reward UI, or null if none is configured.</summary>
        public Sprite Sprite => GiftsManagerConfig.Instance?.SpriteFor(type);

        public IReward Grant() {
            AppData.Boosts[type].Value += amount;
            return this;
        }

        public GameObject Card => GiftsManagerConfig.Instance?.boostCard;

        // $Icon gets the boost's sprite, $Amount the "+N" label
        public void Dress(GameObject card) {
            var icon = card.transform.RecursiveFindChild("$Icon")?.GetComponent<Image>();
            if (icon != null && Sprite != null) icon.sprite = Sprite;
            var label = card.transform.RecursiveFindChild("$Amount")?.GetComponent<TMP_Text>();
            if (label != null) label.text = Label;
        }

        public bool IsUsable => false;
        public void Use() { }
    }
}
