using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// App-wide sounds (buttons, popups) + the one Play() everybody calls:
    /// CommonSFX.Play(GPSFX.Instance.PlaceQueen). Loaded once from Resources/CommonSFX
    /// and kept for the whole run.
    /// </summary>
    [CreateAssetMenu(fileName = "CommonSFX", menuName = "QueensPuzzle/Common SFX")]
    public class CommonSFX : ScriptableObject {

        public AudioClip Click;
        public AudioClip ButtonDown;
        public AudioClip ButtonUp;

        /// <summary>A gift opening. Here and not in GPSFX because gifts are handed out in the
        /// lobby too (the tournament's prize), and GPSFX is released on the way out of Gameplay.</summary>
        public AudioClip RewordOpen;

        const string ResourcePath = "CommonSFX";
        static CommonSFX _instance;

        public static CommonSFX Instance {
            get {
                if (_instance == null) {
                    _instance = Resources.Load<CommonSFX>(ResourcePath);
                    if (_instance == null)
                        CDebug.LogError($"[CommonSFX] Resources/{ResourcePath}.asset not found.");
                }
                return _instance;
            }
        }

        public static void Play(AudioClip clip, float volume = 1f) {
            if (clip == null) {
                CDebug.LogError("[CommonSFX] Play called with a null clip — not assigned in the asset?");
                return;
            }

            clip.PlaySFX(volume);
        }
    }
}
