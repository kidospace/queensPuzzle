using System.Collections.Generic;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Hands rewards to the player and shows them. Features decide what they give (their own
    /// reward lists); this is the one place that applies it, and the one place that turns game
    /// data (EGiftBox, BoostReward) into what the gift popup shows — the popup only knows prefabs.
    /// </summary>
    public static class GiftsManager {

        /// <summary>Apply one reward to the player; null is ignored.</summary>
        public static void Grant(BoostReward reward) {
            if (reward == null) return;
            AppData.Boosts[reward.type].Value += reward.amount;
        }

        /// <summary>Apply every reward in the list; a null list or entry is ignored.</summary>
        public static void Grant(IEnumerable<BoostReward> rewards) {
            if (rewards == null) return;
            foreach (var reward in rewards) Grant(reward);
        }

        /// <summary>Open the gift popup on this box and play the reveal down to these rewards.
        /// Only shows — granting stays with the feature. Wait on popup.IsShowing.
        /// <paramref name="from"/>: the feature screen's own box (a Gifts/Boxes instance) — the
        /// popup's box starts right over it and flies in; hide it on the same frame.</summary>
        public static void ShowGift(MBGiftPopup popup, EGiftBox box, IEnumerable<BoostReward> rewards, RectTransform from = null) {
            if (!Prepare(popup, box, rewards)) return;
            popup.Show(from);
        }

        /// <summary>Put the box art and one filled card per reward into the popup without opening
        /// it (ShowGift, and the popup's editor debug). False when the config is missing.</summary>
        public static bool Prepare(MBGiftPopup popup, EGiftBox box, IEnumerable<BoostReward> rewards) {
            var cfg = GiftsManagerConfig.Instance;
            if (popup == null || cfg == null) {
                CDebug.LogError($"[GiftsManager] can't prepare the gift: popup {(popup != null ? "ok" : "missing")}, config {(cfg != null ? "ok" : "missing")}");
                return false;
            }
            popup.SetBox(cfg.BoxFor(box));
            popup.ClearRewards();
            if (rewards != null)
                foreach (var reward in rewards)
                    if (reward != null) _dressBoost(popup.AddReward(cfg.boostCard, 0), reward);
            return true;
        }

        // the boost card's $Icon gets the boost's sprite, its $Amount the "+N" label
        static void _dressBoost(GameObject card, BoostReward reward) {
            var icon = card.transform.RecursiveFindChild("$Icon")?.GetComponent<Image>();
            if (icon != null && reward.Sprite != null) icon.sprite = reward.Sprite;
            var amount = card.transform.RecursiveFindChild("$Amount")?.GetComponent<TMP_Text>();
            if (amount != null) amount.text = reward.Label;
        }
    }
}
