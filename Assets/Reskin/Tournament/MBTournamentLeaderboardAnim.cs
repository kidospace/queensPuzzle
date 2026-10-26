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

        const string Show = "Show", InfoIn = "InfoIn", InfoOut = "InfoOut";
        static readonly int ShowState = Animator.StringToHash(Show);
        static readonly int InfoInState = Animator.StringToHash(InfoIn);
        static readonly int InfoOutState = Animator.StringToHash(InfoOut);

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the opening. Driven by MBTournamentLeaderboard.Show.</summary>
        public void PlayIn() => _play(ShowState);

        /// <summary>True once the clip has played through and the animator is back in the empty
        /// Idle default — which, with Write Defaults on, is the prefab exactly as authored.</summary>
        public bool IsFinished() => _done(Show);

        /// <summary>The info panel revealing itself a step at a time.</summary>
        public void PlayInfo() => _play(InfoInState);

        /// <summary>And taking it all back. The only state that holds its last frame (everything
        /// faded out) — the panel is switched off right after, and Keep Animator State On Disable
        /// stops that held frame from being captured as the new defaults.</summary>
        public void PlayInfoOut() => _play(InfoOutState);

        public bool IsInfoOutDone() => _done(InfoOut);

        void _play(int state) {
            _animator.Play(state, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        bool _done(string state) {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName(state) || st.normalizedTime >= 1f;
        }
    }
}
