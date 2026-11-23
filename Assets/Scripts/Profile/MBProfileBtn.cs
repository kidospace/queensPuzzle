using UnityEngine;

namespace qp {

    /// <summary>
    /// The lobby's profile button: keeps the <see cref="MBSkinPortrait"/> inside it showing what
    /// the player wears. That is all it does — drawing is the portrait's, opening the popup is
    /// the lobby's.
    ///
    /// It also carries the red dot that says something new is waiting inside.
    ///
    /// It redraws when <see cref="ProfileManager.SkinsVersion"/> moves — after a save in the
    /// profile popup, a new item, anything. Checking is one int compared a frame; the portrait is
    /// only touched on the frame the number actually changed.
    /// </summary>
    public sealed class MBProfileBtn : MonoBehaviour {

        MBSkinPortrait _portrait;
        GameObject _new;          // $New: the red dot - something new is waiting in the profile
        int _drawnVersion = -1;   // -1 so the first look always draws

        void Awake() {
            _portrait = GetComponentInChildren<MBSkinPortrait>(true);
            var dot = transform.Find("$New");
            if (dot != null) _new = dot.gameObject;
        }

        void OnEnable() => _draw();

        void Update() {
            if (ProfileManager.SkinsVersion != _drawnVersion) _draw();
        }

        void _draw() {
            _drawnVersion = ProfileManager.SkinsVersion;
            if (_portrait != null) _portrait.Show(ProfileManager.Skins);
            if (_new != null && _new.activeSelf != ProfileManager.HasNew()) _new.SetActive(ProfileManager.HasNew());
        }
    }
}
