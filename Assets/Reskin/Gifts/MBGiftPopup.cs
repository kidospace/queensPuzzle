using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace qp {

    /// <summary>
    /// The gift popup's one entry point: choose the box art, add rewards, play the reveal.
    /// Lives in the scene like the other popups: its owner wakes it in Awake, it lays out
    /// invisible for two frames and hides itself, so Show() is instant and blocks the UI at once.
    /// Game code opens it through GiftsManager.ShowGift(popup, EGiftBox, rewards); this class only
    /// knows prefabs: ClearRewards() / AddReward() (fill each returned instance), SetBox(), Show().
    /// Wait on IsShowing or Claimed — a tap once the reveal is done claims and closes.
    ///
    /// A gift that holds something the player can put on right away (OfferUse) does not close on
    /// that tap: the cards that can be used come together in the middle, the rest go, and two
    /// buttons ask — Use or Later. Either one closes the popup; Use puts them all on first.
    /// The Debug fields are for the editor only: the inspector's Apply puts that gift in through
    /// GiftsManager (never saved into the prefab or scene), ▶ Reveal plays it.
    /// </summary>
    public class MBGiftPopup : MonoBehaviour, IPointerClickHandler {

        [Header("Debug (editor only)")]
        public EGiftBox DebugBox;
        public BoostReward[] DebugRewards;
        [Tooltip("Skins to preview next to them. Slot: frame, name, badge. Item: an id from the " +
                 "catalog (premiumframe2, gold, badge5) - leave empty for the first one that has " +
                 "to be earned. Nothing is given to the player: this only fills the cards.")]
        public DebugSkin[] DebugSkins;

        /// <summary>One skin for the editor preview: which slot, and which item of it.</summary>
        [Serializable]
        public class DebugSkin {
            public string slot = "frame";
            public string item = "";
        }

        MBGiftBox _box;
        MBGiftRewards _rewards;
        Animator _anim;
        CanvasGroup _group;
        bool _showing;

        /// <summary>Seconds the usable cards take to come together, and the buttons to appear.</summary>
        const float GatherSeconds = 0.35f;

        readonly List<Action> _uses = new List<Action>();   // one per usable card: puts it on
        CanvasGroup _choice;                                // $Choice: the two buttons
        GameObject _tapHint;                                // $TapToClaim: gives way to them
        bool _asking;                                       // the question is on screen (or on its way)

        public MBGiftBox Box { get { _find(); return _box; } }
        public MBGiftRewards Rewards { get { _find(); return _rewards; } }
        public Animator Anim { get { _find(); return _anim; } }

        /// <summary>True from Show() until the player taps to claim.</summary>
        public bool IsShowing => _showing;

        /// <summary>Fired when the player taps to claim, as the popup closes.</summary>
        public event Action Claimed;

        void Awake() {
            _find();
            var tap = _findDeep(transform, "$TapToClaim");
            if (tap != null) _tapHint = tap.gameObject;
            var choice = _findDeep(transform, "$Choice");
            if (choice != null) {
                _choice = choice.GetComponent<CanvasGroup>();
                if (_choice == null) _choice = choice.gameObject.AddComponent<CanvasGroup>();
                var use = _findDeep(choice, "$UseButton")?.GetComponent<Button>();
                var later = _findDeep(choice, "$LaterButton")?.GetComponent<Button>();
                if (use != null) use.onClick.AddListener(() => _answer(true));
                if (later != null) later.onClick.AddListener(() => _answer(false));
                choice.gameObject.SetActive(false);
            }
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass
        }

        // Same trick as the other popups: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size, then hide until Show().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);
        }

        /// <summary>Open with this box art and play the reveal; add the rewards before.</summary>
        public void Show(GameObject boxPrefab) {
            SetBox(boxPrefab);
            Show();
        }

        /// <summary>Open with the box and rewards already in place and play the reveal. With
        /// <paramref name="from"/> (the feature screen's own Box prefab instance) the box first
        /// flies in from there while the black cover fades in; the caller hides its box on the
        /// same frame.</summary>
        public void Show(RectTransform from = null) {
            _showing = true;
            _asking = false;
            _group.alpha = 1f;
            gameObject.SetActive(true);
            if (_choice != null) _choice.gameObject.SetActive(false);
            if (_tapHint != null) _tapHint.SetActive(true);
            Rewards.Gather = 0f;
            Anim.Rebind();          // start from Idle, not from the last show's held end state
            var art = Box.transform.Find("$BoxSlot");
            if (from != null && art != null && art.childCount > 0) {
                Box.GetComponent<MBGiftArrive>().Begin(from, art.GetChild(0));
                Anim.SetTrigger("Arrive");
            } else {
                Anim.SetTrigger("Reveal");
            }
        }

        /// <summary>Close without the tap (the tap calls this too, then fires Claimed).</summary>
        public void Close() {
            _showing = false;
            gameObject.SetActive(false);
        }

        // the black cover catches every tap while open; only a finished reveal claims
        public void OnPointerClick(PointerEventData e) {
            if (!_showing || _asking || !Anim.GetCurrentAnimatorStateInfo(0).IsName("Claimable")) return;
            // Something to put on, and buttons to ask with: ask. Otherwise it closes as it always did.
            if (_uses.Count > 0 && _choice != null) { _asking = true; StartCoroutine(_ask()); return; }
            Close();
            Claimed?.Invoke();
        }

        IEnumerator _ask() {
            if (_tapHint != null) _tapHint.SetActive(false);     // the tap has been made; the buttons take its place
            _choice.alpha = 0f;
            _choice.interactable = false;
            _choice.gameObject.SetActive(true);
            for (float e = 0f; e < GatherSeconds; e += Time.unscaledDeltaTime) {
                float k = e / GatherSeconds;
                Rewards.Gather = k;
                _choice.alpha = Mathf.SmoothStep(0f, 1f, k);
                yield return null;
            }
            Rewards.Gather = 1f;
            _choice.alpha = 1f;
            _choice.interactable = true;     // not before: a tap still landing must not answer
        }

        void _answer(bool use) {
            if (!_showing || !_asking) return;
            if (use) foreach (var put in _uses) put?.Invoke();
            Close();
            Claimed?.Invoke();
        }

        public void SetBox(GameObject boxPrefab) {
            Box.SetBox(boxPrefab);
        }

        public void ClearRewards() {
            Rewards.Clear();
            _uses.Clear();
        }

        /// <summary>This card is something the player can put on right away; <paramref name="use"/>
        /// puts it on. A gift with one asks Use / Later instead of closing on the first tap.</summary>
        public void OfferUse(GameObject card, Action use) {
            if (card == null || use == null) return;
            Rewards.Keep(card.transform);
            _uses.Add(use);
        }

        /// <summary>Instantiate a reward into a row (0 = regular, 1 = NEW ...) and return it to fill.</summary>
        public GameObject AddReward(GameObject prefab, int row = 0) {
            return Rewards.Add(prefab, row);
        }

        public void Reveal() {
            Anim.SetTrigger("Reveal");
        }

        /// <summary>Animation event (GiftReveal.anim, as the box bursts into light). The same
        /// sound every gift has opened on, whichever feature hands it out.</summary>
        public void PlayOpenSFX() {
            var sfx = CommonSFX.Instance?.RewordOpen;
            if (sfx != null) CommonSFX.Play(sfx);
        }

        void _find() {
            if (_box != null) return;
            _box = _findDeep(transform, "$Box").GetComponent<MBGiftBox>();
            _rewards = _findDeep(transform, "$Rewards").GetComponent<MBGiftRewards>();
            _anim = GetComponent<Animator>();
        }

        static Transform _findDeep(Transform t, string name) {
            if (t.name == name) return t;
            foreach (Transform c in t) {
                var f = _findDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
