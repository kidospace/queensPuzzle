using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The streak repair offer — shown after the first win of a day on which a streak lost to one
    /// missed day can still be saved (<see cref="DailyStreakManager.CanRepair"/>), before the
    /// regular streak popup; and in the lobby when the Broken card is tapped. Watch a rewarded video and the streak carries on; decline and it
    /// starts over. Caller does <c>Show(lostStreak)</c>, waits on <see cref="IsShowing"/>, then
    /// reads <see cref="Result"/>. No success animation of its own: the regular streak popup that
    /// follows (12 → 13) is the payoff.
    ///
    /// The video is meant to be SEEN: this is help the player chooses, not an ad slipped in, so
    /// the button says plainly that it plays a video.
    ///
    ///   $StreakNumber        the lost streak (optional)
    ///   $BtnWatch            the rewarded video — its children show its state:
    ///     $ShowVideo           ready, clickable
    ///     $VideoLoading        online, video still loading
    ///     $Offline             no connection
    ///   $BtnNoThanks         decline
    ///   MBDailyStreakProgress  the 7-day bar at the lost streak (optional)
    /// </summary>
    public class MBDailyStreakRepairPopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;          // a real show is on screen (guards the layout pass from hiding it)
        bool _watching;         // a video is playing — a second tap must not start another

        TMP_Text _streakNumber;
        Button _watch;
        GameObject _showVideo, _videoLoading, _offline;
        MBDailyStreakProgress _progress;
        bool _hasLast, _lastReady, _lastOffline;   // last applied button state — redraw only on change

        /// <summary>True while the popup is open — the win flow waits on this.</summary>
        public bool IsShowing => _showing;

        /// <summary>After it closes: Watched (save the streak) or Declined.</summary>
        public ERepairResult Result { get; private set; }

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;      // invisible, but alive for the layout pass

            _streakNumber = transform.RecursiveFindChild<TMP_Text>("$StreakNumber");
            _progress = GetComponentInChildren<MBDailyStreakProgress>(true);

            _watch = transform.RecursiveFindChild<Button>("$BtnWatch");
            if (_watch != null) {
                _watch.onClick.AddListener(_onWatch);
                _showVideo = _watch.transform.RecursiveFindChild("$ShowVideo")?.gameObject;
                _videoLoading = _watch.transform.RecursiveFindChild("$VideoLoading")?.gameObject;
                _offline = _watch.transform.RecursiveFindChild("$Offline")?.gameObject;
                // interactable=false only blocks clicks — no gray-out (same as the lose popup)
                var colors = _watch.colors;
                colors.disabledColor = colors.normalColor;
                _watch.colors = colors;
            }
            else CDebug.LogError("[MBDailyStreakRepairPopup] $BtnWatch missing");

            var no = transform.RecursiveFindChild<Button>("$BtnNoThanks");
            if (no != null) no.onClick.AddListener(_onNoThanks);
            else CDebug.LogError("[MBDailyStreakRepairPopup] $BtnNoThanks missing");
        }

        // Same trick as the other popups: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size, then hide until Show().
        IEnumerator Start() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);
        }

        /// <summary>Opened from the lobby's Broken card rather than after a win — only reported.</summary>
        bool _fromLobby;

        public void Show(int lostStreak, bool fromLobby = false) {
            _fromLobby = fromLobby;
            _showing = true;
            _watching = false;
            Result = ERepairResult.Declined;
            _hasLast = false;   // fresh show — apply the button state even if it did not change
            _group.alpha = 1f;
            gameObject.SetActive(true);

            if (_streakNumber != null) _streakNumber.text = lostStreak.ToString();
            if (_progress != null) _progress.ApplyImmediately(lostStreak);
            _updateWatch();

            DailyStreakManager.NoteRepairOffered(Ads.IsRewardedReady, _fromLobby);
            GetComponent<IPopupAnim>()?.PlayIn();
        }

        void Update() {
            if (_showing) _updateWatch();
        }

        // Ready → $ShowVideo; else offline → $Offline; else (online, still loading) → $VideoLoading.
        // Only $ShowVideo is clickable.
        void _updateWatch() {
            if (_watch == null) return;
            bool ready = Ads.IsRewardedReady;
            bool offline = !ready && InternetConnection.Instance != null && !InternetConnection.Instance.HasInternet;
            if (_hasLast && ready == _lastReady && offline == _lastOffline) return;
            _hasLast = true;
            _lastReady = ready;
            _lastOffline = offline;

            if (_showVideo != null) _showVideo.SetActive(ready);
            if (_offline != null) _offline.SetActive(offline);
            if (_videoLoading != null) _videoLoading.SetActive(!ready && !offline);
            _watch.interactable = ready;
        }

        void _onWatch() {
            if (_watching) return;
            _watching = true;
            Ads.ShowRewarded("streak_repair", earned => {
                _watching = false;
                if (earned) { Result = ERepairResult.Watched; _close(); }
                else DailyStreakManager.NoteRepairAdFailed(_fromLobby);   // stays open: try again or decline
            });
        }

        void _onNoThanks() {
            if (_watching) return;
            Result = ERepairResult.Declined;
            _close();
        }

        void _close() {
            _showing = false;
            gameObject.SetActive(false);
        }
    }
}
