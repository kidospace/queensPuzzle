using Common;
using Puzzby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// Drives the lobby's three daily cards — pure display over <see cref="DailyChallengeManager"/>.
    /// Attach to ButtonsContainer (or any parent of the cards). Below
    /// <see cref="DailyChallengeManager.IsRevealed"/> none is active; otherwise exactly one:
    ///   $DailyButtonLock            Status == Lock (nothing to fill)
    ///   $DailyButtonActive          Status == Active — $Date, $TimeLeft (countdown to next UTC day)
    ///   $DailyButtonDone            Status == Done — $Date, $SolvedTime, $TopPct
    /// Ticks once per second so the countdown runs and a midnight/status flip while the lobby
    /// sits open just works. Play click starts the daily run; the Done card only logs.
    /// </summary>
    public class MBDailyChallengeButtons : MonoBehaviour {

        GameObject _lock, _active, _done;
        TMP_Text _date, _timeLeft;                       // under $DailyButtonActive
        TMP_Text _doneDate, _solvedTime, _topPct;        // under $DailyButtonDone

        EDailyChallengeStatus _shownStatus;   // what Refresh last built — Tick refreshes only on change
        int _shownDay = -1;                   // day Refresh last painted — midnight repaints even if status didn't flip
        bool _shownRevealed;                  // reveal Refresh last painted — a level jump (debug) repaints

        void Awake() {
            var lockT = transform.RecursiveFindChild("$DailyButtonLock");
            var activeT = transform.RecursiveFindChild("$DailyButtonActive");
            var doneT = transform.RecursiveFindChild("$DailyButtonDone");
            _lock = lockT != null ? lockT.gameObject : null;
            _active = activeT != null ? activeT.gameObject : null;
            _done = doneT != null ? doneT.gameObject : null;

            // Art for an offline card exists but nothing drives it yet — keep it off so it never
            // shows on top of (or instead of) the real card.
            var offlineT = transform.RecursiveFindChild("$DailyButtonOffline");
            if (offlineT != null) offlineT.gameObject.SetActive(false);

            if (activeT != null) {   // $Date exists on BOTH cards — searches stay scoped per card
                _date = activeT.RecursiveFindChild<TMP_Text>("$Date");
                _timeLeft = activeT.RecursiveFindChild<TMP_Text>("$TimeLeft");
                Tap(activeT, "$DailyButtonActive", OnPlayClick);
            }
            if (lockT != null) {
                // Locked has nothing to open, but a dead tap feels broken: the lock rocks instead.
                var lockWiggle = MBWiggle.On(lockT.RecursiveFindChild("$Lock"));
                Tap(lockT, "$DailyButtonLock", () => {
                    Haptics.Play(GameHaptic.Tap);
                    if (lockWiggle != null) lockWiggle.Play();
                });
            }
            if (doneT != null) {
                _doneDate = doneT.RecursiveFindChild<TMP_Text>("$Date");
                _solvedTime = doneT.RecursiveFindChild<TMP_Text>("$SolvedTime");
                _topPct = doneT.RecursiveFindChild<TMP_Text>("$TopPct");
                // The done card is not a button on purpose: there is nothing to go to once
                // today's challenge is solved. It only reports.
            }
        }

        /// <summary>
        /// Subscribe to a card, and say so when it turns out not to be a button.
        ///
        /// Reached straight off GetComponent before, so a card that lost its Button threw in the
        /// middle of Awake — which costs far more than the click: everything below the throw
        /// never runs, and the component carries on half built with no sign of why.
        /// </summary>
        void Tap(Transform card, string name, UnityEngine.Events.UnityAction onClick) {
            var button = card.GetComponent<Button>();
            if (button == null) { Debug.LogError($"[DailyChallenge] {name} has no Button — it cannot be pressed"); return; }
            button.onClick.AddListener(onClick);
        }

        void OnEnable() {
            Refresh();
            InvokeRepeating(nameof(Tick), 1f, 1f);
        }

        void OnDisable() => CancelInvoke(nameof(Tick));

        // 1/sec: only the countdown string changes — a full Refresh only when the status flips
        // (Done after a solve) or the day does (midnight, even Active→Active: new date, new puzzle).
        // The day check is skipped while Lock — DayIndex needs the tiers config.
        void Tick() {
            var status = DailyChallengeManager.Status;
            bool dayChanged = status != EDailyChallengeStatus.Lock && DailyChallengeManager.DayIndex != _shownDay;
            if (status != _shownStatus || dayChanged || DailyChallengeManager.IsRevealed != _shownRevealed) { Refresh(); return; }
            if (_shownStatus == EDailyChallengeStatus.Active && _timeLeft != null)
                _timeLeft.text = DailyChallengeManager.TimeLeft.ToString(@"hh\:mm\:ss");
        }

        void Refresh() {
            var status = _shownStatus = DailyChallengeManager.Status;
            if (status != EDailyChallengeStatus.Lock) _shownDay = DailyChallengeManager.DayIndex;
            bool revealed = _shownRevealed = DailyChallengeManager.IsRevealed;   // below it no card at all
            if (_lock != null) _lock.SetActive(revealed && status == EDailyChallengeStatus.Lock);
            if (_active != null) _active.SetActive(revealed && status == EDailyChallengeStatus.Active);
            if (_done != null) _done.SetActive(revealed && status == EDailyChallengeStatus.Done);

            switch (status) {
                case EDailyChallengeStatus.Active:
                    if (_date != null) _date.text = DailyChallengeManager.NiceDate;
                    if (_timeLeft != null) _timeLeft.text = DailyChallengeManager.TimeLeft.ToString(@"hh\:mm\:ss");
                    break;
                case EDailyChallengeStatus.Done:
                    if (_doneDate != null) _doneDate.text = DailyChallengeManager.NiceDate;
                    if (_solvedTime != null) _solvedTime.text = DailyChallengeManager.FormatTime(DailyChallengeManager.State.timeSec);
                    if (_topPct != null) _topPct.text = $"{DailyChallengeManager.State.topPct}%";
                    break;
            }
        }

        // Play today's daily: lock the day's tier, flag the run, and enter gameplay —
        // LevelLoader serves the daily pack while InDailyRun is up.
        void OnPlayClick() {
            Debug.Log($"[DailyChallenge] play — day {DailyChallengeManager.DayIndex}, " +
                      $"tier {DailyChallengeManager.Tier} ({DailyChallengeManager.TierName}), " +
                      $"timeSec {DailyChallengeManager.State.timeSec:0}");
            DailyChallengeManager.StartDaily();
#if UNITY_EDITOR
            // the daily launches by day/tier — drop any Level Builder playtest token so it isn't reused
            UnityEditor.SessionState.EraseString(LevelLoader.PlayLevelGuidKey);
#endif
            Navigator.Go(Navigator.Gameplay);
        }

    }
}
