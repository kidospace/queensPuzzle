using Common;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace qp {
    public class MBFailPopup : MonoBehaviour {

        CanvasGroup _group;
        bool _showing;   // a real fail is on screen (guards the layout pass from hiding it)
        bool _opportunitySent;   // rewarded_ad_opportunity — once per Show
        GameObject _btnContinue;   // rewarded revive — clickable only when an ad is ready
        Button _continueButton;
        GameObject _showVideo;
        GameObject _offline;
        GameObject _videoLoading;
        bool _hasLast, _lastReviveLevel, _lastReady, _lastOffline;   // last applied Continue state — redraw only on change
        TMPro.TMP_Text _remainingText;

        [SerializeField]
        Color _queenFoundCLR;

        /// <summary>The one way to open the popup (MBGameplay.Fail).</summary>
        public void Show() {
            _showing = true;
            _opportunitySent = false;
            _hasLast = false;   // fresh show — apply the Continue state even if it didn't change
            _group.alpha = 1f;   // whatever the layout pass left behind, a real show is opaque
            gameObject.SetActive(true);
            UpdateContinueButton();

            // "Remaining: X" — puppies still to place, X tinted like the top bar's found count
            if (_remainingText != null) {
                string hex = ColorUtility.ToHtmlStringRGB(_queenFoundCLR);
                int remaining = MBGameplay.instance != null ? MBGameplay.instance.Remaining : 0;
                _remainingText.text = $"Remaining: <color=#{hex}>{remaining}</color>";
            }

            // Social proof ($SocialProofContiner > $Text): "you're not alone" — the share of
            // stage starters who lost or left here (100 - never-lose-or-leave), like this
            // player just did. Under 10% it backfires ("almost nobody fails here but you") —
            // and -1 means no data — so both fall back to a plain encouragement. Also gated on
            // StartShowReviveAtLevel — aligned with when the Revive button starts existing.
            var social = transform.RecursiveFindChild("$SocialProofContiner");
            var socialText = social != null ? social.RecursiveFindChild<TMPro.TMP_Text>("$Text") : null;
            if (socialText != null) {
                float nlol = WinStats.For(LevelLoader.CurrentLevelHash, LevelLoader.CurrentLevelWeight).NeverLoseOrLeavePct;
                float revive = nlol < 0f ? -1f : 100f - nlol;
                bool reviveKnown = AppData.LevelIdx.Value + 1 >= GameConfig.StartShowReviveAtLevel;
                socialText.text = reviveKnown && revive >= 10f
                    ? $"<color=#FABA1F>{revive:0.##}%</color> of players needed a\nRevive here too!"
                    : "So close! Keep going!";
            }

            // Last: the content above is final before the first animated frame is drawn.
            GetComponent<IPopupAnim>()?.PlayIn();   // the reskin animates the open, if it has one
        }

        // Same trick as the win popup: stay ACTIVE but invisible for the first frames so the UI
        // lays out at real size; only after the layout cycles hide for real. Keep the popup
        // active in the scene — this script turns it off by itself.
        private void Awake() {
            var back = transform.RecursiveFindChild<Button>("$BtnBack");
            back.onClick.AddListener(Replay);

            var btnContinue = transform.RecursiveFindChild<Button>("$BtnContinue");
            btnContinue.onClick.AddListener(Continue);
            _btnContinue = btnContinue.gameObject;
            _continueButton = btnContinue;
            _showVideo = btnContinue.transform.RecursiveFindChild("$ShowVideo").gameObject;
            _offline = btnContinue.transform.RecursiveFindChild("$Offline").gameObject;
            _videoLoading = btnContinue.transform.RecursiveFindChild("$VideoLoading").gameObject;

            // interactable=false only blocks clicks — no gray-out (same as the boost button)
            var colors = btnContinue.colors;
            colors.disabledColor = colors.normalColor;
            btnContinue.colors = colors;

            _remainingText = transform.RecursiveFindChild<TMPro.TMP_Text>("$RemainingText");

            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;   // invisible, but alive for the layout pass
        }

        IEnumerator Start() {
            // wait for UI to refresh our layout
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _group.alpha = 1f;
            if (!_showing) gameObject.SetActive(false);   // layout pass only — hidden until Show()
        }

        bool _replaying;   // re-entry guard — a second tap during the interstitial gap must not double-restart

        // Reset — abandon the attempt, back to the lobby.
        void Replay() {
            if (_replaying) return;
            _replaying = true;

            void _replay() {
                _replaying = false;
                _showing = false;
                gameObject.SetActive(false);
                MBGameplay.instance?.Replay();
            }

            // Interstitial on restart, from GameConfig.StartShowInterAtLevel (+ 1-min cooldown).
            // Show it first, then go to the lobby when it closes.
            if (AppData.LevelIdx.Value + 1 >= GameConfig.StartShowInterAtLevel && Ads.CanShowInterstitial) {
                Ads.ShowInterstitial("lose_restart", () => _replay());
            }
            else {
                _replay();
            }
        }

        // Continue is a rewarded revive: ad ready → $ShowVideo; else offline → $Offline; else
        // (online, ad still loading) → $VideoLoading. Only $ShowVideo is clickable.
        void Update() {
            if (_showing) UpdateContinueButton();
        }

        void UpdateContinueButton() {
            if (_btnContinue == null) return;
            bool reviveLevel = AppData.LevelIdx.Value + 1 >= GameConfig.StartShowReviveAtLevel;
            bool ready = reviveLevel && Ads.IsRewardedReady;
            bool offline = reviveLevel && !ready
                           && InternetConnection.Instance != null && !InternetConnection.Instance.HasInternet;
            if (_hasLast && reviveLevel == _lastReviveLevel && ready == _lastReady && offline == _lastOffline) return;
            _hasLast = true;
            _lastReviveLevel = reviveLevel;
            _lastReady = ready;
            _lastOffline = offline;

            _btnContinue.SetActive(reviveLevel);
            _showVideo.SetActive(ready);
            _offline.SetActive(offline);
            _videoLoading.SetActive(reviveLevel && !ready && !offline);
            _continueButton.interactable = ready;
            if (ready && !_opportunitySent) { _opportunitySent = true; Analytics.RewardedOpportunity(); }
        }

        // Continue — watch a rewarded ad; only revive (bones refill) if the reward is granted.
        void Continue() {
            Ads.WatchToEarn("lose_continue", () => {
                _showing = false;
                gameObject.SetActive(false);
                MBGameplay.instance?.ContinueAfterFail();
            });
        }
    }
}
