using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// "Connect to count today" — shown after an offline win that would have counted toward the
    /// streak (<see cref="DailyStreakManager.AskToConnect"/>), before the regular streak popups.
    /// It closes by itself the moment the line and the clock are back, and the win flow then counts
    /// the win; "Not now" leaves this win uncounted. Caller does <c>Show(stake)</c> and waits on
    /// <see cref="IsShowing"/>.
    ///
    /// Two prefabs carry this: the plain one (nothing left to lose, its text is the prefab's) and
    /// the one with the streak on it, whose text says what connecting does for that streak.
    ///
    ///   $BtnNotNow           dismiss
    ///   $StreakNumber        the streak at stake (only on the streak prefab)
    ///   $%BodyText           the keep / save line (only on the streak prefab)
    /// </summary>
    public class MBDailyStreakOfflinePopup : MonoBehaviour {

        const string KeepText = "Connect to keep your\n{0}-day streak";
        const string SaveText = "Connect to save your\n{0}-day streak";

        CanvasGroup _group;
        bool _showing;          // a real show is on screen (guards the layout pass from hiding it)
        EOfflineStake _stake;   // what this show said, reported with the answer

        TMP_Text _streakNumber, _body;

        /// <summary>True while the popup is open — the win flow waits on this.</summary>
        public bool IsShowing => _showing;

        /// <summary>This is the prefab with the streak on it.</summary>
        public bool ShowsStreak => transform.RecursiveFindChild("$StreakNumber") != null;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakNumber = transform.RecursiveFindChild<TMP_Text>("$StreakNumber");
            _body = transform.RecursiveFindChild<TMP_Text>("$%BodyText");

            var notNow = transform.RecursiveFindChild<Button>("$BtnNotNow");
            if (notNow != null) notNow.onClick.AddListener(() => _close(connected: false));
            else CDebug.LogError("[MBDailyStreakOfflinePopup] $BtnNotNow missing");
        }

        // Same trick as the other popups: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size, then hide until Show().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);
        }

        public void Show(EOfflineStake stake) {
            _stake = stake;
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);

            int streak = DailyStreakManager.LastRun;
            if (_streakNumber != null) _streakNumber.text = streak.ToString();
            if (_body != null) _body.text = string.Format(stake == EOfflineStake.Save ? SaveText : KeepText, streak);

            GetComponent<IPopupAnim>()?.PlayIn();
        }

        void Update() {
            if (_showing && DailyStreakManager.IsOnline) _close(connected: true);
        }

        void _close(bool connected) {
            if (!_showing) return;
            Analytics.StreakOffline(connected, _stake);
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
