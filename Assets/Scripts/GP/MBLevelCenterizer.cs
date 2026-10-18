using UnityEngine;

namespace qp {

    // "Level 1" and "Level 10000" are not the same width — $LevelText is left-aligned inside a
    // fixed box, so the pair drifts off-center as digits are added. We shift the whole Level
    // container on X by a hand-tuned amount per digit count. MBTopBar calls Recenter() right
    // after it writes the text; nothing runs per-frame.
    public class MBLevelCenterizer : MonoBehaviour {

        [SerializeField] float _x1Digit;
        [SerializeField] float _x2Digits;
        [SerializeField] float _x3Digits;
        [SerializeField] float _x4Digits;
        [SerializeField] float _x5Digits;

        // Optional child that must stay put while the container shifts (the Hard icon under the
        // text) — moved back by the same amount, so it never leaves the middle.
        [SerializeField] RectTransform _keepCentered;

        RectTransform _rt;
        float _authoredX;   // kept for text that isn't a plain number (the daily date)
        float _keepCenteredX;

        // Lazy, not Awake: the Regular/Hard containers start inactive in the scene, and Recenter
        // can arrive before this object has ever been awake.
        private void _init() {
            if (_rt != null) return;
            _rt = (RectTransform)transform;
            _authoredX = _rt.anchoredPosition.x;
            if (_keepCentered != null) _keepCenteredX = _keepCentered.anchoredPosition.x;
        }

        // Anything outside 1-5 digits stays at the position authored in the scene.
        public void Recenter(string text) {
            _init();
            float x;
            switch (_digitCount(text)) {
                case 1: x = _x1Digit; break;
                case 2: x = _x2Digits; break;
                case 3: x = _x3Digits; break;
                case 4: x = _x4Digits; break;
                case 5: x = _x5Digits; break;
                default: x = _authoredX; break;
            }
            _rt.anchoredPosition = new Vector2(x, _rt.anchoredPosition.y);

            if (_keepCentered != null) {
                float back = _keepCenteredX - (x - _authoredX);
                _keepCentered.anchoredPosition = new Vector2(back, _keepCentered.anchoredPosition.y);
            }
        }

        private static int _digitCount(string text) {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (var c in text)
                if (!char.IsDigit(c)) return 0;
            return text.Length;
        }
    }
}
