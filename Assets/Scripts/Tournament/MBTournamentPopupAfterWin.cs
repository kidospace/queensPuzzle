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

        /// <summary>The beat between the score landing and the table reacting to it.</summary>
        const float MoveLead = 0.2f;

        /// <summary>The countdown moves once a second, so there is no point asking faster.</summary>
        const float ClockSeconds = 1f;

        /// <summary>Out of the list, up, and back in.</summary>
        const float LiftSeconds = 0.18f, TravelSeconds = 0.55f, SettleSeconds = 0.22f;

        /// <summary>Raised while it is out, so it reads as held above the list rather than as a
        /// row that grew.</summary>
        const float LiftScale = 1.08f;

        /// <summary>The points landing on the row: taken, given, and sprung back.</summary>
        const float ImpactSquash = 0.95f, ImpactIn = 0.05f;
        const float ImpactPeak = 1.08f, ImpactOut = 0.09f, ImpactSettle = 0.18f;

        CanvasGroup _group;
        MBTournamentRow[] _rows;
        RectTransform _content;
        ScrollRect _scroll;
        TMP_Text _clock, _rankUp, _pendingBones;
        GameObject _rankUpRoot, _offline;
        Transform _overlay;                 // Container — outside the scroll view, over the mask
        MBTournamentPopupAfterWinAnim _anim;

        /// <summary>
        /// The shape of the list, in code, because nothing else holds it any more.
        ///
        /// A VerticalLayoutGroup DRIVES its children's position, width and height — and driven
        /// values are recomputed every rebuild, never written to disk. Taking the group away did
        /// not leave the rows where they were; it uncovered what was underneath, which was
        /// nothing. These four numbers are the layout it used to apply.
        /// </summary>
        const float RowHeight = 150f, RowSpacing = 12f, ListPadding = 16f, RowInset = 20f;

        /// <summary>Everyone but the player, highest first — the table with the player's row
        /// taken out, so it can be put back wherever a score puts them.</summary>
        readonly List<TournamentEntry> _rivals = new List<TournamentEntry>();

        string _clockShown;
        bool _laidOut, _open, _closing;

        void Awake() {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            _scroll  = GetComponentInChildren<ScrollRect>(true);
            _content = _scroll != null ? _scroll.content : null;
            _rows    = _content != null ? _content.GetComponentsInChildren<MBTournamentRow>(true)
                                        : new MBTournamentRow[0];

            _overlay = _scroll != null ? _scroll.transform.parent : transform;

            _clock = Label("$StopWatchText");
            _rankUp = Label("$RankUP");
            _rankUpRoot = _rankUp != null ? _rankUp.gameObject : null;
            _pendingBones = Label("$PendingBonesTxt");

            var offline = transform.RecursiveFindChild("$Offline");
            _offline = offline != null ? offline.gameObject : null;
            if (_offline == null) Debug.LogError("[AfterWin] $Offline is missing from the prefab");

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
            _anim?.PlayIn();
            if (_anim != null) yield return new WaitUntil(() => _anim.IsFinished());

            // The decision point, taken HERE and not when the screen opened: a player who came
            // back online while the opening played has earned the climb.
            if (Offline) ShowOffline();
            else yield return Climb(win);

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
        void OnDisable() {
            CancelInvoke(nameof(Tick));
            _open = false;
            _closing = false;
        }

        /// <summary>The tournament is still running while the player reads this, and a countdown
        /// frozen at the second the screen opened is worse than no countdown. Written only when
        /// it actually changes — a TMP label assigned the string it already holds still rebuilds
        /// its mesh.</summary>
        void Tick() {
            string now = TournamentManager.TimeLeftText;
            if (now == _clockShown) return;
            if (_clock != null) _clock.text = now;
            _clockShown = now;
        }

        static bool Offline => TournamentManager.Status == ETournamentStatus.Offline;

        // ---- offline: nothing happened -----------------------------------------------------

        /// <summary>The panel is translucent and the table stays behind it — there IS a tournament
        /// there, it just cannot be joined from here. All it adds is what is waiting to be sent.</summary>
        void ShowOffline() {
            if (_offline != null) _offline.SetActive(true);
            if (_pendingBones != null)
                _pendingBones.text = "+" + (TournamentManager.State?.PendingScore ?? 0);
        }

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
            if (from < _rows.Length) {
                // The badge stays the one it opened with — where the player ENDS UP. Writing the
                // row with its current place here is what made it flip off the medal and collide
                // with the rival already wearing that number.
                _rows[from].Set(to + 1, ProfileManager.Name, win.newScore, true);
                yield return Impact((RectTransform)_rows[from].transform);
            }

            if (to >= from) {
                Draw(win.newScore, to);
                ScrollToMe(to);
                yield break;
            }

            ShowRankUp(from - to);
            yield return new WaitForSecondsRealtime(MoveLead);
            yield return MakeRoom(from, to);

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
        IEnumerator MakeRoom(int from, int to) {
            if (_content == null || _scroll == null || from >= _rows.Length || to >= _rows.Length) yield break;

            float step = RowHeight + RowSpacing;

            var home = new Vector2[_rows.Length];
            for (int i = 0; i < _rows.Length; i++) home[i] = ((RectTransform)_rows[i].transform).anchoredPosition;

            var me = (RectTransform)_rows[from].transform;
            _scroll.StopMovement();                        // inertia would fight the scroll below

            // Out: fixed size first, or stretched anchors under a different parent would resize it.
            var size = me.rect.size;
            var world = me.position;
            me.anchorMin = me.anchorMax = me.pivot = new Vector2(0.5f, 0.5f);
            me.sizeDelta = size;
            me.SetParent(_overlay, true);
            me.SetAsLastSibling();
            me.position = world;

            yield return Scale(me, 1f, LiftScale, LiftSeconds);

            // Where the list can actually end up: the slot the player is heading for, put where
            // ScrollToMe would put it, and then clamped to the ends of the list.
            var viewport = (RectTransform)_scroll.viewport;
            float slotFromTop = ListPadding + to * step + RowHeight * 0.5f;
            float c0 = _content.anchoredPosition.y;
            float c1 = Mathf.Clamp(slotFromTop - viewport.rect.height * 0.5f,
                                   0f, Mathf.Max(0f, _content.rect.height - viewport.rect.height));

            // And therefore where the row has to land: exactly where that slot will be sitting
            // once the list stops. Derived from the scroll rather than chosen, so the row and the
            // list arrive together and Place() below has nothing left to correct.
            Vector3 start = me.position;
            Vector3 local = viewport.InverseTransformPoint(start);
            Vector3 end = viewport.TransformPoint(
                new Vector3(local.x, viewport.rect.yMax - (slotFromTop - c1), local.z));

            for (float e = 0f; e < TravelSeconds; e += Time.unscaledDeltaTime) {
                float p = Ease(Mathf.Clamp01(e / TravelSeconds));
                me.position = Vector3.Lerp(start, end, p);
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Lerp(c0, c1, p));
                for (int i = to; i < from; i++)
                    ((RectTransform)_rows[i].transform).anchoredPosition =
                        new Vector2(home[i].x, home[i].y - step * p);
                yield return null;
            }

            // Back in, into the place that opened up.
            me.SetParent(_content, true);
            yield return Scale(me, LiftScale, 1f, SettleSeconds);
            Place(me, to);
        }

        /// <summary>Struck: driven in, thrown back past its size, settled. Motion only — a spray
        /// of anything over the row buries the one thing the player is reading.</summary>
        static IEnumerator Impact(RectTransform row) {
            yield return Scale(row, 1f, ImpactSquash, ImpactIn);
            yield return Scale(row, ImpactSquash, ImpactPeak, ImpactOut);
            yield return Scale(row, ImpactPeak, 1f, ImpactSettle);
        }

        static IEnumerator Scale(RectTransform t, float from, float to, float dur) {
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime) {
                t.localScale = Vector3.one * Mathf.Lerp(from, to, Ease(e / dur));
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }

        static float Ease(float p) => 1f - (1f - p) * (1f - p);

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

            if (_offline != null) _offline.SetActive(false);
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
        /// Put a row in its slot: full width inside the list's margins, and as far down from the
        /// top as its place says. Every row is the same height, so the slot is just arithmetic.
        /// </summary>
        static void Place(RectTransform row, int slot) {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(-RowInset * 2f, RowHeight);
            row.anchoredPosition =
                new Vector2(0f, -(ListPadding + slot * (RowHeight + RowSpacing) + RowHeight * 0.5f));
            row.localScale = Vector3.one;
        }

        /// <summary>Make the content exactly as tall as the rows that are switched on. Left at
        /// zero it does not scroll at all, and left at full height it scrolls through empty
        /// space below the last row.</summary>
        void Resize(int used) {
            if (_content == null || used <= 0) return;
            _content.sizeDelta = new Vector2(_content.sizeDelta.x,
                ListPadding * 2f + used * RowHeight + (used - 1) * RowSpacing);
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

            Resize(used);
        }

        /// <summary>Open on the player's row. It is the one row this screen exists to show, and in
        /// a group of twenty it is easily below the fold.</summary>
        void ScrollToMe(int mine) {
            if (_scroll == null || _content == null || mine >= _rows.Length) return;

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
