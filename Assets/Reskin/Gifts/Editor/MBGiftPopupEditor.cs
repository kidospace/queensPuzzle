using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Debug buttons for MBGiftPopup. Apply puts DebugBox and DebugRewards in through GiftsManager,
    /// marked DontSave so they never end up in the prefab or the scene. ▶ Reveal plays the reveal:
    /// in play mode through the Animator trigger, in edit mode by sampling the clip in AnimationMode
    /// (like the Animation window), which restores everything when the preview ends. Particles
    /// (the confetti) only run in play mode.
    /// </summary>
    [CustomEditor(typeof(MBGiftPopup))]
    public class MBGiftPopupEditor : Editor {

        AnimationClip _clip;
        double _startTime;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Apply", GUILayout.Height(30))) _apply();
                if (GUILayout.Button("▶ Reveal", GUILayout.Height(30))) _reveal();
            }
        }

        void _apply() {
            var p = (MBGiftPopup)target;
            if (!GiftsManager.Prepare(p, p.DebugBox, p.DebugRewards)) return;   // the real path
            foreach (var n in new[] { "$BoxSlot", "$GlowSlot" })
                foreach (Transform c in p.Box.transform.Find(n)) _dontSave(c);
            foreach (Transform c in p.Rewards.transform) _dontSave(c);
            SceneView.RepaintAll();
        }

        static void _dontSave(Transform t) {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) {
                c.gameObject.hideFlags = HideFlags.DontSave;
                foreach (var comp in c.GetComponents<Component>()) if (comp != null) comp.hideFlags = HideFlags.DontSave;
            }
        }

        void _reveal() {
            var p = (MBGiftPopup)target;
            if (Application.isPlaying) { p.Reveal(); return; }
            _clip = null;
            foreach (var c in p.Anim.runtimeAnimatorController.animationClips)
                if (c.name.Contains("Reveal")) _clip = c;
            if (_clip == null) return;
            _startTime = EditorApplication.timeSinceStartup;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            EditorApplication.update -= _tick;
            EditorApplication.update += _tick;
        }

        void _tick() {
            var p = target as MBGiftPopup;
            if (p == null || _clip == null) { _stop(); return; }
            float t = (float)(EditorApplication.timeSinceStartup - _startTime);
            if (t > _clip.length + 1f) { _stop(); return; }   // hold the last frame a moment
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(p.gameObject, _clip, Mathf.Min(t, _clip.length));
            AnimationMode.EndSampling();
            p.Rewards.Refresh();
            SceneView.RepaintAll();
        }

        void _stop() {
            EditorApplication.update -= _tick;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            var p = target as MBGiftPopup;
            if (p != null) p.Rewards.Refresh();
            SceneView.RepaintAll();
        }

        void OnDisable() {
            _stop();
        }
    }
}
