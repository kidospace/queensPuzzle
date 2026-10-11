using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side visuals for the tournament CLAIM screen — the opening cascade, and then the
    /// gift. Lives in the reskin folder so MBTournamentEndClaim carries no skin-specific
    /// references, and is optional: a skin without it has no component and the calls no-op.
    ///
    /// Two states. "Show" is the default and runs as the screen appears; "Reward" is played by
    /// hand when Claim is pressed. An empty-handed default matters here — a controller that opened
    /// on Reward would spring the gift before the player had touched anything.
    ///
    /// The clips drive the %-marked objects: GIftBoxs — the PARENT of all three boxes, so one
    /// curve pops whichever place the player came in — %Light behind it, and $%1 / $%2 for the
    /// rewards. WHICH box is open and WHAT is in it is the screen's business, not the clip's; both
    /// are already set before Reward plays.
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

        /// <summary>True once that clip has played through. A one-shot holds at its end, so the
        /// state simply no longer being the current one counts as finished too.</summary>
        bool _done(string state) {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName(state) || st.normalizedTime >= 1f;
        }
    }
}
