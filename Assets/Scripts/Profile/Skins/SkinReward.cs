using System;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// A new skin: one more item of a slot, picked at random from the ones the player does not
    /// have. Which item it is exists only after the grant, so <see cref="Grant"/> returns a copy
    /// that knows — the config's own entry is shared by every player and is never written to.
    ///
    /// A picture (avatar, frame, badge) and a name skin do not look alike, so they have a card
    /// each: one is a big picture, the other a sample of text in the skin. Neither has an amount.
    /// </summary>
    [Serializable]
    public class SkinReward : IReward {

        [Tooltip("The slot a new item comes from: frame, name, badge or avatar.")]
        public string slot = "";

        /// <summary>The item this reward turned out to be; empty until granted.</summary>
        [NonSerialized] public string item = "";

        /// <summary>What a name skin is shown on in a gift. Short on purpose: a whole name does
        /// not sit well on a card, and two letters show the look just as well.</summary>
        const string NameSample = "Ab";

        public IReward Grant() {
            var given = ProfileManager.GrantRandom(slot);
            return given == null ? null : new SkinReward { slot = given.slot, item = given.item };
        }

        public GameObject Card {
            get {
                var cfg = GiftsManagerConfig.Instance;
                if (cfg == null) return null;
                return slot == SkinCatalog.Name ? cfg.nameSkinCard : cfg.skinCard;
            }
        }

        // A picture goes on $Icon at the size it was drawn; a name skin dresses $Label.
        public void Dress(GameObject card) {
            var found = SkinCatalog.Instance != null ? SkinCatalog.Instance.Find(slot, item) : null;
            if (found == null) return;

            var icon = card.transform.RecursiveFindChild("$Icon")?.GetComponent<Image>();
            if (icon != null && found.asset is Sprite sprite) {
                icon.sprite = sprite;
                icon.SetNativeSize();
            }

            var label = card.transform.RecursiveFindChild("$Label")?.GetComponent<TMP_Text>();
            if (label != null && found.asset is SkinNameStyle style) {
                label.text = NameSample;
                var skin = label.GetComponent<MBSkinName>();
                if (skin != null) skin.Show(style);
            }
        }

        public bool IsUsable => true;

        public void Use() {
            if (!string.IsNullOrEmpty(item)) ProfileManager.Equip(slot, item);
        }
    }
}
