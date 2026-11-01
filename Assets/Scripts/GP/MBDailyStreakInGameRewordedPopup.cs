using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The in-game streak REWARD popup — shown after a win that reached a milestone (a reward was
    /// granted), before the win popup. (The plain variant is <see cref="MBDailyStreakInfoPopup"/>.)
    /// Caller does <c>Show(old, new)</c> then waits on <see cref="IsClaimed"/>; the $%ContinueButton
    /// (Claim) marks it claimed, the caller opens the gift popup from <see cref="GiftBox"/> and
    /// calls <see cref="HandOff"/> — the box goes on that frame and this popup fades away.
    /// </summary>
    public class MBDailyStreakInGameRewordedPopup : MonoBehaviour {

        // The streak number container starts scrolled up by this (old in view) and slides to 0 (new).
        const float ScrollFrom = -158f;
        const float FadeOut = 0.35f;   // crossfades with the gift popup's cover fading in

        CanvasGroup _group;
        bool _showing;              // a real show is on screen (guards the layout pass from hiding it)
        bool _claimed;

        TMP_Text _streakOld, _streakNew;                 // $StreakOld / $StreakNew
        RectTransform _streakScroll;                     // $StreakContainer — their shared parent
        MBDailyStreakProgress _progress;                 // the bar widget in %DailyStreakProgressContainer
        Transform _giftBox;                              // %GiftBox — holds the Gifts/Boxes art
        Button _claim;                                   // $%ContinueButton

        MBDailyStreakInGameRewordedPopupAnim _anim;      // reskin animator driver (Show state)

        /// <summary>True while the popup is open — the win flow waits on this before continuing.</summary>
        public bool IsShowing => _showing;

        /// <summary>True once Claim was pressed, until the next Show.</summary>
        public bool IsClaimed => _claimed;

        /// <summary>The box art the gift popup flies in from, or null.</summary>
        public RectTransform GiftBox =>
            _giftBox != null && _giftBox.childCount > 0 ? _giftBox.GetChild(0) as RectTransform : null;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakScroll = transform.RecursiveFindChild<RectTransform>("$StreakContainer");
            _streakOld = _streakScroll.RecursiveFindChild<TMP_Text>("$StreakOld");
            _streakNew = _streakScroll.RecursiveFindChild<TMP_Text>("$StreakNew");
            _progress = GetComponentInChildren<MBDailyStreakProgress>(true);

            _anim = GetComponent<MBDailyStreakInGameRewordedPopupAnim>();
            _giftBox = transform.RecursiveFindChild("%GiftBox");

            // Continue button carries both markers: $ (found from code) and % (used by the animator).
            _claim = transform.RecursiveFindChild<Button>("$%ContinueButton");
            if (_claim != null) _claim.onClick.AddListener(_onClaim);
        }

        // Same trick as the win popup: stay ACTIVE but invisible for the first frames so the UI lays
        // out at real size, then hide until Show().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);
        }

        public void Show(int oldStreak, int newStreak) {
            _showing = true;
            _claimed = false;
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            gameObject.SetActive(true);
            if (_giftBox != null) _giftBox.gameObject.SetActive(true);   // HandOff hid it last time
            if (_claim != null) _claim.interactable = true;

            _streakOld.text = oldStreak.ToString();
            _streakNew.text = newStreak.ToString();
            // start with the old number in view + the old streak's checks already on the bar
            _streakScroll.anchoredPosition = new Vector2(_streakScroll.anchoredPosition.x, ScrollFrom);
            if (_progress != null) _progress.ApplyImmediately(oldStreak);

            StartCoroutine(_showFlow(newStreak));
        }

        // Promise-style sequence: entrance → stamp the completing check → scroll old→new. The gift
        // itself opens in the gift popup once Claim hands it over.
        IEnumerator _showFlow(int newStreak) {
            _anim?.PlayIn();
            if (_anim != null) yield return new WaitUntil(() => _anim.IsFinished());

            if (_progress != null) yield return _progress.PlayStamp(newStreak);
            yield return _scrollY(_streakScroll, ScrollFrom, 0f, 1f);
        }

        // Slide the container's anchored Y from -> to (ease-out), revealing $StreakNew.
        IEnumerator _scrollY(RectTransform rt, float fromY, float toY, float dur) {
            float x = rt.anchoredPosition.x;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime) {
                float p = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - p) * (1f - p);   // ease-out
                rt.anchoredPosition = new Vector2(x, Mathf.Lerp(fromY, toY, e));
                yield return null;
            }
            rt.anchoredPosition = new Vector2(x, toY);
        }

        void _onClaim() {
            if (_claimed) return;
            _claimed = true;
            if (_claim != null) _claim.interactable = false;
        }

        /// <summary>The gift popup has taken over: our box goes on this frame (the popup's box is
        /// now exactly over it) and the popup fades away, letting taps through to the gift.</summary>
        public void HandOff() {
            if (!gameObject.activeInHierarchy) { _showing = false; return; }
            if (_giftBox != null) _giftBox.gameObject.SetActive(false);
            _group.blocksRaycasts = false;
            StartCoroutine(_fadeAway());
        }

        IEnumerator _fadeAway() {
            for (float t = 0f; t < FadeOut; t += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.Clamp01(t / FadeOut);
                yield return null;
            }
            _group.alpha = 0f;
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
