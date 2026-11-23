using TMPro;
using UnityEngine;

namespace qp {

    /// <summary>
    /// A name label that can wear a name skin. Its whole API is <see cref="Show"/>: hand it a
    /// style and the label wears it; hand it null and the label goes back to how the prefab
    /// drew it.
    ///
    /// A style decides the whole look. Under one the label is always white, so the colour comes
    /// from the style alone — its gradient, or the face colour of its material — and a skin looks
    /// the same wherever it is shown. The label's own colour only ever shows without a style,
    /// and this remembers it, so nobody has to pass it in.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class MBSkinName : MonoBehaviour {

        TMP_Text _label;

        // The label as the prefab drew it.
        Material _ownMaterial;
        Color _ownColor;
        bool _ownGradientOn;
        TMP_ColorGradient _ownGradient;

        public void Show(SkinNameStyle style) {
            if (_label == null) {
                _label = GetComponent<TMP_Text>();
                _ownMaterial = _label.fontSharedMaterial;
                _ownColor = _label.color;
                _ownGradientOn = _label.enableVertexGradient;
                _ownGradient = _label.colorGradientPreset;
            }

            if (style == null) {
                _label.fontSharedMaterial = _ownMaterial;
                _label.enableVertexGradient = _ownGradientOn;
                _label.colorGradientPreset = _ownGradient;
                _label.color = _ownColor;
                return;
            }

            // No preset = the font's plain material; the gradient, if any, does the colouring.
            _label.fontSharedMaterial = style.preset != null ? style.preset : _label.font.material;
            _label.enableVertexGradient = style.color != null;
            _label.colorGradientPreset = style.color;
            _label.color = Color.white;
        }
    }
}
