using System;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// One item in the profile popup's grid. Its whole API is <see cref="Set"/>: tell it which
    /// item, whether the player has it and whether it is the one picked, and it looks right.
    /// It knows nothing about tabs, drafts or saving — it reports a tap and that is all.
    ///
    /// Every look is already in the prefab and this only turns the right parts on:
    /// $Icon (a picture, at the size it was drawn), $Label (a name skin, shown on a sample of
    /// the player's name), $Inside (what sits in an empty frame), $Back (the tile behind a
    /// label or a badge), $Selected (the tick) and $Lock.
    /// </summary>
    public sealed class MBSkinCell : MonoBehaviour {

        /// <summary>How much of a locked item shows through.</summary>
        const float LockedAlpha = 0.3f;

        Image _icon;
        TMP_Text _label;
        MBSkinName _labelSkin;      // on $Label: dresses it in the item's style
        GameObject _inside, _back, _selected, _lock;
        CanvasGroup _content;       // everything that dims when locked

        /// <summary>The item this cell shows.</summary>
        public string Item { get; private set; } = "";

        /// <summary>The player can pick it.</summary>
        public bool Owned { get; private set; }

        public event Action<MBSkinCell> Tapped;

        void Awake() => _find();

        void _find() {
            _icon = transform.RecursiveFindChild<Image>("$Icon");
            _label = transform.RecursiveFindChild<TMP_Text>("$Label");
            if (_label != null) _labelSkin = _label.GetComponent<MBSkinName>();
            _inside = _child("$Inside");
            _back = _child("$Back");
            _selected = _child("$Selected");
            _lock = _child("$Lock");

            var content = transform.RecursiveFindChild("$Content");
            if (content != null) {
                _content = content.GetComponent<CanvasGroup>();
                if (_content == null) _content = content.gameObject.AddComponent<CanvasGroup>();
            }

            var button = GetComponent<Button>();
            // The sound is played here, on a real tap, and not by an MBSfxButton: that one speaks
            // on finger-down, and in a list a finger goes down to scroll far more often than to pick.
            if (button != null) button.onClick.AddListener(() => {
                CommonSFX.Play(CommonSFX.Instance.ButtonDown);
                Tapped?.Invoke(this);
            });
        }

        GameObject _child(string name) {
            var found = transform.RecursiveFindChild(name);
            return found != null ? found.gameObject : null;
        }

        /// <summary>Draw an item. <paramref name="iconScale"/> is how big pictures of this slot
        /// sit in a cell — one number for the whole slot, so every frame's ring is the same size
        /// whatever its ornaments add around it.</summary>
        public void Set(string slot, SkinItem item, bool owned, bool selected, float iconScale) {
            if (_icon == null && _label == null) _find();   // filled before it was ever enabled
            Item = item.id;
            Owned = owned;

            var sprite = item.asset as Sprite;
            var style = item.asset as SkinNameStyle;

            if (_icon != null) {
                _icon.enabled = sprite != null;
                if (sprite != null) {
                    _icon.sprite = sprite;
                    _icon.SetNativeSize();
                    _icon.transform.localScale = Vector3.one * iconScale;
                }
            }

            if (_label != null) {
                bool named = slot == SkinCatalog.Name;
                _label.gameObject.SetActive(named);
                if (named) {
                    _label.text = ProfileManager.Name;
                    if (_labelSkin != null) _labelSkin.Show(style);
                }
            }

            _show(_inside, slot == SkinCatalog.Frame);
            _show(_back, slot == SkinCatalog.Name || slot == SkinCatalog.Badge);
            _show(_lock, !owned);
            if (_content != null) _content.alpha = owned ? 1f : LockedAlpha;
            SetSelected(selected);
        }

        public void SetSelected(bool selected) => _show(_selected, selected);

        static void _show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
    }
}
