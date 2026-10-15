using System.Collections;
using UnityEngine;

namespace qp {

    /// <summary>
    /// A quick decaying rock around Z — the "can't do that, and here is why" answer to a tap on a
    /// button that has nothing to open (offline, locked). Same shape as the board's wrong-queen
    /// shake, as a rotation, because a small icon reads better rocking in place than sliding.
    ///
    /// Added from code onto the icon itself (<see cref="On"/>), so the prefab needs nothing but the
    /// $-named icon. Living on the icon also means the icon's own OnDisable resets it: a state root
    /// switched off mid-swing never comes back tilted.
    /// </summary>
    public sealed class MBWiggle : MonoBehaviour {

        const float Duration = 0.3f, MaxAngle = 15f;

        Coroutine _routine;

        /// <summary>The wiggle on <paramref name="icon"/>, added the first time it is asked for.
        /// Null in, null out — a missing icon just means no wiggle.</summary>
        public static MBWiggle On(Transform icon) {
            if (icon == null) return null;
            var wiggle = icon.GetComponent<MBWiggle>();
            return wiggle != null ? wiggle : icon.gameObject.AddComponent<MBWiggle>();
        }

        /// <summary>Rock now. A tap during a wiggle starts it over rather than stacking.</summary>
        public void Play() {
            if (!isActiveAndEnabled) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(_rock());
        }

        IEnumerator _rock() {
            for (float e = 0f; e < Duration; e += Time.unscaledDeltaTime) {
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 60f) * MaxAngle * (1f - e / Duration));
                yield return null;
            }
            _reset();
        }

        void OnDisable() => _reset();

        void _reset() {
            _routine = null;
            transform.localRotation = Quaternion.identity;
        }
    }
}
