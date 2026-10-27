using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side opening for the tournament RESULT screen — the one a player who finished
    /// outside the paying places lands on. Lives in the reskin folder so MBTournamentEndNoClaim
    /// carries no skin-specific references, and is optional: a skin without it has no component
    /// and the screen simply appears.
    ///
    /// The clip drives the %-marked objects. Nothing in it decides anything — the podium is
    /// already filled before this plays.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBTournamentEndNoClaimAnim : MonoBehaviour, IPopupAnim {

        const string Show = "Show";
        static readonly int ShowState = Animator.StringToHash(Show);

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the opening. Driven by MBTournamentEndNoClaim.Show.</summary>
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
    }
}
