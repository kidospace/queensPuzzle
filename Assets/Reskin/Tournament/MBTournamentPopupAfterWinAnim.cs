using Coffee.UIExtensions;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side opening for the after-win tournament popup — the quick arrival, ending with
    /// "Tap to Continue". Lives in the reskin folder so MBTournamentPopupAfterWin carries no
    /// skin-specific references, and is optional: a skin without it has no component and the
    /// screen simply appears.
    ///
    /// The row travelling to its new place is NOT a clip — it is measured off where the rows
    /// actually are at runtime, which nothing can be authored against. The two things that can be
    /// are here: the arrival, and the green arrow when the player moved up.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBTournamentPopupAfterWinAnim : MonoBehaviour, IPopupAnim {

        const string Show = "Show", RankUp = "RankUp", OfflineGone = "OfflineGone";
        static readonly int ShowState = Animator.StringToHash(Show);
        static readonly int RankUpState = Animator.StringToHash(RankUp);
        static readonly int OfflineGoneState = Animator.StringToHash(OfflineGone);

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the opening. Driven by MBTournamentPopupAfterWin.Show.</summary>
        public void PlayIn() {
            _animator.Play(ShowState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>True once the clip has played through and the animator is back in the empty
        /// Idle default — which, with Write Defaults on, is the prefab exactly as authored.</summary>
        public bool IsFinished() {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName(Show) || st.normalizedTime >= 1f;
        }

        /// <summary>The green arrow arriving. Played by hand, because only the screen knows
        /// whether the win moved the player up at all.</summary>
        public void PlayRankUp() {
            _animator.Play(RankUpState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>The no-connection panel leaving, once the line is back, while the table
        /// fades in. The one state that holds its last frame — the panel is switched off right
        /// after, and Keep Animator State On Disable keeps that frame out of the defaults.</summary>
        public void PlayOfflineGone() {
            _animator.Play(OfflineGoneState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        public bool IsOfflineGone() {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName(OfflineGone) || st.normalizedTime >= 1f;
        }

        /// <summary>
        /// Animation event. Fires the particle system named by the event's string argument, so
        /// the clip picks the moment the sparks leave the arrow.
        ///
        /// The emitter ships switched OFF. A UIParticle divides by its own lossy scale, and this
        /// screen spends its life inactive with the layout scripts above it sitting at zero —
        /// alive under that it fills the console with NaN mesh bounds every frame. It wakes up
        /// here, by which point the layout is real.
        /// </summary>
        public void PlayPS(string psName) {
            var t = transform.RecursiveFindChild(psName);
            if (t == null) return;
            t.gameObject.SetActive(true);

            // UIParticle wraps the emitter to render it inside the canvas and owns its play
            // state — going straight to ParticleSystem.Play would bypass that bookkeeping.
            var uip = t.GetComponent<UIParticle>();
            if (uip != null) { uip.Play(); return; }

            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null) ps.Play();
        }
    }
}
