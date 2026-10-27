using System.Collections;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// The screen a finished tournament opens on when there is nothing to collect — the player
    /// placed outside the paying spots. It states where they came, shows the podium and their own
    /// row, and offers one way out.
    ///
    /// NOT the prize version. That one hands something over and gets its own screen; this one
    /// deliberately has no claim, no reward animation and one button.
    ///
    ///     yield return endScreen.Show();
    ///     // the result has been marked seen and the screen is gone
    ///
    /// A coroutine rather than a handle with a result, because there is only one way to leave.
    /// Nothing is reported back and there is nothing to decide.
    ///
    /// The table it draws is the CLOSED one, frozen by the server at the moment the tournament
    /// ended. The live tournament has already moved on by now.
    /// </summary>
    public sealed class MBTournamentEndNoClaim : MonoBehaviour {

        const float FadeOut = 0.15f;

        /// <summary>How many places the podium shows — $1, $2, $3 in the prefab.</summary>
        const int PodiumPlaces = 3;

        CanvasGroup _group;
        TMP_Text _topText;
        MBTournamentRow[] _podium;
        MBTournamentRow _myRow;
        IPopupAnim _anim;           // the reskin's opening — absent in a skin without one

        bool _closing, _laidOut, _opened;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            _topText = Label("$TopText");
            _anim = GetComponent<IPopupAnim>();

            var panel = transform.RecursiveFindChild("$RowsPannel");
            if (panel == null) { Debug.LogError("[EndNoClaim] $RowsPannel is missing from the prefab"); return; }

            _podium = new MBTournamentRow[PodiumPlaces];
            for (int i = 0; i < PodiumPlaces; i++) _podium[i] = Row(panel, "$" + (i + 1));
            _myRow = Row(transform, "$MyRow");      // under the podium, not inside it

            var button = transform.RecursiveFindChild("$LvlButton")?.GetComponent<Button>();
            if (button == null) Debug.LogError("[EndNoClaim] $LvlButton has no Button — it cannot be pressed");
            else button.onClick.AddListener(() => { if (!_closing) StartCoroutine(Close()); });
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
            _closing = false;

            gameObject.SetActive(true);
            Fill();

            while (!_laidOut) yield return null;   // a first Show can beat the layout pass

            // Filled before the first frame of the opening: the clip plays over a screen that is
            // already right, never one still settling behind it.
            _group.alpha = 1f;
            _anim?.PlayIn();

            while (_opened) yield return null;     // until Close finishes
        }

        IEnumerator Close() {
            _closing = true;

            // Mark it seen before the fade, not after: the card behind is about to be looked at,
            // and it must not still be offering a result the player just read.
            TournamentManager.CompleteEnded();

            for (float e = 0f; e < FadeOut; e += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.Clamp01(e / FadeOut);
                yield return null;
            }
            _group.alpha = 0f;
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

        void Fill() {
            var standings = TournamentManager.State?.lastSyncClosed?.standings;
            var entries = standings?.entries;
            if (entries == null) { Debug.LogWarning("[EndNoClaim] no closed table to show"); return; }

            int mine = standings.myIndex;
            if (_topText != null) _topText.text = $"You finished {Ordinal(mine + 1)}";

            for (int i = 0; i < _podium.Length; i++) {
                bool has = i < entries.Length;
                if (_podium[i] == null) continue;
                _podium[i].gameObject.SetActive(has);
                if (has) _podium[i].Set(i + 1, entries[i].name, entries[i].score, i == mine);
            }

            // Their own row, always: this screen only opens for a finish outside the paying
            // places, so it can never be one of the three above.
            bool show = mine >= 0 && mine < entries.Length;
            if (_myRow != null) {
                _myRow.gameObject.SetActive(show);
                if (show) _myRow.Set(mine + 1, entries[mine].name, entries[mine].score, true);
            }
        }

        /// <summary>1st, 2nd, 3rd, 4th … including the teens, which break the simple rule.</summary>
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

        // ---- the prefab ---------------------------------------------------------------------

        MBTournamentRow Row(Transform under, string name) {
            var found = under.RecursiveFindChild(name);
            if (found == null) { Debug.LogError($"[EndNoClaim] {name} is missing under {under.name}"); return null; }
            return found.GetComponent<MBTournamentRow>();
        }

        TMP_Text Label(string name) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) { Debug.LogError($"[EndNoClaim] {name} is missing from the prefab"); return null; }
            return found.GetComponent<TMP_Text>();
        }
    }
}
