using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    public class MBLevelButton : MonoBehaviour {

        public void OnEnable() {
            int n = AppData.LevelIdx + 1;   // endless: keeps counting past the pack size
            bool hard = AppData.IsHardLevel;

            var regular = transform.RecursiveFindChild("$Regular");
            var hardSkin = transform.RecursiveFindChild("$Hard");
            var shown = hard && hardSkin != null ? hardSkin : regular;

            if (regular != null) regular.gameObject.SetActive(shown == regular);
            if (hardSkin != null) hardSkin.gameObject.SetActive(shown == hardSkin);

            // Each skin carries its own label; fill both so a later switch never shows a stale number.
            SetText(regular, n);
            SetText(hardSkin, n);

            // The press tint has to land on the skin that is actually showing.
            var graphic = shown != null ? shown.GetComponent<Graphic>() : null;
            if (graphic != null) GetButton().targetGraphic = graphic;
        }

        static void SetText(Transform skin, int n) {
            if (skin == null) return;
            var text = skin.RecursiveFindChild<TMP_Text>("$Text");
            if (text != null) text.text = $"Level {n}";
        }

        public Button GetButton() {
            return GetComponent<Button>();
        }
    }
}
