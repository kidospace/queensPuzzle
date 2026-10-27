using System.Collections;
using System.Collections.Generic;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {

    /// <summary>
    /// What a level win did to the tournament running now — shown right after a finished
    /// tournament's result, before the streak.
    ///
    ///     yield return afterWin.Show(result);
    ///     // gone
    ///
    /// WHETHER it opens is <see cref="ETournamentStatus"/>'s call and the caller's, not this
    /// screen's: Active and EndingSoon climb, Offline explains itself, and everything else means
    /// the win belongs to a tournament that isn't on screen yet.
    ///
    /// The table it draws is the one from the last SYNC — the server has not seen this win. So
    /// the player is placed at the score they had BEFORE it, and the animation is what moves them
    /// to the score they have now. A first win starts them at the bottom on zero, which is where
    /// a player with no points belongs.
    ///
    /// Offline stops at the first beat: the panel over the table says what is waiting to be sent,
    /// and nothing moves, because nothing has actually happened yet.
    /// </summary>
    public sealed class MBTournamentPopupAfterWin : MonoBehaviour {

        const float FadeOut = 0.15f;

        /// <summary>The pause before the table reacts, so the win registers first.</summary>
        const float MoveLead = 0.25f;

        /// <summary>The countdown moves once a second, so there is no point asking faster.</summary>
        const float ClockSeconds = 1f;

        /// <summary>Out of the list, up, and back in. Long on purpose: this is the one thing on
        /// the screen worth watching, and every version of it that hurried felt cheap.</summary>
        const float LiftSeconds = 0.32f, TravelSeconds = 1.1f, SettleSeconds = 0.4f;

        /// <summary>How much of the trip the rivals have to be clear in. The card covers the
        /// whole climb while they each step aside by one place, so at equal speed it is always
        /// ahead of the space it is heading for and lands on somebody. They go first.</summary>
        const float ClearBy = 0.55f;

        /// <summary>The score changes while the row is on its way up, not as a beat of its own —
        /// a separate punch on the number is what made the whole thing feel hard.</summary>
        const float ScoreFade = 0.55f;

        /// <summary>Barely raised. Enough to read as held above the list, not enough to read as
        /// a row that changed size.</summary>
        const float LiftScale = 1.04f;

        CanvasGroup _group;
        MBTournamentRow[] _rows;
        RectTransform _content;
        ScrollRect _scroll;
        TMP_Text _clock, _rankUp, _pendingBones;
        GameObject _rankUpRoot, _offline, _online;
        Transform _overlay;                 // $Online — outside the scroll view, over the mask
        Transform _arrows;                  // $RankUpPS — ridden by the card on its way up
        MBTournamentPopupAfterWinAnim _anim;
        [SerializeField] MBTournamentBonesEffect _bonesEffect;

        /// <summary>
        /// The shape of the list, in code, because nothing else holds it any more.
        ///
        /// A VerticalLayoutGroup DRIVES its children's position, width and height — and driven
        /// values are recomputed every rebuild, never written to disk. Taking the group away did
        /// not leave the rows where they were; it uncovered what was underneath, which was
        /// nothing. These two numbers are the layout it used to apply.
        ///
        /// The width, the anchors and the pivot are NOT here. They are the row's own, read off
        /// the prefab and never written — writing them meant the list laid out in the editor was
        /// a list nobody ever saw, and a row coming back from the climb came back a different
        /// shape from the ones it landed between.
        /// </summary>
        const float RowSpacing = 12f, ListPadding = 16f;

        /// <summary>The height the rows actually ship at, so the slots match the art.</summary>
        float _rowHeight = 150f;

        /// <summary>Everyone but the player, highest first — the table with the player's row
        /// taken out, so it can be put back wherever a score puts them.</summary>
        readonly List<TournamentEntry> _rivals = new List<TournamentEntry>();

        string _clockShown;
        bool _laidOut, _open, _closing, _offlineShown;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            _scroll  = GetComponentInChildren<ScrollRect>(true);
            _content = _scroll != null ? _scroll.content : null;
            _rows    = _content != null ? _content.GetComponentsInChildren<MBTournamentRow>(true)
                                        : new MBTournamentRow[0];
            if (_rows.Length > 0) _rowHeight = ((RectTransform)_rows[0].transform).rect.height;

            _overlay = _scroll != null ? _scroll.transform.parent : transform;
            _arrows = transform.RecursiveFindChild("$RankUpPS");

            _clock = Label("$StopWatchText");
            _rankUp = Label("$RankUP");
            _rankUpRoot = _rankUp != null ? _rankUp.gameObject : null;
            _pendingBones = Label("$PendingBonesTxt");

            var offline = transform.RecursiveFindChild("$Offline");
            _offline = offline != null ? offline.gameObject : null;
            if (_offline == null) Debug.LogError("[AfterWin] $Offline is missing from the prefab");

            // The table and its header. Never on screen together with the offline panel.
            var online = transform.RecursiveFindChild("$Online");
            _online = online != null ? online.gameObject : null;
            if (_online == null) Debug.LogError("[AfterWin] $Online is missing from the prefab");

            _anim = GetComponent<MBTournamentPopupAfterWinAnim>();

            var close = transform.RecursiveFindChild("$CloseBtn")?.GetComponent<Button>();
            if (close == null) Debug.LogError("[AfterWin] $CloseBtn has no Button — it cannot be closed");
            else close.onClick.AddListener(() => { if (!_closing && _open) StartCoroutine(Close()); });
        }

        // Alive but invisible for the first frames so the list lays out at its real size, then
        // hidden until Show(). Measuring while hidden gives zero-high rows, and the travel below
        // is measured off those heights.
        IEnumerator Start() {
            _group.alpha = 0f;
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _laidOut = true;
            if (!_open) gameObject.SetActive(false);
        }

        // ---- the one thing this screen is for --------------------------------------------

        /// <summary>Show it, and finish when it is gone. Opening it twice does nothing.</summary>
        public IEnumerator Show(TournamentWinResult win) {
            if (_open) yield break;
            _open = true;
            _closing = false;

            gameObject.SetActive(true);
            Fill(win);
            InvokeRepeating(nameof(Tick), 0f, ClockSeconds);

            while (!_laidOut) yield return null;

            _group.alpha = 1f;

            // The opening clip brings in both the panel and the table; all this decides is which
            // of the two is there to be brought in. A curve on an inactive object draws nothing.
            //
            // Still only a presentation choice - the decision that matters is taken after the
            // opening, so a line that comes back while it plays earns the climb anyway.
            if (_offline != null) { _offline.SetActive(Offline); _offlineShown = Offline; }
            if (_online != null) _online.SetActive(!_offlineShown);
            if (_offlineShown) Tick();                 // the pending count, before it is seen

            _anim?.PlayIn();
            if (_anim != null) yield return new WaitUntil(() => _anim.IsFinished());

            // The decision point, taken HERE and not when the screen opened: a player who came
            // back online while the opening played has earned the climb.
            bool panelWasUp = _offlineShown;

            if (Offline) yield return WaitOffline();

            // Online by the time we got here, but the panel went up with the opening — the line
            // came back during those few tenths. Nothing else takes it down, so without this it
            // stays on screen for good, over a screen that is otherwise working perfectly.
            else if (_offlineShown) yield return HideOffline();

            // One rebuild for both routes. Reconnecting triggers a sync that moves the table,
            // and the climb below is drawn from it; a route that skipped this would animate
            // against standings that are already out of date, which is how the two paths came to
            // disagree in the first place.
            if (panelWasUp && _open && !_closing) Fill(win);

            // And from here it is settled. Losing the line again mid-climb does not undo a win
            // that has already been counted, so nothing below asks a second time.
            if (Climbable) yield return Climb(win);

            while (_open) yield return null;   // until the player taps out
        }

        IEnumerator Close() {
            _closing = true;
            CancelInvoke(nameof(Tick));
            for (float e = 0f; e < FadeOut; e += Time.unscaledDeltaTime) {
                _group.alpha = 1f - Mathf.Clamp01(e / FadeOut);
                yield return null;
            }
            _group.alpha = 0f;
            gameObject.SetActive(false);
            _open = false;                     // releases Show()
        }

        /// <summary>
        /// Switched off by any route at all, not just the one out: the caller is waiting on this
        /// screen and would wait for ever otherwise. Only Close() releases it today, so anything
        /// else that hides it - a scene tearing down, a debug tool, a future caller - locks it
        /// shut, and every later Show returns at once having shown nothing.
        /// </summary>
        void OnEnable() {
            if (_bonesEffect != null) _bonesEffect.Stop();
        }

        void OnDisable() {
            if (_bonesEffect != null) _bonesEffect.Stop();
            CancelInvoke(nameof(Tick));
            _open = false;
            _closing = false;
        }

        /// <summary>The tournament is still running while the player reads this, and a countdown
        /// frozen at the second the screen opened is worse than no countdown. Written only when
        /// it actually changes — a TMP label assigned the string it already holds still rebuilds
        /// its mesh.</summary>
        void Tick() {
            if (_pendingBones != null && _offline != null && _offline.activeSelf)
                _pendingBones.text = "+" + (TournamentManager.State?.PendingScore ?? 0);

            string now = TournamentManager.TimeLeftText;
            if (now == _clockShown) return;
            if (_clock != null) _clock.text = now;
            _clockShown = now;
        }

        static bool Offline => TournamentManager.ShowsOffline;

        // ---- offline: nothing happened -----------------------------------------------------

        /// <summary>
        /// Hold here while there is no line, and let go the moment there is one.
        ///
        /// The panel is translucent and the table stays behind it — there IS a tournament there,
        /// it just cannot be reached from here. All the panel adds is what is waiting to be sent.
        ///
        /// It fades, both ways. A player who reconnects while reading it sees it clear and then
        /// gets the climb they would have had at the open, rather than a panel vanishing on a
        /// frame boundary.
        /// </summary>
        IEnumerator WaitOffline() {
            ShowOfflinePanel();

            // Closing the screen releases this too — the player does not have to wait for a
            // connection to get out.
            yield return new WaitUntil(() => !Offline || !_open || _closing);
            if (!_open || _closing) yield break;

            yield return HideOffline();
        }

        /// <summary>
        /// Take the panel away. The only way it ever leaves, so both routes to a live connection
        /// come through here — the one that waited for it, and the one where it arrived during
        /// the opening.
        ///
        /// The table is switched on first and the clip crossfades: the panel fades out while the
        /// list and its header fade in. The clip holds its last frame (panel at zero) only until
        /// the panel is switched off below.
        /// </summary>
        IEnumerator HideOffline() {
            if (_online != null) _online.SetActive(true);
            _anim?.PlayOfflineGone();
            if (_anim != null) yield return new WaitUntil(() => _anim.IsOfflineGone());
            _offline.SetActive(false);
            _offlineShown = false;
        }

        /// <summary>Bring the panel up for a line that dropped AFTER the opening, in place of the
        /// table. The animator is resting in Idle, which holds the prefab's values — the panel at
        /// full — so it only has to exist again.</summary>
        void ShowOfflinePanel() {
            if (_offline == null || _offlineShown) return;
            _offlineShown = true;
            _offline.SetActive(true);
            if (_online != null) _online.SetActive(false);
            Tick();
        }

        /// <summary>There is a live tournament for the climb to happen in. Checked again after a
        /// wait offline: the one the player won in can have closed while they were away, and
        /// animating a table that is already finished would be a lie.</summary>
        static bool Climbable =>
            !TournamentManager.ShowsOffline &&
            (TournamentManager.Status == ETournamentStatus.Active ||
             TournamentManager.Status == ETournamentStatus.EndingSoon);

        // ---- online: the climb --------------------------------------------------------------

        /// <summary>
        /// The points land on the badge, and then the table rearranges itself around the player.
        ///
        /// Nothing slides. The score becomes the new one, the table is drawn as it now stands,
        /// and the player is put back in the middle of it — the arrow at the top is what says
        /// they moved.
        ///
        /// A rank that did not improve gets no arrow either. Rivals score while the player is
        /// playing, so a win can land them lower than they were; that is not something to
        /// announce.
        /// </summary>
        IEnumerator Climb(TournamentWinResult win) {
            int before = win.newScore - win.scoreAdded;
            int from = Place(before);
            int to = Place(win.newScore);

            // The score becomes the new one and the row takes the hit for it. There is nothing
            // to count up to — the player knows what they just won; what they have not seen yet
            // is where it puts them, and that is what comes after.
            // Standing still: nowhere to carry the change, so it happens here.
            if (to >= from) {
                if (from < _rows.Length) yield return ChangeScore(_rows[from], win.newScore);
                Draw(win.newScore, to);
                ScrollToMe(to);
                yield break;
            }

            ShowRankUp(from - to);
            yield return new WaitForSecondsRealtime(MoveLead);
            yield return MakeRoom(from, to, win.newScore);

            // Everyone is now standing under the number they have been wearing since it opened,
            // so this settles the positions and changes nothing anybody can see.
            Draw(win.newScore, to);
        }

        /// <summary>
        /// Lift the player out of the list, move the list under them, and set them back down.
        ///
        /// The table does the travelling and the row barely moves — so a win worth fourteen
        /// places looks much like one worth a single place, and neither carries the row off the
        /// screen.
        ///
        /// How far each of them goes is NOT decided in advance. The list is asked first: it can
        /// only scroll between its top and its bottom, and near either end it runs out long
        /// before the climb does. Whatever it cannot cover, the row covers by travelling further.
        /// Fixing the row's rise first and hoping the scroll would absorb the rest is what put a
        /// jump at the end of every climb into the top places, where there is nothing left to
        /// scroll.
        ///
        /// While it is out it belongs to the Container, not the list. That puts it over the scroll
        /// view in draw order and outside the mask, so it is never clipped at the edge of the
        /// viewport — which is exactly where a row about to move usually sits.
        /// </summary>
        IEnumerator MakeRoom(int from, int to, int score) {
            if (_content == null || _scroll == null || from >= _rows.Length || to >= _rows.Length) yield break;

            float step = _rowHeight + RowSpacing;

            var home = new Vector2[_rows.Length];
            for (int i = 0; i < _rows.Length; i++) home[i] = ((RectTransform)_rows[i].transform).anchoredPosition;

            var me = (RectTransform)_rows[from].transform;
            _scroll.StopMovement();                        // inertia would fight the scroll below

            // How the row is anchored is the prefab's business, and the trip is about to replace
            // all of it — so it is kept here and handed back at the end, rather than put back as
            // whatever this file happens to believe a row looks like.
            var anchorMin = me.anchorMin;
            var anchorMax = me.anchorMax;
            var pivot = me.pivot;
            var sizeDelta = me.sizeDelta;
            float anchoredX = me.anchoredPosition.x;

            // Out: fixed size first, or stretched anchors under a different parent would resize it.
            var size = me.rect.size;
            var world = me.position;
            me.anchorMin = me.anchorMax = me.pivot = new Vector2(0.5f, 0.5f);
            me.sizeDelta = size;
            me.SetParent(_overlay, true);
            me.SetAsLastSibling();
            me.position = world;

            // The arrows belong on the card, not on a badge in the corner — the card is what
            // the eye is following. Carried with it, so they keep rising out of it the whole way.
            if (_arrows != null) {
                _arrows.SetParent(me, false);
                _arrows.localPosition = Vector3.zero;
                _arrows.gameObject.SetActive(true);
                var ui = _arrows.GetComponent<Coffee.UIExtensions.UIParticle>();
                if (ui != null) ui.Play(); else _arrows.GetComponent<ParticleSystem>()?.Play();
            }

            yield return Scale(me, 1f, LiftScale, LiftSeconds);

            // The score changes on the way up rather than before it. One movement instead of a
            // punch followed by a journey.
            StartCoroutine(ChangeScore(_rows[from], score));

            // Where the list can actually end up: the slot the player is heading for, put where
            // ScrollToMe would put it, and then clamped to the ends of the list.
            //
            // Measured as how far the list is SCROLLED, not as the content's anchoredPosition.
            // Those are the same number only while the content hangs from the top of the
            // viewport; anchored anywhere else they are not, and "scrolled to the very top" came
            // out as a position halfway down the list — which is why a climb into first place
            // was the one that landed wrong.
            var viewport = (RectTransform)_scroll.viewport;
            float slotFromTop = ListPadding + to * step + _rowHeight * 0.5f;
            float scrollable = Mathf.Max(0f, _content.rect.height - viewport.rect.height);
            float c0 = scrollable > 0f ? (1f - _scroll.verticalNormalizedPosition) * scrollable : 0f;
            float c1 = Mathf.Clamp(slotFromTop - viewport.rect.height * 0.5f, 0f, scrollable);

            // And therefore where the row has to land: exactly where that slot will be sitting
            // once the list stops. Derived from the scroll rather than chosen, so the row and the
            // list arrive together and Place() below has nothing left to correct.
            Vector3 start = me.position;
            Vector3 local = viewport.InverseTransformPoint(start);
            Vector3 end = viewport.TransformPoint(
                new Vector3(local.x, viewport.rect.yMax - (slotFromTop - c1), local.z));

            for (float e = 0f; e < TravelSeconds; e += Time.unscaledDeltaTime) {
                float p = Ease(Mathf.Clamp01(e / TravelSeconds));
                float cleared = Ease(Mathf.Clamp01(e / (TravelSeconds * ClearBy)));

                me.position = Vector3.Lerp(start, end, p);
                if (scrollable > 0f)
                    _scroll.verticalNormalizedPosition = 1f - Mathf.Lerp(c0, c1, p) / scrollable;
                for (int i = to; i < from; i++)
                    ((RectTransform)_rows[i].transform).anchoredPosition =
                        new Vector2(home[i].x, home[i].y - step * cleared);
                yield return null;
            }

            // Back in, into the place that opened up.
            if (_arrows != null) {
                _arrows.SetParent(_overlay, false);
                _arrows.gameObject.SetActive(false);
            }
            me.SetParent(_content, true);
            yield return Scale(me, LiftScale, 1f, SettleSeconds);

            me.anchorMin = anchorMin;
            me.anchorMax = anchorMax;
            me.pivot = pivot;
            me.sizeDelta = sizeDelta;
            me.anchoredPosition = new Vector2(anchoredX, me.anchoredPosition.y);
            Place(me, to);
        }

        IEnumerator ChangeScore(MBTournamentRow row, int score) {
            if (_bonesEffect == null || row.ScoreLabel == null) {
                yield return row.FadeScoreTo(score, ScoreFade);
                yield break;
            }
            _bonesEffect.Attach(row.ScoreLabel);
            _bonesEffect.Play();
            yield return new WaitForSecondsRealtime(_bonesEffect.RevealDelay);
            row.SetScore(score);
            while (_bonesEffect.IsPlaying) yield return null;
        }

        static IEnumerator Scale(RectTransform t, float from, float to, float dur) {
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * Mathf.Lerp(from, to, Ease(e / dur));
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }

        /// <summary>Soft at both ends. An ease-out alone still leaves hard: it starts at full
        /// speed, and the eye catches the start of a movement more than the end of it.</summary>
        static float Ease(float p) => Mathf.SmoothStep(0f, 1f, p);

        /// <summary>Switch the arrow on with how far the player came, and let the reskin make
        /// something of it. The number is this screen's; everything that moves is the clip's.</summary>
        void ShowRankUp(int places) {
            if (_rankUpRoot != null) _rankUpRoot.SetActive(true);
            if (_rankUp != null) _rankUp.text = "+" + places;
            _anim?.PlayRankUp();
        }

        // ---- the table ----------------------------------------------------------------------

        void Fill(TournamentWinResult win) {
            _rivals.Clear();
            var standings = TournamentManager.State?.lastSyncCurrent?.standings;
            var entries = standings?.entries;
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                    if (!standings.IsMe(i)) _rivals.Add(entries[i]);

            if (_offline != null && !_offlineShown) _offline.SetActive(false);
            if (_online != null && !_offlineShown) _online.SetActive(true);
            if (_rankUpRoot != null) _rankUpRoot.SetActive(false);
            _clockShown = null;          // reopened later, the time it last showed is wrong
            Tick();

            // Opened in the order the player is still in — the server has not seen this win —
            // but with everyone's FINAL place already on their badge. The badges then never move
            // again: the animation simply delivers each row to the number it is already wearing.
            int before = win.newScore - win.scoreAdded;
            Draw(before, Place(win.newScore));
            ScrollToMe(Place(before));
        }

        /// <summary>The place a rival currently at <paramref name="slot"/> will hold once the
        /// player has moved from <paramref name="mine"/> to <paramref name="finalMine"/>: one
        /// lower if the player is about to overtake them, unchanged otherwise.</summary>
        static int Badge(int slot, int mine, int finalMine) =>
            slot >= finalMine && slot < mine ? slot + 2 : slot + 1;

        /// <summary>Where a score puts the player: below everyone who already has that much, which
        /// is the rule the server ranks by. Zero points lands at the bottom, which is exactly where
        /// a player who has not scored yet belongs.</summary>
        int Place(int score) {
            int index = 0;
            foreach (var rival in _rivals) if (rival.score >= score) index++;
            return Mathf.Min(index, _rows.Length - 1);
        }

        /// <summary>
        /// Put a row in its slot: as far down from the top of the list as its place says, and
        /// nothing else. Every row is the same height, so the slot is just arithmetic.
        ///
        /// Worked out against the row's OWN anchor and pivot rather than assuming it hangs from
        /// the top of the list, so the prefab is free to anchor its rows however it likes and
        /// this still drops them in the right place.
        /// </summary>
        void Place(RectTransform row, int slot) {
            float centre = _content.rect.yMax
                         - (ListPadding + slot * (_rowHeight + RowSpacing) + _rowHeight * 0.5f);
            float anchor = _content.rect.yMin + row.anchorMin.y * _content.rect.height;

            row.anchoredPosition = new Vector2(row.anchoredPosition.x,
                centre - (0.5f - row.pivot.y) * row.rect.height - anchor);
            row.localScale = Vector3.one;
        }

        /// <summary>Make the content exactly as tall as the rows that are switched on. Left at
        /// zero it does not scroll at all, and left at full height it scrolls through empty
        /// space below the last row.</summary>
        void Resize(int used) {
            if (_content == null || used <= 0) return;
            _content.sizeDelta = new Vector2(_content.sizeDelta.x,
                ListPadding * 2f + used * _rowHeight + (used - 1) * RowSpacing);
        }

        /// <summary>
        /// The whole list at one score for the player: the rivals in order, with the player
        /// slotted into the place that score earns.
        ///
        /// <paramref name="finalMine"/> is where the player ENDS UP, which is what every badge is
        /// drawn from — rank, medal and prize. Drawing those from the current order instead would
        /// make all of them flip the instant the table rearranged, which is the one thing in here
        /// that cannot be hidden behind motion.
        /// </summary>
        void Draw(int myScore, int finalMine) {
            int mine = Place(myScore);
            int used = Mathf.Min(_rivals.Count + 1, _rows.Length);

            // The list is sized BEFORE the rows go in it. Place works a slot out against the
            // row's own anchor, and an anchor that is not the top of the list is measured from a
            // height — so placing first put every row where it belonged in the list's previous
            // size.
            Resize(used);

            for (int i = 0; i < _rows.Length; i++) {
                bool on = i < used;
                _rows[i].gameObject.SetActive(on);
                if (!on) continue;
                Place((RectTransform)_rows[i].transform, i);

                if (i == mine) { _rows[i].Set(finalMine + 1, ProfileManager.Name, myScore, true); continue; }

                int rival = i < mine ? i : i - 1;
                if (rival < _rivals.Count) _rows[i].Set(Badge(i, mine, finalMine), _rivals[rival].name, _rivals[rival].score, false);
                else _rows[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Open on the player's row. It is the one row this screen exists to show, and in
        /// a group of twenty it is easily below the fold.</summary>
        void ScrollToMe(int mine) {
            if (_scroll == null || _content == null || mine >= _rows.Length) return;

            // The list was just resized and refilled. A ScrollRect works off bounds it refreshes
            // at the end of the frame, so setting a position now lands against the size the list
            // used to be — and the further down the player sits, the further off it lands.
            Canvas.ForceUpdateCanvases();

            float view = ((RectTransform)_scroll.viewport).rect.height;
            float scrollable = _content.rect.height - view;
            if (scrollable <= 0f) { _scroll.verticalNormalizedPosition = 1f; return; }

            var row = (RectTransform)_rows[mine].transform;
            float centre = _content.rect.yMax - _content.InverseTransformPoint(row.position).y;
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((centre - view * 0.5f) / scrollable);
        }

        TMP_Text Label(string name) {
            var found = transform.RecursiveFindChild(name);
            if (found == null) { Debug.LogError($"[AfterWin] {name} is missing from the prefab"); return null; }
            return found.GetComponent<TMP_Text>();
        }
    }
}
