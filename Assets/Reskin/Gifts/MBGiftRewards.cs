using System.Collections.Generic;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Lays out whatever reward objects the caller adds — the popup knows nothing about what a
    /// reward looks like. Add() instantiates a prefab into a row and returns the instance, so the
    /// caller sets its icon / number / badge afterwards. Each row holds up to 3 and is centered;
    /// a longer row wraps. The reveal clip animates only Burst (0 → 1 over the flight); each card
    /// is thrown out of the gift by it on its own: full speed at once then slowing down, a small
    /// upward arc, its own slight delay and its own decaying spin — so they never move in lockstep.
    /// </summary>
    [ExecuteAlways]
    public class MBGiftRewards : MonoBehaviour {

        public float Spacing = 250f;     // between card centers in a row
        public float RowGap = 250f;      // between rows
        public float FirstRowY = 247f;   // first row's height above the gift's center
        public int PerRow = 3;

        public float Burst = 1f;         // animated: 0 = inside the gift, 1 = every card at rest
        public float FlyTime = 1.1f;     // seconds of flight Burst 0 → 1 stands for
        public float Snap = 7f;          // how fast a card loses its speed (higher = harder throw)
        public float Lift = 60f;         // height of the upward arc
        public float Spin = 16f;         // starting spin, degrees (mirrored left / right)
        public float Stagger = 0.06f;    // max random delay between cards, seconds
        public int Seed = 7;

        // row of each added card, serialized so it survives a domain reload in the editor
        [SerializeField, HideInInspector] List<Transform> _rowCards = new List<Transform>();
        [SerializeField, HideInInspector] List<int> _rows = new List<int>();

        readonly List<Transform> _cards = new List<Transform>();
        readonly List<Vector2> _place = new List<Vector2>();
        readonly List<float> _delay = new List<float>();
        readonly List<float> _spin = new List<float>();

        /// <summary>Instantiate a reward into the given row and return it for the caller to fill.</summary>
        public GameObject Add(GameObject prefab, int row = 0) {
            var go = Instantiate(prefab, transform, false);
            go.name = prefab.name;
            _rowCards.Add(go.transform);
            _rows.Add(row);
            Layout();
            return go;
        }

        public void Clear() {
            for (int i = transform.childCount - 1; i >= 0; i--) {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            _rowCards.Clear();
            _rows.Clear();
            _cards.Clear();
        }

        /// <summary>Re-place every card for the current Burst (editor preview calls this after sampling).</summary>
        public void Refresh() {
            _apply();
        }

        /// <summary>Work out every child's place (rows in order, up to PerRow each, each line
        /// centered) and its own throw: delay and spin.</summary>
        public void Layout() {
            _cards.Clear(); _place.Clear(); _delay.Clear(); _spin.Clear();
            var rows = new SortedDictionary<int, List<Transform>>();
            foreach (Transform c in transform) {
                if (!c.gameObject.activeSelf) continue;
                int k = _rowCards.IndexOf(c);
                int r = k >= 0 ? _rows[k] : 0;    // objects not added through Add() go to row 0
                if (!rows.TryGetValue(r, out var list)) rows[r] = list = new List<Transform>();
                list.Add(c);
            }
            var old = Random.state;
            Random.InitState(Seed);
            int line = 0;
            foreach (var list in rows.Values) {
                for (int i = 0; i < list.Count; i++) {
                    int lineStart = i / PerRow * PerRow;
                    int inLine = Mathf.Min(PerRow, list.Count - lineStart);
                    float x = (i - lineStart - (inLine - 1) / 2f) * Spacing;
                    float side = Mathf.Abs(x) < 1f ? (Random.value < 0.5f ? -0.4f : 0.4f) : -Mathf.Sign(x);
                    _cards.Add(list[i]);
                    _place.Add(new Vector2(x, FirstRowY - (line + i / PerRow) * RowGap));
                    _delay.Add(Random.Range(0f, Stagger));
                    _spin.Add(Spin * side * Random.Range(0.7f, 1.2f));
                }
                line += (list.Count + PerRow - 1) / PerRow;
            }
            Random.state = old;
            _apply();
        }

        // called whenever an animation (Animator or the Animation window preview) writes our fields
        void OnDidApplyAnimationProperties() {
            _apply();
        }

        void LateUpdate() {
            _apply();
        }

        void _apply() {
            if (_cards.Count != _activeChildren()) { Layout(); return; }
            float s = Burst * FlyTime;
            for (int i = 0; i < _cards.Count; i++) {
                var rt = (RectTransform)_cards[i];
                if (rt == null) continue;
                if (Burst >= 1f) { _rest(rt, _place[i]); continue; }
                float t = s - _delay[i];
                if (t <= 0f) { rt.localScale = Vector3.zero; continue; }
                float go = 1f - Mathf.Exp(-Snap * t);                                  // thrown: full speed, then slowing
                float arc = (Mathf.Exp(-4f * t) - Mathf.Exp(-12f * t)) / 0.385f;     // 0 → 1 → 0 hump, peak at ~0.14s
                rt.anchoredPosition = _place[i] * go + new Vector2(0f, Lift * arc);
                rt.localScale = Vector3.one * (0.3f + 0.7f * (1f - Mathf.Exp(-Snap * 1.2f * t)));
                rt.localRotation = Quaternion.Euler(0f, 0f, _spin[i] * Mathf.Exp(-3.5f * t) * Mathf.Cos(4f * t));
            }
        }

        static void _rest(RectTransform rt, Vector2 place) {
            rt.anchoredPosition = place;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        int _activeChildren() {
            int n = 0;
            foreach (Transform c in transform) if (c.gameObject.activeSelf) n++;
            return n;
        }
    }
}
