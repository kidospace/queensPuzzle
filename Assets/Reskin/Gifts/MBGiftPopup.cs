using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace qp {

    /// <summary>
    /// The gift popup's one entry point: choose the box art, add rewards, play the reveal.
    /// Lives in the scene like the other popups: its owner wakes it in Awake, it lays out
    /// invisible for two frames and hides itself, so Show() is instant and blocks the UI at once.
    /// Game code opens it through GiftsManager.ShowGift(popup, EGiftBox, rewards); this class only
    /// knows prefabs: ClearRewards() / AddReward() (fill each returned instance), SetBox(), Show().
    /// Wait on IsShowing or Claimed — a tap once the reveal is done claims and closes.
    /// The Debug fields are for the editor only: the inspector's Apply puts that gift in through
    /// GiftsManager (never saved into the prefab or scene), ▶ Reveal plays it.
    /// </summary>
    public class MBGiftPopup : MonoBehaviour, IPointerClickHandler {

        [Header("Debug (editor only)")]
        public EGiftBox DebugBox;
        public BoostReward[] DebugRewards;

        MBGiftBox _box;
        MBGiftRewards _rewards;
        Animator _anim;
        CanvasGroup _group;
        bool _showing;

        public MBGiftBox Box { get { _find(); return _box; } }
        public MBGiftRewards Rewards { get { _find(); return _rewards; } }
        public Animator Anim { get { _find(); return _anim; } }

        /// <summary>True from Show() until the player taps to claim.</summary>
        public bool IsShowing => _showing;

        /// <summary>Fired when the player taps to claim, as the popup closes.</summary>
        public event Action Claimed;

        void Awake() {
            _find();
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
            _group.alpha = 1f;
            gameObject.SetActive(true);
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
            if (!_showing || !Anim.GetCurrentAnimatorStateInfo(0).IsName("Claimable")) return;
            Close();
            Claimed?.Invoke();
        }

        public void SetBox(GameObject boxPrefab) {
            Box.SetBox(boxPrefab);
        }

        public void ClearRewards() {
            Rewards.Clear();
        }

        /// <summary>Instantiate a reward into a row (0 = regular, 1 = NEW ...) and return it to fill.</summary>
        public GameObject AddReward(GameObject prefab, int row = 0) {
            return Rewards.Add(prefab, row);
        }

        public void Reveal() {
            Anim.SetTrigger("Reveal");
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
