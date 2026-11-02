using System.Collections;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// "Connect to count today" — shown after an offline win that would have counted toward the
    /// streak (<see cref="DailyStreakManager.AskToConnect"/>), before the regular streak popups.
    /// It closes by itself the moment the line and the clock are back, and the win flow then counts
    /// the win; "Not now" leaves this win uncounted. Caller does <c>Show()</c> and waits on
    /// <see cref="IsShowing"/>.
    ///
    ///   $BtnNotNow           dismiss
    /// </summary>
    public class MBDailyStreakOfflinePopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;          // a real show is on screen (guards the layout pass from hiding it)

        /// <summary>True while the popup is open — the win flow waits on this.</summary>
        public bool IsShowing => _showing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

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

        public void Show() {
            _showing = true;
            _group.alpha = 1f;
            gameObject.SetActive(true);
            GetComponent<IPopupAnim>()?.PlayIn();
        }

        void Update() {
            if (_showing && DailyStreakManager.IsOnline) _close(connected: true);
        }

        void _close(bool connected) {
            if (!_showing) return;
            Analytics.StreakOffline(connected);
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
