using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side visuals for the tournament CLAIM screen — the opening cascade. Lives in the
    /// reskin folder so MBTournamentEndClaim carries no skin-specific references, and is optional:
    /// a skin without it has no component and the calls no-op. The gift itself opens in the gift
    /// popup after Claim.
    ///
    /// The default is an empty Idle with Write Defaults on, so at rest everything sits at its
    /// prefab values. "Show" runs as the screen appears, ends on the prefab values and falls back
    /// to Idle. Keep Animator State On Disable is on, so closing mid-clip never captures animated
    /// values as the next defaults.
    ///
    /// The clip drives GIftBoxs — the PARENT of all three boxes, so one curve pops whichever
    /// place the player came in. WHICH box shows is the screen's business, not the clip's.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBTournamentEndClaimAnim : MonoBehaviour, IPopupAnim {

        const string Show = "Show";
        static readonly int ShowState = Animator.StringToHash(Show);

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>The screen coming in. Driven by MBTournamentEndClaim.Show.</summary>
        public void PlayIn() => _play(ShowState);

        public bool IsFinished() => _done(Show);

        void _play(int state) {
            _animator.Play(state, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>True once that clip has played through — by then the animator has gone back
        /// to Idle, so the state no longer being the current one counts as finished.</summary>
        bool _done(string state) {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName(state) || st.normalizedTime >= 1f;
        }
    }
}
