using System.Collections;
using Common;
using Puzzby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Drives the lobby's daily-streak button — pure display over <see cref="DailyStreakManager"/>.
    /// Exactly one child root is active for the current status:
    ///   $DailyButtonActive     Status == Active  — streak count + progress
    ///   $DailyButtonOffline    Status == Offline — no trusted server time
    ///   $DailyButtonBroken     Status == Broken  — the LOST streak, still savable; its own
    ///                          $CurrentDay (and bar, if it has one). Tapping it opens the repair
    ///                          offer: watched, the streak is saved on the spot; "No thanks", it is
    ///                          gone, as it would be after a win.
    ///
    /// The bar itself is <see cref="MBDailyStreakProgress"/> (a reusable widget); this only picks the
    /// status root, sets $CurrentDay, and pushes the streak into the widget. It ticks slowly so a
    /// status flip (server time syncing / net drop) while the lobby sits open just works.
    /// </summary>
    public class MBDailyStreakBtn : MonoBehaviour {

        // "Days" rides in the same label as the number, so a 3-digit streak pushes it along instead
        // of running into it. The font tag loads from Resources/Fonts & Materials — keep it there.
        const string DaysSuffix = "<space=7><font=\"Mitr-SemiBold SDF\"><size=52>Days";

        GameObject _active, _offline, _broken;
        TMP_Text _currentDay;                 // the 🔥 streak number + "Days"
        MBDailyStreakProgress _progress;      // the bar widget under $DailyButtonActive
        TMP_Text _brokenDay;                  // the lost streak, on $DailyButtonBroken
        MBDailyStreakProgress _brokenProgress;   // its bar, if the Broken face has one
        MBDailyStreakInfoPopup _infoPopup;    // opened on tap (found lazily)
        MBWiggle _wifiWiggle;                 // on $IconWifi — rocks when the offline button is tapped
        MBDailyStreakRepairPopup _repairPopup;   // opened by the Broken face (found lazily)

        // What the UI currently shows. Sentinels so the first tick always paints.
        EStreakStatus _shownStatus = (EStreakStatus)(-1);
        int _shownStreak = -1;

        void Awake() {
            var activeT = transform.RecursiveFindChild("$DailyButtonActive");
            var offlineT = transform.RecursiveFindChild("$DailyButtonOffline");
            _active = activeT != null ? activeT.gameObject : null;
            _offline = offlineT != null ? offlineT.gameObject : null;

            var brokenT = transform.RecursiveFindChild("$DailyButtonBroken");

            // The Broken face has a $CurrentDay and maybe a bar of its own, so the Active ones are
            // looked up away from it: the number under $DailyButtonActive, the bar as before — the
            // first one in the button — but never the Broken face's.
            _currentDay = activeT != null ? activeT.RecursiveFindChild<TMP_Text>("$CurrentDay") : null;
            foreach (var p in GetComponentsInChildren<MBDailyStreakProgress>(true))
                if (brokenT == null || !p.transform.IsChildOf(brokenT)) { _progress = p; break; }

            if (brokenT != null) {
                _broken = brokenT.gameObject;
                _brokenDay = brokenT.RecursiveFindChild<TMP_Text>("$CurrentDay");
                _brokenProgress = brokenT.GetComponentInChildren<MBDailyStreakProgress>(true);
                var brokenBtn = brokenT.GetComponent<Button>();
                if (brokenBtn != null) brokenBtn.onClick.AddListener(_openRepair);
            }

            // Tapping the active button opens the streak info popup (current streak, entrance only).
            var btn = activeT != null ? activeT.GetComponent<Button>() : null;
            if (btn != null) btn.onClick.AddListener(_openInfo);

            // Offline has nothing to open, but a dead tap feels broken: the wifi icon rocks instead,
            // pointing at the connection as the reason.
            _wifiWiggle = MBWiggle.On(offlineT != null ? offlineT.RecursiveFindChild("$IconWifi") : null);
            var offlineBtn = offlineT != null ? offlineT.GetComponent<Button>() : null;
            if (offlineBtn != null) offlineBtn.onClick.AddListener(_wiggleWifi);
        }

        void _wiggleWifi() {
            Haptics.Play(GameHaptic.Tap);
            if (_wifiWiggle != null) _wifiWiggle.Play();
        }

        void _openRepair() {
            if (_repairPopup == null)
                _repairPopup = FindAnyObjectByType<MBDailyStreakRepairPopup>(FindObjectsInactive.Include);
            if (_repairPopup == null || _repairPopup.IsShowing || !DailyStreakManager.CanRepair) return;
            StartCoroutine(_repairFlow());
        }

        // The offer, then the answer: watched — saved, back to Active on the same number; "No thanks"
        // — gone, Active at 0.
        IEnumerator _repairFlow() {
            _repairPopup.Show(DailyStreakManager.LostStreak, fromLobby: true);
            yield return new WaitWhile(() => _repairPopup.IsShowing);
            if (_repairPopup.Result == ERepairResult.Watched) DailyStreakManager.RepairFromLobby();
            else DailyStreakManager.DeclineFromLobby();
            _apply();
        }

        void _openInfo() {
            if (_infoPopup == null)
                _infoPopup = FindAnyObjectByType<MBDailyStreakInfoPopup>(FindObjectsInactive.Include);
            if (_infoPopup != null) _infoPopup.Show(DailyStreakManager.Streak);
        }

        void OnEnable() {
            _apply();
            InvokeRepeating(nameof(_apply), 1f, 1f);   // status can flip while the lobby sits open (net drop/return)
        }

        void OnDisable() => CancelInvoke(nameof(_apply));

        // Runs each second, so a server-midnight that breaks the streak while the lobby sits open
        // shows at once; repaints only when the status or streak actually changed, not every tick.
        void _apply() {
            var status = DailyStreakManager.Status;
            int streak = status == EStreakStatus.Broken ? DailyStreakManager.LostStreak : DailyStreakManager.Streak;
            if (status == _shownStatus && streak == _shownStreak) return;
            _shownStatus = status;
            _shownStreak = streak;

            if (_active != null) _active.SetActive(status == EStreakStatus.Active);
            if (_offline != null) _offline.SetActive(status == EStreakStatus.Offline);
            if (_broken != null) _broken.SetActive(status == EStreakStatus.Broken);

            if (status == EStreakStatus.Active) {
                if (_currentDay != null) _currentDay.text = streak + DaysSuffix;
                if (_progress != null) _progress.ApplyImmediately(streak);
            }
            else if (status == EStreakStatus.Broken) {
                if (_brokenDay != null) _brokenDay.text = streak + DaysSuffix;
                if (_brokenProgress != null) _brokenProgress.ApplyImmediately(streak);
            }
        }
    }
}
