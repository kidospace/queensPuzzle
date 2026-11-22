using TMPro;
using UnityEngine;

namespace qp {

    // Lives on the example row only. Pick a preset, a color, a place and whether it is the player's
    // own row, and the row shows what MBTournamentRow.Set would turn on, so a style can be checked
    // on every look. Both go on both name labels, only one of which is ever visible.
    public sealed class MBRowStatePreview : MonoBehaviour {

        public enum Place { First, Second, Third, Other }

        // The labels' own colors in TournamentRow, shown when no color is picked.
        static readonly Color NameColor = new Color(0.32941177f, 0.22745098f, 0.6117647f);
        static readonly Color NameMeColor = Color.white;

        public Material preset;
        public TMP_ColorGradient color;
        public Place place = Place.Other;
        public bool isMe;

#if UNITY_EDITOR
        void OnValidate() {
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return;
            // SetActive is not allowed during OnValidate.
            UnityEditor.EditorApplication.delayCall += _Apply;
        }

        void _Apply() {
            if (this == null) return;

            int rank = (int)place + 1;
            bool podium = !isMe && place != Place.Other;

            _Show("$BG", !isMe && !podium);
            _Show("$BGSelected", isMe);
            _Show("$Name", !isMe);
            _Show("$NameSelected", isMe);
            _Show("$Place", place == Place.Other);

            for (int i = 1; i <= 3; i++) {
                _Show("$BGPlace" + i, !isMe && rank == i);
                _Show("$Medal" + i, rank == i);
                _Show("$Prize" + i, rank == i);
            }

            _Style("$Name", NameColor);
            _Style("$NameSelected", NameMeColor);
        }

        Transform _Find(string childName) {
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child;
            return null;
        }

        void _Show(string childName, bool on) {
            var child = _Find(childName);
            if (child != null && child.gameObject.activeSelf != on) child.gameObject.SetActive(on);
        }

        void _Style(string childName, Color ownColor) {
            var child = _Find(childName);
            var label = child != null ? child.GetComponent<TMP_Text>() : null;
            if (label == null) return;

            label.fontSharedMaterial = preset != null ? preset : label.font.material;
            label.enableVertexGradient = color != null;
            label.colorGradientPreset = color;
            // The gradient is multiplied by this, so it has to stay white under one.
            label.color = color != null ? Color.white : ownColor;
            UnityEditor.EditorUtility.SetDirty(label);
        }
#endif
    }
}
