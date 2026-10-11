using System.Collections;
using System.Collections.Generic;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The screen a finished tournament opens on when there IS something to collect — the player
    /// placed inside the paying spots. It shows where they came, the podium, and a closed gift
    /// box; pressing Claim grants the prize and opens the box.
    ///
    ///     yield return claimScreen.Show();
    ///     // granted, marked seen, and gone
    ///
    /// The grant happens the moment Claim is pressed, in the same breath as marking the result
    /// seen — BEFORE any of the animation. Nothing a player is owed should depend on an effect
    /// finishing, or on them staying to watch it.
    ///
    /// The box opening is a swap between two images, and the burst of particles exists to cover
    /// exactly that frame. Which is also why the particles are fired first.
    ///
    /// This screen sets WHAT is on it — which box, open or closed, which rewards. The motion over
    /// that belongs to <see cref="MBTournamentEndClaimAnim"/> in the reskin, like every other popup
    /// in the game.
    /// </summary>
    public sealed class MBTournamentEndClaim : MonoBehaviour {

        const float FadeOut = 0.15f;

        /// <summary>How long the poof runs before the lid is swapped under it.</summary>
        const float PoofLead = 0.18f;

        /// <summary>The gift boxes in the prefab, one per paying place.</summary>
        static readonly string[] BoxNames = { "1Place", "2Place", "3Place" };

        CanvasGroup _group;
        TMP_Text _topText, _buttonText;
        Button _button;
        MBTournamentRow[] _podium;

        GameObject[] _boxes;
        Transform _rewards;                 // $Rewords
        List<CanvasGroup> _slots;           // $%1, $%2 …
        Transform _poof;                    // $CFXR Magic Poof
        MBTournamentEndClaimAnim _anim;     // the reskin's opening clip — absent in a skin without one

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

            _rewards = transform.RecursiveFindChild("$Rewords");
            _slots = new List<CanvasGroup>();
            if (_rewards != null)
                for (int i = 0; i < _rewards.childCount; i++) {
                    var slot = _rewards.GetChild(i).GetComponent<CanvasGroup>();
                    if (slot != null) _slots.Add(slot);
                }
            else Debug.LogError("[EndClaim] $Rewords is missing from the prefab");

            _poof = transform.RecursiveFindChild("$CFXR Magic Poof");
            _anim = GetComponent<MBTournamentEndClaimAnim>();

            _button = transform.RecursiveFindChild("$LvlButton")?.GetComponent<Button>();
            _buttonText = Label("$Text");
            if (_button == null) Debug.LogError("[EndClaim] $LvlButton has no Button — it cannot be pressed");
            else _button.onClick.AddListener(OnButton);
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
            _anim?.PlayIn();

            while (_opened) yield return null;
        }

        /// <summary>One button, two jobs: claim, then leave.</summary>
        void OnButton() {
            if (_closing) return;
            if (!_claimed) { StartCoroutine(Claim()); return; }
            StartCoroutine(Close());
        }

        // ---- claiming ---------------------------------------------------------------------

        IEnumerator Claim() {
            _claimed = true;
            if (_button != null) _button.interactable = false;

            // Granted and marked in one call, before a single frame of animation. A player who
            // closes the app mid-effect has still been paid, and it cannot be claimed twice.
            TournamentManager.CompleteEnded();

            // Particles first, lid second: the burst is what hides the swap.
            PlayPoof();
            yield return new WaitForSecondsRealtime(PoofLead);
            OpenBox();

            _anim?.PlayReward();
            if (_anim != null) yield return new WaitUntil(() => _anim.IsRewardFinished());

            if (_buttonText != null) _buttonText.text = "Continue";
            if (_button != null) _button.interactable = true;
        }

        /// <summary>
        /// Everything the clip cannot know, set on the one frame the burst is covering: this
        /// player's box is now an open one, its glow is lit, and the rewards it holds are in the
        /// scene. The motion over all of it belongs to <see cref="MBTournamentEndClaimAnim"/>.
        ///
        /// Without that component nothing would ever fade the rewards up, so they are simply put
        /// on screen — a skin with no clip still pays out and still reads.
        /// </summary>
        void OpenBox() {
            var box = Box(Rank);
            if (box != null) {
                Show(box.transform.RecursiveFindChild("GiftBoxIconClose"), false);
                Show(box.transform.RecursiveFindChild("GiftBoxIconOpen"), true);
                Show(box.transform.RecursiveFindChild("%Light"), true);
            }

            if (_rewards != null) _rewards.gameObject.SetActive(true);

            var rewards = TournamentConfig.Instance?.PrizeFor(Rank)?.rewards;
            int shown = rewards != null ? Mathf.Min(rewards.Length, _slots.Count) : 0;
            for (int i = 0; i < shown; i++) {
                _slots[i].gameObject.SetActive(true);
                if (_anim == null) _slots[i].alpha = 1f;
            }
        }

        /// <summary>The burst, and the sound the daily streak's gift opens on — the same gift
        /// opening should not sound like two different ones.
        ///
        /// A particle system under a Canvas is driven by UIParticle, which renders it in the UI's
        /// draw order — calling Play on the ParticleSystem alone leaves it behind the screen. The
        /// plain system is the fallback for a prefab without the wrapper.</summary>
        void PlayPoof() {
            var sfx = CommonSFX.Instance?.RewordOpen;
            if (sfx != null) CommonSFX.Play(sfx);

            if (_poof == null) return;
            var ui = _poof.GetComponent<Coffee.UIExtensions.UIParticle>();
            if (ui != null) { ui.Play(); return; }
            _poof.GetComponent<ParticleSystem>()?.Play();
        }

        IEnumerator Close() {
            _closing = true;
            for (float e = 0f; e < FadeOut; e += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.Clamp01(e / FadeOut);
                yield return null;
            }
            _group.alpha = 0f;
            gameObject.SetActive(false);
            _opened = false;                       // releases Show()
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

            // Only this player's box, closed, with the glow still off. Everything OpenBox sets is
            // put back here, so opening the screen twice starts the same way.
            for (int i = 0; i < _boxes.Length; i++) {
                if (_boxes[i] == null) continue;
                bool ours = i + 1 == Rank;
                _boxes[i].SetActive(ours);
                if (!ours) continue;
                Show(_boxes[i].transform.RecursiveFindChild("GiftBoxIconClose"), true);
                Show(_boxes[i].transform.RecursiveFindChild("GiftBoxIconOpen"), false);
                Show(_boxes[i].transform.RecursiveFindChild("%Light"), false);
            }

            // What is inside, dressed but hidden until the box opens.
            var rewards = TournamentConfig.Instance?.PrizeFor(Rank)?.rewards;
            for (int i = 0; i < _slots.Count; i++) {
                bool has = rewards != null && i < rewards.Length;
                _slots[i].gameObject.SetActive(false);        // OpenBox turns them on
                _slots[i].alpha = 0f;
                if (has) Dress(_slots[i].transform, rewards[i]);
            }
            if (_rewards != null) _rewards.gameObject.SetActive(false);

            if (_buttonText != null) _buttonText.text = "Claim";
            if (_button != null) _button.interactable = true;
        }

        static void Dress(Transform slot, Reward reward) {
            var icon = slot.RecursiveFindChild("$Icon")?.GetComponent<Image>();
            if (icon != null) {
                var sprite = TournamentConfig.Instance?.SpriteFor(reward.type);
                if (sprite != null) icon.sprite = sprite;
            }
            var amount = slot.RecursiveFindChild("$AmountText")?.GetComponent<TMP_Text>();
            if (amount != null) amount.text = reward.Label;
        }

        GameObject Box(int rank) =>
            rank >= 1 && rank <= _boxes.Length ? _boxes[rank - 1] : null;

        static void Show(Transform t, bool on) { if (t != null) t.gameObject.SetActive(on); }

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
