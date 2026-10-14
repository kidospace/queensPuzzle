using Common;
using UnityEngine;

namespace qp {
    /// <summary>
    /// Reskin-side show visuals for the lose popup: rewinds the Animator on every open and
    /// receives the animation events fired by LosePopupShow.anim. Lives in the reskin folder
    /// so MBFailPopup itself carries no skin-specific references.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MBLosePopupAnim : MonoBehaviour, IPopupAnim {

        static readonly int ShowState = Animator.StringToHash("Show");
        static readonly int BreatheState = Animator.StringToHash("Breathe");
        static readonly int StillState = Animator.StringToHash("Still");

        Animator _animator;
        MBSpinePlayer _char;
        GameObject _showVideo;
        bool _breathe;

        void Awake() {
            _animator = GetComponent<Animator>();
            _char = transform.RecursiveFindChild<MBSpinePlayer>("$LoseDog");
            _showVideo = transform.RecursiveFindChild("$ShowVideo").gameObject;
        }

        // Disabling the popup resets the Animator to its default states (Breathe layer: Still).
        void OnEnable() => _breathe = false;

        // $BtnContinue breathes (Breathe layer) only while it offers a video to watch.
        void Update() {
            bool breathe = _showVideo.activeInHierarchy;
            if (breathe == _breathe) return;
            _breathe = breathe;
            int layer = _animator.GetLayerIndex("Breathe");
            if (breathe) {
                // Join the Idle loop at its current phase, so the button breathes with the rest of
                // the idle (both clips are the same length).
                var idle = _animator.GetCurrentAnimatorStateInfo(_animator.GetLayerIndex("Idle"));
                float phase = (idle.normalizedTime % 1f) * idle.length;
                _animator.CrossFadeInFixedTime(BreatheState, 0.2f, layer, phase);
            }
            else {
                _animator.CrossFadeInFixedTime(StillState, 0.2f, layer);
            }
        }

        /// <summary>Rewinds and plays the show cascade. Driven by MBFailPopup.Show.</summary>
        public void PlayIn() {
            // Clear the previous run's last frame so the dog opens on the neutral "LoseIn" first
            // frame instead of flashing the stale pose until PlayCharIn fires later in the cascade.
            if (_char != null) _char.StartFrame("LoseIn");

            _animator.Play(ShowState, 0, 0f);

            // Evaluate frame 0 now, or the popup renders one frame at rest values before the
            // Animator's own update lands. Guarded because Update asserts on m_DidAwake if the
            // Animator has not awoken yet.
            if (_animator.isInitialized) _animator.Update(0f);
        }

        /// <summary>True once the Show cascade has played through (or moved on to Idle).</summary>
        public bool IsFinished() {
            if (_animator == null || !_animator.isInitialized) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            return !st.IsName("Show") || st.normalizedTime >= 1f;
        }

        /// <summary>Animation event. Starts the character; its controller chains LoseIn -> LoseIdle.</summary>
        public void PlayCharIn() {
            if (_char != null) _char.Play("LoseIn");
        }

        /// <summary>Animation event. Whines the lose puppy. Silent while the clip is still a placeholder.</summary>
        public void PlayPuppySFX() {
            var clip = GPSFX.Instance.PuppyLose;
            if (clip != null) CommonSFX.Play(clip);
        }
    }
}
