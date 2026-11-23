using System.Collections;
using System.Collections.Generic;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The profile popup: the player on top, four tabs — avatar, frame, name, badge — and a grid
    /// of everything the open tab holds.
    ///
    /// Picking is a DRAFT. A tap changes the portrait on top and nothing else; nobody else sees
    /// it and nothing is saved until Save, which equips all four slots at once. The X throws the
    /// draft away.
    ///
    /// An item the player does not have is shown dimmed under a lock and cannot be picked.
    /// A slot nobody has to wear (name, badge) is emptied by tapping the picked item again.
    ///
    /// Woken by the lobby and hidden after the first layout pass, like the other popups.
    /// </summary>
    public sealed class MBProfilePopup : MonoBehaviour {

        // The tabs, left to right.
        static readonly string[] Slots = { SkinCatalog.Avatar, SkinCatalog.Frame, SkinCatalog.Name, SkinCatalog.Badge };

        [Tooltip("How big a picture sits in a grid cell, per tab: avatar, frame, name, badge. " +
                 "One number for the whole tab, so every frame's ring comes out the same size.")]
        [SerializeField] float[] _iconScale = { 0.95f, 0.82f, 1f, 1f };

        CanvasGroup _group;
        bool _showing;

        MBSkinPortrait _portrait;    // the preview on top
        TMP_Text _name;
        MBSkinName _nameSkin;        // on $Name: dresses it in the drafted name skin

        GameObject[] _tabOn, _tabOff;
        ScrollRect _scroll;
        RectTransform _grid;
        MBSkinCell _template;        // $Cell — hidden, cloned once per item
        readonly List<MBSkinCell> _cells = new List<MBSkinCell>();

        int _tab;
        readonly List<SkinRef> _draft = new List<SkinRef>();   // what is picked, not yet saved

        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _portrait = transform.RecursiveFindChild<MBSkinPortrait>("$Portrait");
            _name = transform.RecursiveFindChild<TMP_Text>("$Name");
            if (_name != null) _nameSkin = _name.GetComponent<MBSkinName>();

            _tabOn = new GameObject[Slots.Length];
            _tabOff = new GameObject[Slots.Length];
            for (int i = 0; i < Slots.Length; i++) {
                var tab = transform.RecursiveFindChild("$Tab" + i);
                if (tab == null) { CDebug.LogError($"[ProfilePopup] $Tab{i} is missing from the prefab"); continue; }
                _tabOn[i] = _child(tab, "$On");
                _tabOff[i] = _child(tab, "$Off");
                int index = i;
                var button = tab.GetComponent<Button>();
                if (button != null) button.onClick.AddListener(() => _openTab(index));
            }

            _scroll = GetComponentInChildren<ScrollRect>(true);
            _grid = transform.RecursiveFindChild<RectTransform>("$Grid");
            _template = transform.RecursiveFindChild<MBSkinCell>("$Cell");
            if (_template != null) _template.gameObject.SetActive(false);

            _click("$CloseButton", Close);
            _click("$SaveButton", _save);
        }

        // Stay ACTIVE but invisible for the first frames so the UI lays out at real size, then
        // hide until Show().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);
        }

        /// <summary>Open on what the player wears now, on the first tab.</summary>
        public void Show() {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            _draft.Clear();
            foreach (string slot in Slots) SkinRef.Wear(_draft, slot, ProfileManager.Equipped(slot));
            if (_name != null) _name.text = ProfileManager.Name;
            _openTab(0);
            _preview();

            GetComponent<IPopupAnim>()?.PlayIn();
        }

        /// <summary>Close without saving: the draft is simply forgotten.</summary>
        public void Close() {
            _showing = false;
            gameObject.SetActive(false);
        }

        // ---- tabs and grid -------------------------------------------------------------------

        void _openTab(int index) {
            _tab = index;
            for (int i = 0; i < Slots.Length; i++) {
                _show(_tabOn[i], i == index);
                _show(_tabOff[i], i != index);
            }
            _fill();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;   // back to the top
        }

        // One cell per item of the open tab. Cells are kept and reused: a tab with fewer items
        // just leaves the spare ones off.
        void _fill() {
            if (_grid == null || _template == null) return;
            string slotId = Slots[_tab];
            var slot = SkinCatalog.Instance != null ? SkinCatalog.Instance.Slot(slotId) : null;
            int count = slot != null && slot.items != null ? slot.items.Length : 0;
            float scale = _tab < _iconScale.Length ? _iconScale[_tab] : 1f;

            for (int i = 0; i < count; i++) {
                if (i == _cells.Count) {
                    var cell = Instantiate(_template, _grid);
                    cell.name = "Cell" + i;
                    cell.Tapped += _onCell;
                    _cells.Add(cell);
                }
                var item = slot.items[i];
                _cells[i].gameObject.SetActive(true);
                _cells[i].Set(slotId, item, ProfileManager.Owns(slotId, item.id), SkinRef.Worn(_draft, slotId) == item.id, scale);
            }
            for (int i = count; i < _cells.Count; i++) _cells[i].gameObject.SetActive(false);
        }

        void _onCell(MBSkinCell cell) {
            if (!cell.Owned) return;                     // locked: nothing to pick

            string slotId = Slots[_tab];
            bool again = SkinRef.Worn(_draft, slotId) == cell.Item;
            // Tapping the picked item again takes it off — but only where going without is a
            // look of its own. An avatar or a frame always has one.
            if (again && _hasFree(slotId)) return;
            SkinRef.Wear(_draft, slotId, again ? "" : cell.Item);

            string picked = SkinRef.Worn(_draft, slotId);
            foreach (var c in _cells) if (c.gameObject.activeSelf) c.SetSelected(c.Item == picked);
            _preview();
        }

        static bool _hasFree(string slotId) {
            var slot = SkinCatalog.Instance != null ? SkinCatalog.Instance.Slot(slotId) : null;
            return slot != null && slot.FirstFree() != null;
        }

        // The top of the popup, dressed in the draft.
        void _preview() {
            if (_portrait != null) _portrait.Show(_draft);
            if (_nameSkin != null)
                _nameSkin.Show(SkinCatalog.Asset<SkinNameStyle>(SkinCatalog.Name, SkinRef.Worn(_draft, SkinCatalog.Name)));
        }

        // ---- save ----------------------------------------------------------------------------

        void _save() {
            foreach (string slot in Slots) ProfileManager.Equip(slot, SkinRef.Worn(_draft, slot));
            Close();
        }

        // ---- the prefab ----------------------------------------------------------------------

        void _click(string name, UnityEngine.Events.UnityAction action) {
            var button = transform.RecursiveFindChild<Button>(name);
            if (button != null) button.onClick.AddListener(action);
            else CDebug.LogError($"[ProfilePopup] {name} is missing from the prefab");
        }

        static GameObject _child(Transform parent, string name) {
            var found = parent.RecursiveFindChild(name);
            return found != null ? found.gameObject : null;
        }

        static void _show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
    }
}
