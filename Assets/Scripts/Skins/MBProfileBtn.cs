using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The player as they look right now: avatar, the frame around it, and the badge on its
    /// corner. Drop it on the button and that is the whole wiring — it asks
    /// <see cref="ProfileManager"/> what is worn in each slot and the <see cref="SkinCatalog"/>
    /// what that looks like.
    ///
    /// It redraws itself when <see cref="ProfileManager.SkinsVersion"/> moves — after a save in
    /// the profile popup, a new item, anything. Checking is one int compared a frame; the
    /// sprites are only touched on the frame the number actually changed.
    ///
    /// A slot with nothing to show (no badge, an id this build does not know and no default)
    /// simply turns its image off.
    /// </summary>
    public sealed class MBProfileBtn : MonoBehaviour {

        Image _avatar, _frame, _badge;
        int _drawnVersion = -1;   // -1 so the first look always draws

        void Awake() {
            _avatar = transform.RecursiveFindChild<Image>("$Avatar");
            _frame = transform.RecursiveFindChild<Image>("$Frame");
            _badge = transform.RecursiveFindChild<Image>("$Badge");
        }

        void OnEnable() => _draw();

        void Update() {
            if (ProfileManager.SkinsVersion != _drawnVersion) _draw();
        }

        void _draw() {
            _drawnVersion = ProfileManager.SkinsVersion;
            _show(_avatar, SkinCatalog.Avatar);
            _show(_frame, SkinCatalog.Frame);
            _show(_badge, SkinCatalog.Badge);
        }

        // Nothing is ever fitted into a box: every picture is shown at the size it was drawn,
        // around the point it is anchored to in the prefab. A frame may be bigger than another —
        // what the art has to keep is the ring in the middle of its own picture. How big the
        // whole thing is on a given screen is the root's scale, and how small the badge sits on
        // the avatar is the badge's scale; both belong to the prefab, and this touches neither.
        static void _show(Image image, string slot) {
            if (image == null) return;
            var sprite = SkinCatalog.Asset<Sprite>(slot, ProfileManager.Equipped(slot));
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (sprite != null) image.SetNativeSize();
        }
    }
}
