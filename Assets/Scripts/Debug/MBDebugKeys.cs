#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Keyboard shortcuts for the things worth reaching without a detour through the settings
    /// popup. Editor and development builds only — the whole file compiles out of release.
    ///
    /// It lives on its own object created at boot rather than inside <see cref="MBDebugWin"/>,
    /// because that panel does not exist until something opens it, and opening it is one of the
    /// things worth a shortcut.
    ///
    /// Nothing else in the game binds a key, so there is nothing here to collide with.
    /// </summary>
    public sealed class MBDebugKeys : MonoBehaviour {

        /// <summary>Pull the network out from under the game, and put it back.</summary>
        const UnityEngine.InputSystem.Key Offline = UnityEngine.InputSystem.Key.F9;

        /// <summary>The debug panel itself. Escape closes it again, from the panel's own
        /// Update — it is only there while the panel is.</summary>
        const UnityEngine.InputSystem.Key Panel = UnityEngine.InputSystem.Key.F1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            var go = new GameObject("$DebugKeys");
            DontDestroyOnLoad(go);
            go.AddComponent<MBDebugKeys>();
        }

        void Update() {
            var kb = UnityEngine.InputSystem.Keyboard.current;   // the Input System (no keyboard on most phones)
            if (kb == null) return;
            if (kb[Offline].wasPressedThisFrame) {
                InternetConnection.ForceOffline = !InternetConnection.ForceOffline;
                // Debug.Log and not CDebug.Log: the latter is an analytics event.
                Debug.Log($"[DebugKeys] network {(InternetConnection.ForceOffline ? "OFF" : "back on")}");
            }

            if (kb[Panel].wasPressedThisFrame) MBDebugWin.Open();
        }
    }
}
#endif
