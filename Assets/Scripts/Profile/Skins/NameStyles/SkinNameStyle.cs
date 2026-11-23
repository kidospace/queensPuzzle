using TMPro;
using UnityEngine;

namespace qp {

    // One approved look for a player's name: a preset and a color that were checked together.
    // A style is the whole look: under one the name is white and the colour is this gradient, or
    // the face colour of the preset when there is none. No preset = the font's plain material.
    public sealed class SkinNameStyle : ScriptableObject {
        public Material preset;
        public TMP_ColorGradient color;
    }
}
