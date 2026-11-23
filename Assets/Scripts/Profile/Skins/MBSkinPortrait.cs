using System.Collections.Generic;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Somebody's avatar, the frame around it and the badge on its corner. Its whole API is
    /// <see cref="Show"/>: hand it the skins to draw and it draws them. It does not know whose
    /// they are or where they came from — the player's saved choice, a draft in the profile
    /// popup, a rival's row.
    ///
    /// Nothing is ever fitted into a box: every picture is shown at the size it was drawn, around
    /// the point it is anchored to in the prefab. A frame may be bigger than another — what the
    /// art has to keep is the ring in the middle of its own picture. How big the whole thing is
    /// on a given screen is the root's scale, and how the avatar and the badge sit against the
    /// frame is their own scale; all of that belongs to the prefab, and this touches none of it.
    ///
    /// A slot with nothing to show (no badge, an id this build does not know and nothing free)
    /// simply turns its image off.
    /// </summary>
    public sealed class MBSkinPortrait : MonoBehaviour {

        Image _avatar, _frame, _badge;

        /// <summary>Draw these skins. It only reads the list; call again after changing it.
        /// Null or empty draws what a player who wears nothing gets.</summary>
        public void Show(List<SkinRef> skins) {
            if (_avatar == null) {
                _avatar = transform.RecursiveFindChild<Image>("$Avatar");
                _frame = transform.RecursiveFindChild<Image>("$Frame");
                _badge = transform.RecursiveFindChild<Image>("$Badge");
            }
            _draw(_avatar, SkinCatalog.Avatar, skins);
            _draw(_frame, SkinCatalog.Frame, skins);
            _draw(_badge, SkinCatalog.Badge, skins);
        }

        static void _draw(Image image, string slot, List<SkinRef> skins) {
            if (image == null) return;
            var sprite = SkinCatalog.Asset<Sprite>(slot, SkinRef.Worn(skins, slot));
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (sprite != null) image.SetNativeSize();
        }
    }
}
