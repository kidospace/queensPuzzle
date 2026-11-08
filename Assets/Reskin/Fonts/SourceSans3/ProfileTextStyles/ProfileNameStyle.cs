using TMPro;
using UnityEngine;

namespace qp {

    // One approved look for a player's name: a preset and a color that were checked together.
    // Either can be empty, which keeps the font's base material or the row's own color.
    public sealed class ProfileNameStyle : ScriptableObject {
        public Material preset;
        public TMP_ColorGradient color;
    }
}
