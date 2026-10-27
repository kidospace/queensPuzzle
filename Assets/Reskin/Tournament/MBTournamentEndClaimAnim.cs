using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side visuals for the tournament CLAIM screen — the opening cascade, and then the
    /// gift. Lives in the reskin folder so MBTournamentEndClaim carries no skin-specific
    /// references, and is optional: a skin without it has no component and the calls no-op.
    ///
    /// The default is an empty Idle with Write Defaults on, so at rest everything sits at its
    /// prefab values. "Show" runs as the screen appears and "Reward" when Claim is pressed; both
    /// end on the prefab values and fall back to Idle. Keep Animator State On Disable is on, so
    /// closing mid-clip never captures animated values as the next defaults.
    ///
    /// The clips drive GIftBoxs — the PARENT of all three boxes, so one curve pops whichever
    /// place the player came in — and $%1 / $%2 for the rewards. WHICH box is open and WHAT is in
    /// it is the screen's business, not the clip's; both are already set before Reward plays.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBTournamentEndClaimAnim : MonoBehaviour, IPopupAnim {

        const string Show = "Show", Reward = "Reward";
        static readonly int ShowState = Animator.StringToHash(Show);
        static readonly int RewardState = Animator.StringToHash(Reward);

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>The screen coming in. Driven by MBTournamentEndClaim.Show.</summary>
        public void PlayIn() => _play(ShowState);

        /// <summary>The box springs open and the rewards come out. Driven the moment Claim is
        /// pressed — after the prize has already been granted.</summary>
        public void PlayReward() => _play(RewardState);

        public bool IsFinished() => _done(Show);
        public bool IsRewardFinished() => _done(Reward);

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
