using UnityEngine;

namespace qp {

    /// <summary>
    /// Reskin-side show visuals for the streak REPAIR popup: rewinds the Animator on every open and
    /// plays the "Show" cascade (background in, card up, then its rows one after another). Lives in
    /// the reskin folder so MBDailyStreakRepairPopup carries no skin-specific references.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBDailyStreakRepairPopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>Rewinds and plays the show cascade. Driven by MBDailyStreakRepairPopup.Show.</summary>
        public void PlayIn() {
            _animator.Play(ShowState, 0, 0f);
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>True once the Show clip has played through (it returns to Idle at the end).</summary>
        public bool IsFinished() {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName("Show") || st.normalizedTime >= 1f;
        }
    }
}
