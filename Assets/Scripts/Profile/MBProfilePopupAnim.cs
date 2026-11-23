using UnityEngine;

namespace qp {

    /// <summary>
    /// The profile popup's way in: rewinds the Animator on every open and plays "Show" — the
    /// cover fades up and the card settles into place. Soft on purpose: a short ease, no bounce.
    /// The popup only knows <see cref="IPopupAnim"/>, so it works the same with or without this.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBProfilePopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");

        Animator _animator;

        void Awake() => _animator = GetComponent<Animator>();

        public void PlayIn() {
            if (_animator == null) _animator = GetComponent<Animator>();
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
