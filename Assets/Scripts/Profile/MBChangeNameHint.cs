using System.Collections;
using UnityEngine;

namespace qp {

    /// <summary>
    /// The nudge that points at the name field, for players who have not noticed it.
    ///
    /// Drop it on the hint object and that is the whole wiring — it asks
    /// <see cref="ProfileManager.Named"/> whether it should be there at all, and nothing else in
    /// the game has to know it exists.
    ///
    /// It breathes rather than flashes: it sits in the lobby until it is answered, and anything
    /// sharper than this becomes something to resent by the third launch. The moment the player
    /// names themselves it fades out where it stands and never comes back — the flag it reads is
    /// for life.
    /// </summary>
    public sealed class MBChangeNameHint : MonoBehaviour {

        [Tooltip("How far it drifts, in local units.")]
        [SerializeField] float _rise = 4f;

        [Tooltip("Seconds for one full up-and-down.")]
        [SerializeField] float _period = 2f;

        [Tooltip("Seconds the fade-out takes once the player has renamed.")]
        [SerializeField] float _fadeOut = 0.35f;

        CanvasGroup _group;
        RectTransform _rt;
        Vector2 _home;      // anchored, not local: local moves when the layout sizes the parent
        bool _leaving;

        void Awake() {
            _rt = (RectTransform)transform;
            _home = _rt.anchoredPosition;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        }

        void OnEnable() {
            // Already named — including on a relaunch, where there is nothing to fade and the
            // hint should simply never appear. Nor before the tournament opens: the name is for
            // its leaderboard. The win that unlocks it comes back through a fresh lobby.
            if (ProfileManager.Named || !TournamentManager.IsUnlocked) { gameObject.SetActive(false); return; }

            _leaving = false;
            _group.alpha = 1f;
            _rt.anchoredPosition = _home;
        }

        void Update() {
            if (_leaving) return;

            if (ProfileManager.Named) { StartCoroutine(_leave()); return; }

            // Unscaled: the lobby may be paused behind a popup, and the hint going still would
            // read as the game having frozen.
            float phase = Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / _period));
            _rt.anchoredPosition = _home + Vector2.up * (phase * _rise);
        }

        /// <summary>Settle back to where it started while fading — drifting away mid-bob looks
        /// like something went wrong rather than something being finished.</summary>
        IEnumerator _leave() {
            _leaving = true;
            Vector2 from = _rt.anchoredPosition;

            for (float e = 0f; e < _fadeOut; e += Time.unscaledDeltaTime) {
                float k = e / _fadeOut;
                _rt.anchoredPosition = Vector2.Lerp(from, _home, k);
                _group.alpha = 1f - k;
                yield return null;
            }

            _rt.anchoredPosition = _home;
            _group.alpha = 1f;          // reset, in case something activates it again
            gameObject.SetActive(false);
        }
    }
}
