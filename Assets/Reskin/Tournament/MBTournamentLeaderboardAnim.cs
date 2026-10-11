using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side opening for the tournament leaderboard — the cascade that brings the banner,
    /// the list and the button in. Lives in the reskin folder so MBTournamentLeaderboard carries no
    /// skin-specific references, and is optional: a skin without it has no component and the screen
    /// simply appears.
    ///
    /// The clip drives the %-marked objects. Nothing in it decides anything — the table is already
    /// filled and scrolled to the player's row before this plays.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBTournamentLeaderboardAnim : MonoBehaviour, IPopupAnim {

        const string Show = "Show";
        static readonly int ShowState = Animator.StringToHash(Show);

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the opening. Driven by MBTournamentLeaderboard.Show.</summary>
        public void PlayIn() {
            _animator.Play(ShowState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>True once the clip has played through (a one-shot, so it holds at the end).</summary>
        public bool IsFinished() {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName(Show) || st.normalizedTime >= 1f;
        }
    }
}
