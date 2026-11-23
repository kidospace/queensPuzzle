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
    /// Everything here is a DRAFT. A tap changes the portrait on top and nothing else, a typed
    /// name sits in its field; nobody else sees any of it and nothing is saved until Save, which
    /// takes the name and all four slots at once. The X throws the draft away.
    ///
    /// The name is checked while it is typed, and Save will not go through with one that is
    /// refused — the line under the field has been saying why the whole time.
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

        // A picture sits well in a square; a name does not — it is wide and low, and in a square
        // it has to shrink until the skin can hardly be seen. So the name tab lays its items out
        // as two columns of wide tiles, with the player's whole name in each.
        [Tooltip("Columns in the grid, per tab: avatar, frame, name, badge.")]
        [SerializeField] int[] _columns = { 3, 3, 2, 3 };

        [Tooltip("Size of one grid cell, per tab: avatar, frame, name, badge.")]
        [SerializeField] Vector2[] _cellSize = {
            new Vector2(270, 275), new Vector2(270, 275), new Vector2(405, 150), new Vector2(270, 275),
        };

        /// <summary>How long the way out takes: a short fade, nothing more.</summary>
        const float CloseSeconds = 0.15f;

        CanvasGroup _group;
        bool _showing;
        Coroutine _closing;          // the fade out, while it runs

        MBSkinPortrait _portrait;    // the preview on top
        TMP_InputField _nameInput;   // the name, editable in place
        TMP_Text _nameError;         // one line under it, or nothing
        TMP_Text _name;
        MBSkinName _nameSkin;        // on $Name: dresses it in the drafted name skin

        GameObject[] _tabOn, _tabOff, _tabNew;   // _tabNew: the red dot on a tab with something new in it
        ScrollRect _scroll;
        RectTransform _grid;
        GridLayoutGroup _gridLayout;
        MBSkinCell _template;        // $Cell — hidden, cloned once per item
        readonly List<MBSkinCell> _cells = new List<MBSkinCell>();
        readonly List<int> _order = new List<int>();     // the open tab's items, in the order they are shown

        int _tab;
        readonly List<SkinRef> _draft = new List<SkinRef>();   // what is picked, not yet saved

        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _portrait = transform.RecursiveFindChild<MBSkinPortrait>("$Portrait");
            _name = transform.RecursiveFindChild<TMP_Text>("$Name");
            _nameError = transform.RecursiveFindChild<TMP_Text>("$NameError");
            _nameInput = transform.RecursiveFindChild<TMP_InputField>("$NameInput");
            if (_nameInput != null) {
                var cfg = ProfileConfig.Instance;
                if (cfg != null) _nameInput.characterLimit = cfg.maxNameLength;   // a limit is better kept than explained
                _nameInput.onValueChanged.AddListener(_ => _showNameError(ProfileManager.Validate(_nameInput.text)));
                // The pencil is the way in for anyone who does not guess the field itself is one.
                var pencil = transform.RecursiveFindChild<Button>("$EditName");
                if (pencil != null) pencil.onClick.AddListener(() => _nameInput.ActivateInputField());
            }
            if (_name != null) _nameSkin = _name.GetComponent<MBSkinName>();

            _tabOn = new GameObject[Slots.Length];
            _tabOff = new GameObject[Slots.Length];
            _tabNew = new GameObject[Slots.Length];
            for (int i = 0; i < Slots.Length; i++) {
                var tab = transform.RecursiveFindChild("$Tab" + i);
                if (tab == null) { CDebug.LogError($"[ProfilePopup] $Tab{i} is missing from the prefab"); continue; }
                _tabOn[i] = _child(tab, "$On");
                _tabOff[i] = _child(tab, "$Off");
                _tabNew[i] = _child(tab, "$New");
                int index = i;
                var button = tab.GetComponent<Button>();
                if (button != null) button.onClick.AddListener(() => _openTab(index));
            }

            _scroll = GetComponentInChildren<ScrollRect>(true);
            _grid = transform.RecursiveFindChild<RectTransform>("$Grid");
            if (_grid != null) _gridLayout = _grid.GetComponent<GridLayoutGroup>();
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
            if (_closing != null) { StopCoroutine(_closing); _closing = null; }   // reopened mid-fade
            _showing = true;
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            gameObject.SetActive(true);

            _draft.Clear();
            foreach (string slot in Slots) SkinRef.Wear(_draft, slot, ProfileManager.Equipped(slot));
            // Open on the name in use, not on whatever was typed and dropped last time.
            if (_nameInput != null) _nameInput.SetTextWithoutNotify(ProfileManager.Name);
            else if (_name != null) _name.text = ProfileManager.Name;
            _showNameError(ENameError.Ok);
            // Open where the news is: a red dot on the lobby button promised something, and the
            // first tab that has it is where the player is looking for it. Nothing new = the first tab.
            int first = 0;
            for (int i = 0; i < Slots.Length; i++) if (ProfileManager.HasNew(Slots[i])) { first = i; break; }
            _openTab(first);
            _preview();

            GetComponent<IPopupAnim>()?.PlayIn();
        }

        /// <summary>Close without saving: the draft is simply forgotten. The popup fades out
        /// rather than vanishing; a second call while it does is ignored.</summary>
        public void Close() {
            if (!_showing || _closing != null) return;
            if (!gameObject.activeInHierarchy) { _showing = false; return; }   // no coroutine on an inactive object
            _closing = StartCoroutine(_fadeOut());
        }

        IEnumerator _fadeOut() {
            _group.blocksRaycasts = false;      // nothing can be tapped on the way out
            for (float e = 0f; e < CloseSeconds; e += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.SmoothStep(0f, 1f, e / CloseSeconds);
                yield return null;
            }
            _group.alpha = 1f;                  // back to the prefab's value for the next open
            _group.blocksRaycasts = true;
            _closing = null;
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
            _scrollToPicked();

            // The items were just drawn with their dots, so the player is looking at them: that is
            // seen. The dots stay on the cells for this visit; the tab's own goes now, and the
            // lobby button's once no tab has one left.
            ProfileManager.MarkSeen(Slots[index]);
            for (int i = 0; i < Slots.Length; i++) _show(_tabNew[i], ProfileManager.HasNew(Slots[i]));
        }

        // Open a tab on what the player came for, not on the top of the list. If something in it
        // is new, that: the red dot brought them here, and it may be rows below. Otherwise the
        // item that is picked, so they see what they have on without looking for it. The row is
        // brought to the middle; neither = the top.
        void _scrollToPicked() {
            if (_scroll == null || _grid == null || _gridLayout == null) return;
            _scroll.StopMovement();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_grid);     // the cells were just changed

            string picked = SkinRef.Worn(_draft, Slots[_tab]);
            int index = -1;
            for (int i = 0; i < _cells.Count && index < 0; i++)
                if (_cells[i].gameObject.activeSelf && _cells[i].IsNew) index = i;
            for (int i = 0; i < _cells.Count && index < 0; i++)
                if (_cells[i].gameObject.activeSelf && _cells[i].Item == picked && picked != "") index = i;

            float view = ((RectTransform)_scroll.transform).rect.height;
            float room = Mathf.Max(0f, _grid.rect.height - view);   // how far the list can scroll
            float y = 0f;
            if (index >= 0) {
                float cell = _gridLayout.cellSize.y + _gridLayout.spacing.y;
                int row = index / Mathf.Max(1, _gridLayout.constraintCount);
                y = Mathf.Clamp(_gridLayout.padding.top + row * cell - (view - cell) * 0.5f, 0f, room);
            }
            _grid.anchoredPosition = new Vector2(_grid.anchoredPosition.x, y);
        }

        // One cell per item of the open tab. Cells are kept and reused: a tab with fewer items
        // just leaves the spare ones off.
        void _fill() {
            if (_grid == null || _template == null) return;
            string slotId = Slots[_tab];
            var slot = SkinCatalog.Instance != null ? SkinCatalog.Instance.Slot(slotId) : null;
            int count = slot != null && slot.items != null ? slot.items.Length : 0;
            float scale = _tab < _iconScale.Length ? _iconScale[_tab] : 1f;
            if (_gridLayout != null) {
                if (_tab < _columns.Length) _gridLayout.constraintCount = _columns[_tab];
                if (_tab < _cellSize.Length) _gridLayout.cellSize = _cellSize[_tab];
            }

            // What the player has comes first, then what is locked - items open in no fixed order,
            // and theirs should not be scattered between locks. Within each group, catalog order.
            _order.Clear();
            for (int i = 0; i < count; i++) if (ProfileManager.Owns(slotId, slot.items[i].id)) _order.Add(i);
            for (int i = 0; i < count; i++) if (!ProfileManager.Owns(slotId, slot.items[i].id)) _order.Add(i);

            for (int i = 0; i < count; i++) {
                if (i == _cells.Count) {
                    var cell = Instantiate(_template, _grid);
                    cell.name = "Cell" + i;
                    cell.Tapped += _onCell;
                    _cells.Add(cell);
                }
                var item = slot.items[_order[i]];
                _cells[i].gameObject.SetActive(true);
                _cells[i].Set(slotId, item, ProfileManager.Owns(slotId, item.id), SkinRef.Worn(_draft, slotId) == item.id, scale,
                              ProfileManager.IsNew(slotId, item.id));
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
            // The name first: if it is refused nothing is saved and the popup stays, with the
            // reason under the field. ProfileManager re-checks it — this side of the glass can
            // always be lied to.
            if (_nameInput != null) {
                string typed = (_nameInput.text ?? "").Trim();
                if (typed != ProfileManager.Name) {
                    var error = ProfileManager.SetName(typed);
                    if (error != ENameError.Ok) { _showNameError(error); return; }
                }
            }

            foreach (string slot in Slots) ProfileManager.Equip(slot, SkinRef.Worn(_draft, slot));
            Close();
        }

        void _showNameError(ENameError error) {
            if (_nameError != null) _nameError.text = ProfileManager.NameErrorText(error);
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
