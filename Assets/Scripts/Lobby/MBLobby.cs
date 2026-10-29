using System.Collections;
using Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace qp {
    public class MBLobby : MonoBehaviour {

        /// <summary>How long the player gets to look at the lobby before a finished tournament
        /// lands on top of it. Landing on the first frame reads as the lobby never having been
        /// there at all.</summary>
        const float ResultDelay = 1f;

        MBSettingsPopup _settings;   // inactive in the scene by default (its OUT state)
        MBTournamentLeaderboard _leaderboard;
        MBTournamentEndNoClaim _endScreen;
        MBTournamentEndClaim _claimScreen;
        MBTournamentCard _card;

        private void Awake() {
            var lvlBtn = transform.RecursiveFindChild<MBLevelButton>("$LvlButton");
            if (lvlBtn != null) lvlBtn.GetButton().onClick.AddListener(PlayCurrentLevel);

            transform.RecursiveFindChild<Button>("$SettingsBtn").onClick.AddListener(OpenSettings);

            var streakProgressPopup = FindAnyObjectByType<MBDailyStreakInfoPopup>(FindObjectsInactive.Include);
            if (streakProgressPopup != null) streakProgressPopup.gameObject.SetActive(true);
            else Debug.LogError("[MBGameplay] MBDailyStreakInGamePopup missing in the scene");

            // The repair offer the Broken streak card opens — woken the same way, so it lays out at
            // real size before the first tap.
            var streakRepairPopup = FindAnyObjectByType<MBDailyStreakRepairPopup>(FindObjectsInactive.Include);
            if (streakRepairPopup != null) streakRepairPopup.gameObject.SetActive(true);
            else Debug.LogError("[MBLobby] MBDailyStreakRepairPopup missing in the scene");

            // Awake so it lays out at real size and hides itself, the same deal the streak popup
            // makes. A screen first measured while hidden comes up with zero-high rows.
            _leaderboard = FindAnyObjectByType<MBTournamentLeaderboard>(FindObjectsInactive.Include);
            if (_leaderboard != null) _leaderboard.gameObject.SetActive(true);
            else Debug.LogError("[MBLobby] MBTournamentLeaderboard missing in the scene");

            _endScreen = FindAnyObjectByType<MBTournamentEndNoClaim>(FindObjectsInactive.Include);
            if (_endScreen != null) _endScreen.gameObject.SetActive(true);
            else Debug.LogError("[MBLobby] MBTournamentEndNoClaim missing in the scene");

            _claimScreen = FindAnyObjectByType<MBTournamentEndClaim>(FindObjectsInactive.Include);
            if (_claimScreen != null) _claimScreen.gameObject.SetActive(true);
            else Debug.LogError("[MBLobby] MBTournamentEndClaim missing in the scene");

            _card = FindAnyObjectByType<MBTournamentCard>(FindObjectsInactive.Include);
            if (_card != null) {
                _card.Tapped += OnTournamentTapped;
                _card.PlayTapped += PlayCurrentLevel;
            }
            else Debug.LogError("[MBLobby] MBTournamentCard missing in the scene");
        }

        /// <summary>
        /// A prize waiting to be collected opens itself — but only here, at the moment the lobby
        /// does. A tournament that ends while the player is already sitting in the lobby turns the
        /// card into a Claim card and waits to be tapped; nothing drops on top of a screen
        /// somebody is using.
        ///
        /// A result with nothing to collect is never forced at all. It sits on the card for this
        /// visit, and the card spends it when it goes away.
        /// </summary>
        IEnumerator Start() {
            if (!TournamentManager.PrizeWaiting) yield break;

            // Locked, so that second is a look at the lobby and not a chance to open something
            // about to be covered. The screen's own full-screen cover takes over from here.
            var events = EventSystem.current;
            if (events != null) events.enabled = false;
            yield return new WaitForSecondsRealtime(ResultDelay);
            if (events != null) events.enabled = true;

            yield return ShowEnded();
        }

        // Open plays the in animation; the popup closes itself (X / BG tap → out animation).
        void OpenSettings() {
            if (_settings == null) {
                _settings = FindAnyObjectByType<MBSettingsPopup>(FindObjectsInactive.Include);
            }

            _settings.Open();
        }

        /// <summary>
        /// The card was tapped. Only a card with a table behind it opens one.
        ///
        /// A locked card is an explanation, not a door. Offline has no table worth showing — the
        /// last one may be hours old. And a player who has not joined has nothing of their own to
        /// look at, so on that card only the Play button does anything at all.
        /// </summary>
        void OnTournamentTapped(ETournamentStatus status) {
            if (TournamentManager.ShowsOffline) return;   // the table behind it may be hours old
            switch (status) {
                case ETournamentStatus.Active:
                case ETournamentStatus.EndingSoon:
                case ETournamentStatus.Calculating:
                    StartCoroutine(ShowLeaderboard());
                    break;

                case ETournamentStatus.Ended:
                    StartCoroutine(ShowEnded());
                    break;
            }
        }

        /// <summary>
        /// The result of a tournament that closed. Two screens, one question: a finish inside the
        /// paying places has something to collect, so it opens the claim screen; everyone else gets
        /// the plain result. The card asks <see cref="TournamentManager.ClosedWonPrize"/> the same
        /// question for its own face, so a card that promises a prize always opens the screen that
        /// gives one.
        ///
        /// Either screen marks the result as seen on the way through, so the card stops offering it.
        /// </summary>
        IEnumerator ShowEnded() {
            if (TournamentManager.ClosedWonPrize) {
                if (_claimScreen != null) yield return _claimScreen.Show();
            }
            else if (_endScreen != null) yield return _endScreen.Show();

            // The result has been seen, so this is no longer an Ended card. Drawn now rather than
            // on the card's next tick, which could leave the old one on screen for a second after
            // the screen over it is gone.
            if (_card != null) _card.Redraw();
        }

        IEnumerator ShowLeaderboard() {
            if (_leaderboard == null) yield break;

            var showing = _leaderboard.Show();
            yield return showing;

            // The one thing the leaderboard hands back: they want to play. Everything else it
            // dealt with itself.
            if (showing.Result == ETournamentScreenResult.Play) PlayCurrentLevel();
        }

        // Start the player's current level (AppData.LevelIdx). LevelLoader reads that index.
        void PlayCurrentLevel() {
            DailyChallengeManager.ExitDaily();   // campaign play — make sure no daily run is flagged
#if UNITY_EDITOR
            // the lobby launches by index — drop any Level Builder playtest token so it isn't reused
            UnityEditor.SessionState.EraseString(LevelLoader.PlayLevelGuidKey);
#endif
            Navigator.Go(Navigator.Gameplay);
        }
    }
}
