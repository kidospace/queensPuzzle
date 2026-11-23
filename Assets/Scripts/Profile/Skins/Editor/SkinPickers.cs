using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace qp {

    // Slots and items are ids, and an id typed by hand is a typo waiting to happen. These draw
    // them as lists read from the catalog, so the Inspector can only hold ids that exist.
    static class SkinPicker {

        /// <summary>The slots that hold something to earn — the only ones a gift can give from.</summary>
        public static List<string> Slots() {
            var list = new List<string>();
            var catalog = SkinCatalog.Instance;
            if (catalog == null || catalog.slots == null) return list;
            foreach (var slot in catalog.slots) {
                if (slot == null || slot.items == null) continue;
                foreach (var item in slot.items) if (item != null && !item.free) { list.Add(slot.id); break; }
            }
            return list;
        }

        /// <summary>The items of a slot that have to be earned.</summary>
        public static List<string> Items(string slotId) {
            var list = new List<string>();
            var slot = SkinCatalog.Instance != null ? SkinCatalog.Instance.Slot(slotId) : null;
            if (slot == null || slot.items == null) return list;
            foreach (var item in slot.items) if (item != null && !item.free) list.Add(item.id);
            return list;
        }

        /// <summary>A text property drawn as a list. A value that is not in the list is kept and
        /// shown as it is, marked, rather than silently replaced.</summary>
        public static void Popup(Rect rect, string label, SerializedProperty property, List<string> options, string first = null) {
            var shown = new List<string>();
            if (first != null) shown.Add(first);
            shown.AddRange(options);

            string value = property.stringValue ?? "";
            int index = string.IsNullOrEmpty(value) && first != null ? 0 : shown.IndexOf(value);
            if (index < 0) { shown.Add(value + "  (not in the catalog)"); index = shown.Count - 1; }

            int picked = EditorGUI.Popup(rect, label, index, shown.ToArray());
            if (picked == index) return;
            if (first != null && picked == 0) property.stringValue = "";
            else if (picked < (first != null ? 1 : 0) + options.Count) property.stringValue = options[picked - (first != null ? 1 : 0)];
        }
    }

    // A skin in a gift's config: just the slot a new item comes from.
    [CustomPropertyDrawer(typeof(SkinReward))]
    sealed class SkinRewardDrawer : PropertyDrawer {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            SkinPicker.Popup(position, "New item of", property.FindPropertyRelative("slot"), SkinPicker.Slots());
        }
    }

    // A skin in the gift popup's editor preview: the slot, and which item of it.
    [CustomPropertyDrawer(typeof(MBGiftPopup.DebugSkin))]
    sealed class DebugSkinDrawer : PropertyDrawer {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var slot = property.FindPropertyRelative("slot");
            var item = property.FindPropertyRelative("item");
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            string before = slot.stringValue;
            SkinPicker.Popup(line, "Slot", slot, SkinPicker.Slots());
            if (slot.stringValue != before) item.stringValue = "";       // another slot: its items are different

            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            SkinPicker.Popup(line, "Item", item, SkinPicker.Items(slot.stringValue), "(the first one)");
        }
    }
}
