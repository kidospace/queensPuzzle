using TMPro;
using UnityEngine;

namespace qp {

    /// <summary>
    /// "Loading" with dots that count up 0 → 3 and start over. The dots not shown yet are still
    /// written, just invisible, so the word keeps its width and never jumps while centred.
    /// Runs on unscaled time, from the moment its object turns on.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class MBLoadingDots : MonoBehaviour {

        public string Word = "Loading";
        public float Step = 0.35f;   // seconds per dot

        TMP_Text _text;
        int _shown = -1;
        float _t;

        void Awake() => _text = GetComponent<TMP_Text>();

        void OnEnable() {
            _t = 0f;
            _shown = -1;
            _apply(0);
        }

        void Update() {
            _t += Time.unscaledDeltaTime;
            _apply((int)(_t / Step) % 4);
        }

        void _apply(int dots) {
            if (dots == _shown) return;
            _shown = dots;
            _text.text = Word + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots);
        }
    }
}
