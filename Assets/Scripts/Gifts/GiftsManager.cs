using System.Collections.Generic;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Hands rewards to the player and shows them. Features decide what they give (their own
    /// reward lists); this is the one place that applies it and the one place that puts it in the
    /// gift popup. It knows rewards only as <see cref="IReward"/>: each gives itself and fills its
    /// own card, so a new kind of reward changes nothing here.
    /// </summary>
    public static class GiftsManager {

        /// <summary>Apply one reward to the player and hand it back as it was given (see
        /// <see cref="IReward.Grant"/>); null in, or nothing to give, is null out.</summary>
        public static IReward Grant(IReward reward) => reward?.Grant();

        /// <summary>Apply every reward in the list and return what was actually given — the list
        /// to show in the gift popup. A null list or entry is ignored.</summary>
        public static List<IReward> Grant(IEnumerable<IReward> rewards) {
            var given = new List<IReward>();
            if (rewards == null) return given;
            foreach (var reward in rewards) {
                var got = Grant(reward);
                if (got != null) given.Add(got);
            }
            return given;
        }

        /// <summary>Open the gift popup on this box and play the reveal down to these rewards.
        /// Only shows — granting stays with the feature. Wait on popup.IsShowing.
        /// <paramref name="from"/>: the feature screen's own box (a Gifts/Boxes instance) — the
        /// popup's box starts right over it and flies in; hide it on the same frame.</summary>
        public static void ShowGift(MBGiftPopup popup, EGiftBox box, IEnumerable<IReward> rewards, RectTransform from = null) {
            if (!Prepare(popup, box, rewards)) return;
            popup.Show(from);
        }

        /// <summary>Put the box art and one filled card per reward into the popup without opening
        /// it (ShowGift, and the popup's editor debug). False when the config is missing.</summary>
        public static bool Prepare(MBGiftPopup popup, EGiftBox box, IEnumerable<IReward> rewards) {
            var cfg = GiftsManagerConfig.Instance;
            if (popup == null || cfg == null) {
                CDebug.LogError($"[GiftsManager] can't prepare the gift: popup {(popup != null ? "ok" : "missing")}, config {(cfg != null ? "ok" : "missing")}");
                return false;
            }
            popup.SetBox(cfg.BoxFor(box));
            popup.ClearRewards();
            if (rewards == null) return true;

            foreach (var reward in rewards) {
                if (reward == null) continue;
                var card = reward.Card;
                if (card == null) { CDebug.LogError($"[GiftsManager] no card for {reward.GetType().Name} - set it in GiftsManagerConfig"); continue; }
                var shown = popup.AddReward(card, 0);
                reward.Dress(shown);
                if (reward.IsUsable) popup.OfferUse(shown, reward.Use);
            }
            return true;
        }
    }
}
