using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Holds the gift art in the popup. The art is any box prefab (Gifts/Boxes/Box1..);
    /// it is instantiated twice — once as is into $BoxSlot, once into $GlowSlot with every
    /// Image switched to the additive whiten material, so the white "box turns into light"
    /// copy always matches the art. The reveal clip animates only the two slots' alpha.
    /// </summary>
    public class MBGiftBox : MonoBehaviour {

        public Material WhitenMaterial;

        public void SetBox(GameObject boxPrefab) {
            _fill(transform.Find("$BoxSlot"), boxPrefab, null);
            _fill(transform.Find("$GlowSlot"), boxPrefab, WhitenMaterial);
        }

        static void _fill(Transform slot, GameObject boxPrefab, Material mat) {
            for (int i = slot.childCount - 1; i >= 0; i--) {
                var c = slot.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            var box = Instantiate(boxPrefab, slot, false);
            box.name = boxPrefab.name;
            foreach (var img in box.GetComponentsInChildren<Image>(true)) {
                img.raycastTarget = false;
                if (mat != null) img.material = mat;
            }
        }
    }
}
