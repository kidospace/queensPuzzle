using System;
using System.Collections.Generic;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>A pointer to one item in the <see cref="SkinCatalog"/>: which slot, and which
    /// item in it — ("frame", "premiumframe2"). This pair is all a save file or the server ever
    /// holds; what it looks like is the catalog's business, so new skins need no server change.</summary>
    [Serializable]
    public class SkinRef {
        public string slot;
        public string item;

        // What someone wears is a plain list of these — the player's saved choice, a draft in
        // the profile popup, a rival's row in a tournament. The two questions asked of such a list:

        /// <summary>The item worn in a slot, or "" when the slot is empty.</summary>
        public static string Worn(List<SkinRef> skins, string slot) {
            if (skins != null)
                for (int i = 0; i < skins.Count; i++)
                    if (skins[i] != null && skins[i].slot == slot) return skins[i].item ?? "";
            return "";
        }

        /// <summary>Put an item in a slot, replacing what was there; "" empties the slot.</summary>
        public static void Wear(List<SkinRef> skins, string slot, string item) {
            skins.RemoveAll(s => s == null || s.slot == slot);
            if (!string.IsNullOrEmpty(item)) skins.Add(new SkinRef { slot = slot, item = item });
        }
    }

    /// <summary>One thing a player can wear: an id and the asset it stands for. The item does not
    /// care what kind of asset that is — a Sprite for an avatar, frame or badge, a
    /// <see cref="SkinNameStyle"/> for a name, whatever a future slot needs. Whoever draws the
    /// slot asks for the type it expects.</summary>
    [Serializable]
    public class SkinItem {
        [Tooltip("The id that is saved and sent. Never rename one that has shipped.")]
        public string id;
        [Tooltip("What the id stands for: a Sprite, a SkinNameStyle, …")]
        public UnityEngine.Object asset;
        [Tooltip("Everyone has it from the start. A new player wears one of these, picked at random.")]
        public bool free;
    }

    /// <summary>One place on the player where an item is worn — avatar, frame, name, badge — and
    /// everything that can go in it, in the order it shows.</summary>
    [Serializable]
    public class SkinSlot {
        [Tooltip("The id that is saved and sent. Never rename one that has shipped.")]
        public string id;
        public SkinItem[] items = new SkinItem[0];

        public SkinItem Find(string item) {
            if (string.IsNullOrEmpty(item) || items == null) return null;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].id == item) return items[i];
            return null;
        }

        /// <summary>The first item everyone has, or null when the slot has none. This is what is
        /// drawn for a player who wears nothing here, or something this build does not know — so
        /// a slot whose items all have to be earned (a badge) draws nothing.</summary>
        public SkinItem FirstFree() {
            if (items == null) return null;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].free) return items[i];
            return null;
        }
    }

    /// <summary>
    /// Everything a player can wear. Part of the profile's config (<see cref="ProfileConfig.skins"/>),
    /// so it ships and loads with it. Players and the server only ever hold <see cref="SkinRef"/>s —
    /// which slot, which item — and this is where a pair of ids turns into something to draw.
    ///
    /// Asking never fails. A slot this build does not know gives null; an item it does not know
    /// gives the slot's first free item. A newer server, an older client, a profile saved by a
    /// build that had more items: all of them just fall back.
    /// </summary>
    [Serializable]
    public class SkinCatalog {

        /// <summary>The catalog in the shipped config, or null if the config is missing.</summary>
        public static SkinCatalog Instance => ProfileConfig.Instance != null ? ProfileConfig.Instance.skins : null;

        // The slot ids.
        public const string Avatar = "avatar";
        public const string Frame = "frame";
        public const string Name = "name";
        public const string Badge = "badge";

        public SkinSlot[] slots = new SkinSlot[0];

        /// <summary>The slot, or null if this build has no such slot.</summary>
        public SkinSlot Slot(string slot) {
            if (string.IsNullOrEmpty(slot) || slots == null) return null;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].id == slot) return slots[i];
            return null;
        }

        /// <summary>The item exactly as asked, or null — for "does this exist", not for drawing.</summary>
        public SkinItem Find(string slot, string item) => Slot(slot)?.Find(item);

        /// <summary>The item to draw: the one asked for, else the slot's first free item, else
        /// null (draw nothing).</summary>
        public SkinItem Resolve(string slot, string item) {
            var found = Slot(slot);
            if (found == null) return null;
            return found.Find(item) ?? found.FirstFree();
        }

        // ---- what the UI asks ----------------------------------------------------------------

        /// <summary>What to draw in a slot, as the type the caller expects — e.g.
        /// <c>Asset&lt;Sprite&gt;(Frame, id)</c>, <c>Asset&lt;SkinNameStyle&gt;(Name, id)</c>.
        /// Null = draw nothing / keep the default look; also when the asset is of another type.</summary>
        public static T Asset<T>(string slot, string item) where T : UnityEngine.Object =>
            Instance != null ? Instance.Resolve(slot, item)?.asset as T : null;
    }
}
