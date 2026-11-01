using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Ring of small sparkles that bursts out of the gift's center as the light fades: each child
    /// Image flies outward on its own angle, slows down, twinkles and fades. The reveal clip
    /// animates only Burst (0 → 1 over LifeTime); at 0 or 1 every sparkle is hidden. The sparkles
    /// are the object's children (baked in the prefab); each gets its own seeded path.
    /// </summary>
    [ExecuteAlways]
    public class MBSparkleRing : MonoBehaviour {

        public float Burst;                // animated: 0 = not started, 1 = all gone
        public float LifeTime = 1.3f;      // seconds Burst 0 → 1 stands for
        public float StartRadius = 60f;
        public float EndRadiusMin = 280f;
        public float EndRadiusMax = 420f;
        public float SizeMin = 22f;
        public float SizeMax = 60f;
        public int Seed = 11;

        Image[] _img;
        float[] _ang, _end, _size, _delay, _twSpeed, _twPhase, _life;

        public void Init() {
            if (_img != null && _img.Length == transform.childCount) return;
            int n = transform.childCount;
            _img = new Image[n];
            _ang = new float[n]; _end = new float[n]; _size = new float[n]; _delay = new float[n];
            _twSpeed = new float[n]; _twPhase = new float[n]; _life = new float[n];
            var old = Random.state;
            Random.InitState(Seed);
            for (int i = 0; i < n; i++) {
                _img[i] = transform.GetChild(i).GetComponent<Image>();
                _ang[i] = (i + Random.Range(-0.4f, 0.4f)) * Mathf.PI * 2f / n;
                _end[i] = Random.Range(EndRadiusMin, EndRadiusMax);
                _size[i] = Random.value < 0.35f ? Random.Range(SizeMin * 0.5f, SizeMin) : Random.Range(SizeMin, SizeMax);   // some dots, some stars
                _delay[i] = Random.Range(0f, 0.15f);
                _twSpeed[i] = Random.Range(8f, 16f);
                _twPhase[i] = Random.Range(0f, Mathf.PI * 2f);
                _life[i] = Random.Range(0.75f, 1f);
            }
            Random.state = old;
        }

        /// <summary>Drop the cached paths and rebuild from the current inspector values.</summary>
        public void Rebuild() {
            _img = null;
            Init();
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
            Init();
            float s = Burst * LifeTime;
            for (int i = 0; i < _img.Length; i++) {
                var img = _img[i];
                if (img == null) continue;
                float life = LifeTime * _life[i] - _delay[i];
                float p = (s - _delay[i]) / life;   // this sparkle's own 0..1
                if (Burst <= 0f || Burst >= 1f || p <= 0f || p >= 1f) { _alpha(img, 0f); continue; }
                float t = s - _delay[i];
                float r = Mathf.Lerp(StartRadius, _end[i], 1f - Mathf.Exp(-4f * t));   // shoots out, slows down
                var rt = img.rectTransform;
                rt.anchoredPosition = new Vector2(Mathf.Cos(_ang[i]), Mathf.Sin(_ang[i])) * r;
                float tw = 0.65f + 0.35f * Mathf.Sin(t * _twSpeed[i] + _twPhase[i]);      // twinkle
                rt.sizeDelta = Vector2.one * _size[i] * tw;
                _alpha(img, _smooth01(0f, 0.08f, p) * (1f - _smooth01(0.45f, 1f, p)) * tw);
            }
        }

        static void _alpha(Image img, float a) {
            var c = img.color;
            c.a = a;
            img.color = c;
        }

        // edge-style smoothstep (Mathf.SmoothStep interpolates, which is not what fades need)
        static float _smooth01(float e0, float e1, float x) {
            float u = Mathf.Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }
    }
}
