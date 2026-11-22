using System;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>One thing a player can equip: an id and the asset it stands for. The item does
    /// not care what kind of asset that is — a Sprite for an avatar, frame or badge, a
    /// <see cref="ProfileNameStyle"/> for a name skin, whatever a future slot needs. The slot it
    /// sits in is what knows, and whoever draws the slot asks for the type it expects.</summary>
    [Serializable]
    public class ProfileItem {
        [Tooltip("The id that is saved and sent. Never rename one that has shipped.")]
        public string key;
        [Tooltip("What the id stands for: a Sprite, a ProfileNameStyle, …")]
        public UnityEngine.Object asset;
    }

    /// <summary>One kind of customization — avatar, frame, name skin, badge — and everything
    /// that can go in it, in the order they show.</summary>
    [Serializable]
    public class ProfileSlot {
        [Tooltip("The id that is saved and sent. Never rename one that has shipped.")]
        public string key;
        [Tooltip("What a player who equipped nothing (or something this build does not know) " +
                 "gets. Empty = nothing is drawn.")]
        public string defaultItem = "";
        public ProfileItem[] items = new ProfileItem[0];

        public ProfileItem Find(string itemKey) {
            if (string.IsNullOrEmpty(itemKey) || items == null) return null;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].key == itemKey) return items[i];
            return null;
        }
    }

    /// <summary>
    /// Everything a profile can be dressed in, shipped as a single asset in Resources
    /// ("ProfileCatalog"). Players and the server only ever hold a pair of ids — which slot, which
    /// item — and this is where a pair turns into something to draw.
    ///
    /// Asking never fails. A slot this build does not know gives null; an item it does not know
    /// gives the slot's default. A newer server, an older client, a profile saved by a build that
    /// had more items: all of them just fall back.
    ///
    /// Singleton, loaded once at boot — same reasons as <see cref="ProfileConfig"/>.
    /// </summary>
    public class ProfileCatalog : ScriptableObject {

        public const string Resource = "ProfileCatalog";

        public const string Avatar = "avatar";
        public const string Frame = "frame";
        public const string NameStyle = "nameStyle";
        public const string Badge = "badge";

        public ProfileSlot[] slots = new ProfileSlot[0];

        /// <summary>The slot, or null if this build has no such slot.</summary>
        public ProfileSlot Slot(string slotKey) {
            if (string.IsNullOrEmpty(slotKey) || slots == null) return null;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].key == slotKey) return slots[i];
            return null;
        }

        /// <summary>The item exactly as asked, or null — for "does this exist", not for drawing.</summary>
        public ProfileItem Find(string slotKey, string itemKey) => Slot(slotKey)?.Find(itemKey);

        /// <summary>The item to draw: the one asked for, else the slot's default, else null
        /// (draw nothing).</summary>
        public ProfileItem Resolve(string slotKey, string itemKey) {
            var slot = Slot(slotKey);
            if (slot == null) return null;
            return slot.Find(itemKey) ?? slot.Find(slot.defaultItem);
        }

        // ---- what the UI asks ----------------------------------------------------------------

        /// <summary>What to draw for a slot, as the type the caller expects — e.g.
        /// <c>Asset&lt;Sprite&gt;(Frame, id)</c>, <c>Asset&lt;ProfileNameStyle&gt;(NameStyle, id)</c>.
        /// Null = draw nothing / keep the default look; also when the asset is of another type.</summary>
        public static T Asset<T>(string slotKey, string itemKey) where T : UnityEngine.Object =>
            Instance != null ? Instance.Resolve(slotKey, itemKey)?.asset as T : null;

        // ---- singleton -----------------------------------------------------------------------

        static ProfileCatalog _instance;

        /// <summary>The loaded asset, or null if it isn't shipped.</summary>
        public static ProfileCatalog Instance {
            get {
#if UNITY_EDITOR
                if (_instance == null && !Application.isPlaying) _load();   // editor tools, no boot ran
#endif
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void _load() {
            _instance = Resources.Load<ProfileCatalog>(Resource);
            if (_instance == null)
                CDebug.LogError($"[ProfileCatalog] Resources/{Resource}.asset not found - customization is off.");
        }
    }
}
