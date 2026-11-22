using System;
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
    }

    /// <summary>One place on the player where an item is worn — avatar, frame, name, badge — and
    /// everything that can go in it, in the order it shows.</summary>
    [Serializable]
    public class SkinSlot {
        [Tooltip("The id that is saved and sent. Never rename one that has shipped.")]
        public string id;
        [Tooltip("What a player who wears nothing here (or something this build does not know) " +
                 "gets. Empty = nothing is drawn.")]
        public string defaultItem = "";
        public SkinItem[] items = new SkinItem[0];

        public SkinItem Find(string item) {
            if (string.IsNullOrEmpty(item) || items == null) return null;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].id == item) return items[i];
            return null;
        }
    }

    /// <summary>
    /// Everything a player can wear, shipped as a single asset in Resources ("SkinCatalog").
    /// Players and the server only ever hold <see cref="SkinRef"/>s — which slot, which item — and
    /// this is where a pair of ids turns into something to draw.
    ///
    /// Asking never fails. A slot this build does not know gives null; an item it does not know
    /// gives the slot's default. A newer server, an older client, a profile saved by a build that
    /// had more items: all of them just fall back.
    ///
    /// Singleton, loaded once at boot — same reasons as <see cref="ProfileConfig"/>.
    /// </summary>
    public class SkinCatalog : ScriptableObject {

        public const string Resource = "SkinCatalog";

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

        /// <summary>The item to draw: the one asked for, else the slot's default, else null
        /// (draw nothing).</summary>
        public SkinItem Resolve(string slot, string item) {
            var found = Slot(slot);
            if (found == null) return null;
            return found.Find(item) ?? found.Find(found.defaultItem);
        }

        // ---- what the UI asks ----------------------------------------------------------------

        /// <summary>What to draw in a slot, as the type the caller expects — e.g.
        /// <c>Asset&lt;Sprite&gt;(Frame, id)</c>, <c>Asset&lt;SkinNameStyle&gt;(Name, id)</c>.
        /// Null = draw nothing / keep the default look; also when the asset is of another type.</summary>
        public static T Asset<T>(string slot, string item) where T : UnityEngine.Object =>
            Instance != null ? Instance.Resolve(slot, item)?.asset as T : null;

        // ---- singleton -----------------------------------------------------------------------

        static SkinCatalog _instance;

        /// <summary>The loaded asset, or null if it isn't shipped.</summary>
        public static SkinCatalog Instance {
            get {
#if UNITY_EDITOR
                if (_instance == null && !Application.isPlaying) _load();   // editor tools, no boot ran
#endif
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _load() {
            _instance = Resources.Load<SkinCatalog>(Resource);
            if (_instance == null)
                CDebug.LogError($"[SkinCatalog] Resources/{Resource}.asset not found - skins are off.");
        }
    }
}
