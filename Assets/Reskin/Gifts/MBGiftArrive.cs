using UnityEngine;

namespace qp {

    /// <summary>
    /// The gift box flying in from the feature screen it was sitting on. Begin(from) puts the
    /// popup's own box exactly over that box — same place, same size; it is the same Box prefab —
    /// so the feature can hide its box on the same frame and the eye sees one gift. The "Arrive"
    /// clip then animates <see cref="Arrive"/> 0 → 1 and the box flies to its place on a soft arc,
    /// leaning into the way it travels, and comes DOWN onto its place still moving — the reveal's
    /// first squash is that landing, so flight, impact, rattle and burst read as one motion.
    /// Sits on $Box; at Arrive 1 (the prefab value, and every other state) it does nothing.
    /// </summary>
    public class MBGiftArrive : MonoBehaviour {

        public float Arrive = 1f;        // animated: 0 = over the feature's box, 1 = at rest
        public float ArcHeight = 140f;   // how far the path bows up above the straight line
        public float Lean = 8f;          // degrees it tilts into the flight, back to 0 on landing
        public float Impact = 1.5f;      // speed it still has as it lands (0 = eases to a stop)

        Vector3 _restPos, _restScale, _fromPos, _fromScale;
        Quaternion _restRot;
        bool _hasRest, _flying;

        /// <summary>Start the box over <paramref name="from"/> (another instance of the same Box
        /// prefab); <paramref name="art"/> is this popup's box art root, at rest.</summary>
        public void Begin(RectTransform from, Transform art) {
            if (!_hasRest) {
                _restPos = transform.localPosition; _restScale = transform.localScale; _restRot = transform.localRotation;
                _hasRest = true;
            }
            transform.localPosition = _restPos;
            transform.localScale = _restScale;
            transform.localRotation = _restRot;

            var parent = transform.parent;
            float k = from.lossyScale.x / art.lossyScale.x;                              // their size / ours
            Vector3 d = parent.InverseTransformPoint(art.position) - _restPos;          // art offset at rest
            _fromPos = parent.InverseTransformPoint(from.position) - d * k;
            _fromPos.z = _restPos.z;
            _fromScale = _restScale * k;

            Arrive = 0f;
            _flying = true;
            _apply();
        }

        void OnDidApplyAnimationProperties() {
            _apply();
        }

        void LateUpdate() {
            _apply();
        }

        void _apply() {
            if (!_flying) return;
            if (Arrive >= 1f) {   // landed: exactly at rest once, then hands off to the reveal
                transform.localPosition = _restPos;
                transform.localScale = _restScale;
                transform.localRotation = _restRot;
                _flying = false;
                return;
            }
            float a = Mathf.Clamp01(Arrive);
            // soft start, but it lands still moving (Hermite: start speed 0, end speed Impact)
            float t = a * a * (3f - 2f * a) + Impact * (a * a * a - a * a);

            // a quadratic arc: the control point sits above the middle of the straight line
            var mid = (_fromPos + _restPos) * 0.5f + Vector3.up * ArcHeight;
            transform.localPosition = (1f - t) * (1f - t) * _fromPos + 2f * (1f - t) * t * mid + t * t * _restPos;

            // lean into the travel (sideways), straight again on landing
            // into the sideways travel; a straight up/down flight still gets half a lean
            float side = Mathf.Clamp((_restPos.x - _fromPos.x) / 200f, -1f, 1f);
            if (Mathf.Abs(side) < 0.5f) side = side < 0f ? -0.5f : 0.5f;
            transform.localRotation = _restRot * Quaternion.Euler(0f, 0f, -side * Lean * Mathf.Sin(Mathf.PI * t));

            // grows into its size early, so the landing squash is the only thing on impact
            float g = 1f - Mathf.Pow(1f - a, 3f);   // ease-out
            transform.localScale = Vector3.Lerp(_fromScale, _restScale, g);
        }
    }
}
