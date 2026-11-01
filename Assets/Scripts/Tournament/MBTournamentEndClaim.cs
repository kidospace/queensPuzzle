using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The screen a finished tournament opens on when there IS something to collect — the player
    /// placed inside the paying spots. It shows where they came, the podium, and a closed gift
    /// box; pressing Claim grants the prize, fades this screen out and opens the gift popup on
    /// this place's box.
    ///
    ///     yield return claimScreen.Show();
    ///     // granted, marked seen, the gift claimed, and gone
    ///
    /// The grant happens the moment Claim is pressed, in the same breath as marking the result
    /// seen — BEFORE any of the animation. Nothing a player is owed should depend on an effect
    /// finishing, or on them staying to watch it.
    ///
    /// The motion over this screen belongs to <see cref="MBTournamentEndClaimAnim"/> in the
    /// reskin, like every other popup in the game; the gift opening is the gift popup's.
    /// </summary>
    public sealed class MBTournamentEndClaim : MonoBehaviour {

        const float FadeOut = 0.35f;   // crossfades with the gift popup's cover fading in

        /// <summary>The gift boxes in the prefab, one per paying place.</summary>
        static readonly string[] BoxNames = { "1Place", "2Place", "3Place" };

        /// <summary>The gift each paying place opens, best first.</summary>
        static readonly EGiftBox[] GiftBoxes = { EGiftBox.Tournament1, EGiftBox.Tournament2, EGiftBox.Tournament3 };

        CanvasGroup _group;
        TMP_Text _topText, _buttonText;
        Button _button;
        MBTournamentRow[] _podium;

        GameObject[] _boxes;
        MBTournamentEndClaimAnim _anim;     // the reskin's opening clip — absent in a skin without one
        MBGiftPopup _gift;                  // the shared gift popup (found lazily)

        bool _claimed, _closing, _laidOut, _opened;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            _topText = Label("$TopText");

            var panel = transform.RecursiveFindChild("$RowsPannel");
            _podium = new MBTournamentRow[BoxNames.Length];
            if (panel != null)
                for (int i = 0; i < _podium.Length; i++) {
                    var row = panel.RecursiveFindChild("$" + (i + 1));
                    _podium[i] = row != null ? row.GetComponent<MBTournamentRow>() : null;
                }
            else Debug.LogError("[EndClaim] $RowsPannel is missing from the prefab");

            _boxes = new GameObject[BoxNames.Length];
            for (int i = 0; i < BoxNames.Length; i++) {
                var box = transform.RecursiveFindChild(BoxNames[i]);
                if (box == null) Debug.LogError($"[EndClaim] {BoxNames[i]} is missing from the prefab");
                else _boxes[i] = box.gameObject;
            }

            _anim = GetComponent<MBTournamentEndClaimAnim>();

            _button = transform.RecursiveFindChild("$ClaimButton")?.GetComponent<Button>();
            if (_button == null) Debug.LogError("[EndClaim] $ClaimButton has no Button — it cannot be pressed");
            else {
                _buttonText = _button.transform.RecursiveFindChild("$Text")?.GetComponent<TMP_Text>();
                _button.onClick.AddListener(OnButton);
            }
        }

        // Stay active but invisible for the first frames so everything lays out at its real size,
        // then hide until Show(). Measuring while hidden gives zero-high rows.
        IEnumerator Start() {
            _group.alpha = 0f;
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _laidOut = true;
            if (!_opened) gameObject.SetActive(false);
        }

        // ---- the one thing this screen is for --------------------------------------------

        /// <summary>Show it, and finish when it is gone. Opening it twice does nothing.</summary>
        public IEnumerator Show() {
            if (_opened) yield break;
            _opened = true;
            _claimed = _closing = false;

            gameObject.SetActive(true);
            Fill();

            while (!_laidOut) yield return null;

            // Filled before the first frame of the opening: the clip plays over a screen that is
            // already right, never one still settling behind it.
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _anim?.PlayIn();

            while (_opened) yield return null;
        }

        /// <summary>The one button: claim.</summary>
        void OnButton() {
            if (_closing || _claimed) return;
            StartCoroutine(Claim());
        }

        // ---- claiming ---------------------------------------------------------------------

        IEnumerator Claim() {
            _claimed = true;
            if (_button != null) _button.interactable = false;

            // Granted and marked in one call, before a single frame of animation. A player who
            // closes the app mid-effect has still been paid, and it cannot be claimed twice.
            TournamentManager.CompleteEnded();
            Analytics.TournamentClaim(TournamentManager.ClosedDay, Rank,
                                      Granted(EBoostType.UNDO), Granted(EBoostType.HINT), Granted(EBoostType.QUEEN));

            // Our box flies on into the gift popup (its box starts right over ours, so ours goes
            // on the same frame) while we fade out; whoever waits on Show() is held until the
            // player has claimed the gift too.
            if (_gift == null) _gift = FindAnyObjectByType<MBGiftPopup>(FindObjectsInactive.Include);
            var box = Rank >= 1 && Rank <= GiftBoxes.Length ? GiftBoxes[Rank - 1] : EGiftBox.Tournament3;
            var place = Rank >= 1 && Rank <= _boxes.Length ? _boxes[Rank - 1] : null;
            var art = place != null && place.transform.childCount > 0 ? place.transform.GetChild(0) as RectTransform : null;
            if (_gift != null) {
                GiftsManager.ShowGift(_gift, box, TournamentConfig.Instance?.PrizeFor(Rank)?.rewards, art);
                if (place != null) place.SetActive(false);   // Fill turns it back on next time
            }
            else Debug.LogError("[EndClaim] MBGiftPopup missing in the scene - the prize is granted but not shown");

            yield return Close(() => _gift == null || !_gift.IsShowing);
        }

        /// <summary>How much of one boost this place just handed over. Read off the prize that
        /// was granted, so the event says what the player actually got rather than what the place
        /// happens to be worth whenever the numbers are read back.</summary>
        static int Granted(EBoostType type) {
            var rewards = TournamentConfig.Instance?.PrizeFor(Rank)?.rewards;
            if (rewards == null) return 0;

            int total = 0;
            foreach (var reward in rewards)
                if (reward != null && reward.type == type) total += reward.amount;
            return total;
        }

        /// <summary>Fade out (letting taps through to what is on top), hold until
        /// <paramref name="done"/>, then switch off and release Show().</summary>
        IEnumerator Close(System.Func<bool> done) {
            _closing = true;
            _group.blocksRaycasts = false;
            for (float e = 0f; e < FadeOut; e += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.Clamp01(e / FadeOut);
                yield return null;
            }
            _group.alpha = 0f;
            while (!done()) yield return null;
            gameObject.SetActive(false);
            _opened = false;                       // releases Show()
        }

        /// <summary>
        /// Switched off by any route at all, not just the one out: the caller is waiting on this
        /// screen and would wait for ever otherwise. Only Close() releases it today, so anything
        /// else that hides it - a scene tearing down, a debug tool, a future caller - locks it
        /// shut, and every later Show returns at once having shown nothing.
        /// </summary>
        void OnDisable() {
            _opened = false;
            _closing = false;
        }

        // ---- contents ---------------------------------------------------------------------

        /// <summary>Where the player finished, 1-based; 0 when there is no closed table. The same
        /// place the lobby read to decide this screen should open at all.</summary>
        static int Rank => TournamentManager.ClosedPlace;

        void Fill() {
            var standings = TournamentManager.State?.lastSyncClosed?.standings;
            var entries = standings?.entries;
            if (entries == null) { Debug.LogWarning("[EndClaim] no closed table to show"); return; }

            int mine = standings.myIndex;
            if (_topText != null) _topText.text = $"You finished {Ordinal(mine + 1)}";

            for (int i = 0; i < _podium.Length; i++) {
                if (_podium[i] == null) continue;
                bool has = i < entries.Length;
                _podium[i].gameObject.SetActive(has);
                if (has) _podium[i].Set(i + 1, entries[i].name, entries[i].score, i == mine);
            }

            // Only this player's box (the Gifts/Boxes art) — it opens in the gift popup.
            for (int i = 0; i < _boxes.Length; i++)
                if (_boxes[i] != null) _boxes[i].SetActive(i + 1 == Rank);

            if (_buttonText != null) _buttonText.text = "Claim";
            if (_button != null) _button.interactable = true;
        }

        /// <summary>1st, 2nd, 3rd … including the teens, which break the simple rule.</summary>
        static string Ordinal(int n) {
            if (n <= 0) return n.ToString();
            int lastTwo = n % 100;
            if (lastTwo >= 11 && lastTwo <= 13) return n + "th";
            switch (n % 10) {
                case 1:  return n + "st";
                case 2:  return n + "nd";
                case 3:  return n + "rd";
                default: return n + "th";
            }
        }

        TMP_Text Label(string name) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) { Debug.LogError($"[EndClaim] {name} is missing from the prefab"); return null; }
            return found.GetComponent<TMP_Text>();
        }
    }
}
