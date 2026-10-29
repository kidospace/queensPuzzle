using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace qp {

    /// <summary>
    /// Fills in the Event Camera of every World Space / Screen Space – Camera canvas in the open
    /// scenes with that scene's Main Camera — Editor only, so the Inspector stops warning about it.
    ///
    /// The popups are World Space canvases inside prefabs, and a prefab cannot hold a reference to
    /// a scene's camera, so the field is empty in every one of them. At runtime that is harmless —
    /// an empty Event Camera falls back to the Main Camera — but the Editor flags it on each one.
    ///
    /// Done here rather than on the instances so nothing has to be kept: it runs whenever a scene
    /// opens or the hierarchy changes, so a prefab that is reverted gets it straight back. The value
    /// is assigned without being recorded, so it never becomes a prefab override and never marks
    /// the scene dirty. Only top-level canvases with no camera of their own are touched: a nested
    /// canvas takes its parent's, and one somebody set by hand is left alone. A prefab opened on its
    /// own has no scene camera to give, so the warning stays there.
    /// </summary>
    [InitializeOnLoad]
    static class CanvasEventCameraAutoAssign {

        static CanvasEventCameraAutoAssign() {
            EditorSceneManager.sceneOpened += (_, __) => _assign();
            EditorApplication.hierarchyChanged += _assign;
            EditorApplication.delayCall += _assign;   // the scenes already open when scripts reload
        }

        static void _assign() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                var cam = _mainCamera(scene);
                if (cam == null) continue;

                foreach (var root in scene.GetRootGameObjects())
                    foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) {
                        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.worldCamera != null) continue;
                        // top-level only: a nested canvas takes its parent's camera
                        if (canvas.transform.parent != null && canvas.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;
                        canvas.worldCamera = cam;
                    }
            }
        }

        /// <summary>The camera tagged MainCamera in this scene, or null.</summary>
        static Camera _mainCamera(Scene scene) {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    if (cam.CompareTag("MainCamera")) return cam;
            return null;
        }
    }
}
