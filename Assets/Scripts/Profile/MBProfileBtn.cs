using UnityEngine;

namespace qp {

    /// <summary>
    /// The lobby's profile button: keeps the <see cref="MBSkinPortrait"/> inside it showing what
    /// the player wears. That is all it does — drawing is the portrait's, opening the popup is
    /// the lobby's.
    ///
    /// It redraws when <see cref="ProfileManager.SkinsVersion"/> moves — after a save in the
    /// profile popup, a new item, anything. Checking is one int compared a frame; the portrait is
    /// only touched on the frame the number actually changed.
    /// </summary>
    public sealed class MBProfileBtn : MonoBehaviour {

        MBSkinPortrait _portrait;
        int _drawnVersion = -1;   // -1 so the first look always draws

        void Awake() => _portrait = GetComponentInChildren<MBSkinPortrait>(true);

        void OnEnable() => _draw();

        void Update() {
            if (ProfileManager.SkinsVersion != _drawnVersion) _draw();
        }

        void _draw() {
            _drawnVersion = ProfileManager.SkinsVersion;
            if (_portrait != null) _portrait.Show(ProfileManager.Skins);
        }
    }
}
