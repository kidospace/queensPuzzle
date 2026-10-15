using System;
using System.Threading.Tasks;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>Where the tournament stands for THIS player — the one value the lobby card switches
    /// on. Set by the server's answer, not by the local clock, so a clock that runs fast only makes
    /// the timer hit zero early and never changes the state.</summary>
    public enum ETournamentStatus {
        None,         // no tournament running at all (server says so) — the card is hidden
        Locked,       // campaign hasn't reached the unlock level yet
        NotJoined,    // tournament running, no win in it yet
        Active,       // joined — timer + rank
        EndingSoon,   // joined, less than the config's threshold left
        Calculating,  // the local timer hit zero — waiting for the next sync to bring the result
        Ended,        // a tournament the player played is closed — Claim Prize / See Results
    }

    /// <summary>
    /// Everything Tournament in one place — API only, for review. Logic comes next.
    ///
    /// Shape: local first. A win counts at once and is queued; <see cref="Sync"/> (background, every
    /// ~30s and on resume) sends the queue and takes back the server's picture. The only thing that
    /// must come from the server is the final ranking; the prize for it is granted here.
    ///
    /// What the UI reads is <see cref="State"/> — the snapshots exactly as the last sync returned
    /// them — plus the four values below, which can't live there because they need the clock, the
    /// config and the connection. Everything else (time, config, backend, sync bookkeeping) is
    /// internal.
    /// </summary>
    public static class TournamentManager {

        static TournamentState _state;
        static TournamentSyncer _syncer;
        static ITournamentBackend _backend;

        /// <summary>Developer tools for whatever backend is in use, or null when it offers none —
        /// which is the honest answer for an HTTP server without debug endpoints. Everything it
        /// does is server-side, so follow a call with <see cref="Sync"/> to see it.</summary>
        public static ITournamentDebug Debug => _backend as ITournamentDebug;

        /// <summary>Boot (MBStartup). Loads the blob, builds the syncer and starts the background
        /// runner. Main thread, instant: it never waits on the network — the runner holds its first
        /// sync until server time is trusted. Calling it twice does nothing.</summary>
        public static void Init() {
            if (_state != null) return;
            _state = TournamentState.Load();
            _backend = new LocalTournamentBackend(UserID.GetUserIDLocal());
            _syncer = new TournamentSyncer(_backend, _state);
            MBTournamentSyncRunner.Create(_syncer);
        }

        /// <summary>The blob: the tournament running now + the last closed one, and the wins not
        /// sent yet. Written only here — the UI reads.</summary>
        public static TournamentState State => _state;

        // ---- derived: needs the clock / config / connection, so it can't sit in State ----

        /// <summary>
        /// Where the tournament stands. Read top to bottom — the first line that matches wins:
        /// no feature at all, then the lock, then a result waiting to be seen, and only then the
        /// running tournament.
        ///
        /// The connection is NOT in here. Being offline is a fact about the device, true
        /// alongside any of these rather than instead of one — see <see cref="ShowsOffline"/>,
        /// which is what a caller asks when it must not act on a state it cannot trust.
        /// </summary>
        public static ETournamentStatus Status {
            get {
                if (!_exists) return ETournamentStatus.None;
                if (!_isRevealed) return ETournamentStatus.None;
                if (!_isUnlocked) return ETournamentStatus.Locked;

                // A closed tournament the player hasn't been shown yet — Claim Prize / See Results.
                // The row check is a belt: the server only sends one the player actually played,
                // and a popup with no rank in it would say nothing.
                if (_state.lastSyncClosed.Exists && !_state.closedShown
                    && _state.lastSyncClosed.standings.myIndex >= 0) return ETournamentStatus.Ended;

                if (!_state.lastSyncCurrent.Exists) return ETournamentStatus.None;

                // The timer ran out. "Calculating" only if there is really a result on the way —
                // the player scored in this one. Otherwise (never joined, or a blob left over from
                // weeks ago) there is nothing to wait for, so the card stays hidden until the sync
                // brings the tournament running now.
                if (TimeLeft <= TimeSpan.Zero)
                    return _state.ConfirmedScore > 0 ? ETournamentStatus.Calculating
                                                     : ETournamentStatus.None;

                if (MyScoreWithPending <= 0) return ETournamentStatus.NotJoined;
                return TimeLeft < TournamentConfig.Instance.EndingSoonTime
                    ? ETournamentStatus.EndingSoon
                    : ETournamentStatus.Active;
            }
        }

        /// <summary>Countdown to the end of the current tournament (zero once it is over).</summary>
        public static TimeSpan TimeLeft {
            get {
                if (_state == null || !_state.lastSyncCurrent.Exists) return TimeSpan.Zero;
                var left = _state.lastSyncCurrent.info.EndUtc - MBServerTimeManagerV2.UTCNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// <see cref="TimeLeft"/> as the UI writes it: hh:mm:ss, the same shape the daily
        /// challenge card uses. Here rather than in each screen because the countdown appears on
        /// both the lobby card and the leaderboard, and one tournament must not show two times.
        ///
        /// Built rather than ToString(@"hh\:mm\:ss"), which would be wrong: that "hh" is the Hours
        /// COMPONENT, 0-23, with days held separately. The daily resets every 24 hours so it never
        /// notices; a 48-hour tournament would spend its whole first day showing the second one's
        /// time, and 41 hours left would read as 17.
        /// </summary>
        public static string TimeLeftText {
            get {
                var left = TimeLeft;
                return $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
            }
        }

        /// <summary>The player's score in the current tournament: what the server confirmed plus
        /// the wins still waiting to be sent. With an empty queue this is exactly the score in
        /// <see cref="State"/>; it differs only between a win and its sync, or while offline.
        /// A pending win belongs to the tournament it will arrive at, so once this one is over it
        /// already belongs to the next — counting it here would inflate a table that is closing.</summary>
        public static int MyScoreWithPending =>
            _state == null ? 0
            : _state.ConfirmedScore + (TimeLeft > TimeSpan.Zero ? _state.PendingScore : 0);

        /// <summary>
        /// The player's row in the table. 0-based; -1 = not in it.
        ///
        /// The server's answer, always, while it has one — the leaderboard draws the rows the
        /// server ordered, and a card that worked the place out for itself is a second opinion
        /// nobody asked for. It disagreed, too: it counted everyone level with the player as
        /// ahead of them, and said 20th about a table that showed 11th.
        ///
        /// The one exception is a win the server has not seen yet, which is the whole point of
        /// this over <c>standings.myIndex</c>: offline, or in the seconds before a sync, the card
        /// still moves. That guess breaks ties the way the server does, so the sync that follows
        /// it does not shuffle the number.
        /// </summary>
        public static int MyIndexWithPending {
            get {
                int mine = MyScoreWithPending;
                if (mine <= 0) return -1;

                var st = _state.lastSyncCurrent.standings;
                if (st.entries == null) return 0;                       // joined, table not synced yet
                if (mine == _state.ConfirmedScore) return st.myIndex;   // nothing pending: the server said

                // Ties by name, ascending — LocalTournamentBackend sorts the table that way.
                string myName = st.Me?.name ?? ProfileManager.Name;

                int index = 0;
                for (int i = 0; i < st.entries.Length; i++) {
                    if (st.IsMe(i)) continue;
                    var other = st.entries[i];
                    if (other.score > mine) { index++; continue; }
                    if (other.score == mine && string.CompareOrdinal(other.name, myName) < 0) index++;
                }
                return index;
            }
        }

        /// <summary>Where the player finished in the CLOSED tournament, 1-based; 0 when they were
        /// not in that table. Here and not in a screen because three callers switch on it — the
        /// lobby card's face, the lobby choosing an end screen, and that screen's contents — and a
        /// tournament that pays on one and not the other would be a bug nobody could see.</summary>
        public static int ClosedPlace {
            get {
                if (_state == null || !_state.lastSyncClosed.Exists) return 0;
                return _state.lastSyncClosed.standings.myIndex + 1;
            }
        }

        /// <summary>The closed tournament finished in a place that actually pays — so the result is
        /// a Claim and not just a result. A place inside <see cref="TournamentConfig.prizePlaces"/>
        /// with nothing configured for it pays nothing: a claim screen that grants air is worse
        /// than the plain one.</summary>
        public static bool ClosedWonPrize {
            get {
                var cfg = TournamentConfig.Instance;
                int place = ClosedPlace;
                return cfg != null && place >= 1 && place <= cfg.prizePlaces && cfg.PrizeFor(place) != null;
            }
        }

        /// <summary>
        /// The day a tournament OPENED, counted from the unix epoch — the key that ties every
        /// event of one tournament together.
        ///
        /// From the start of the window and never from "now": a 48-hour window crosses two dates,
        /// so a win on the second day would otherwise look like a different tournament entirely.
        ///
        /// An int rather than the id, which is a twenty-character string on every event. Two
        /// tournaments cannot open on the same day inside a 48-hour window, so nothing is lost.
        /// Zero means there was none.
        /// </summary>
        public static int CurrentDay => _dayOf(_state?.lastSyncCurrent);
        public static int ClosedDay => _dayOf(_state?.lastSyncClosed);

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static int _dayOf(TournamentSnapshot snapshot) =>
            snapshot != null && snapshot.Exists ? (int)(snapshot.info.StartUtc.Date - Epoch).TotalDays : 0;

        /// <summary>A prize is sitting there uncollected. The one question both auto-openings ask
        /// — the lobby as it opens, the win flow before the celebration — so neither has to work
        /// it out and they can never disagree about whether to interrupt the player.</summary>
        public static bool PrizeWaiting =>
            !ShowsOffline && Status == ETournamentStatus.Ended && ClosedWonPrize;

        /// <summary>No usable connection — a fact about the device, not about the tournament.</summary>
        public static bool IsOffline => !_isOnline;

        /// <summary>The player has reached the level the tournament opens at. Public because the
        /// sync runner will not talk to the server below it: a tournament nobody can see should
        /// not be running, least of all rolling over every 48 hours — the player would unlock the
        /// feature into whatever was left of one.</summary>
        public static bool IsUnlocked => _exists && _isUnlocked;

        /// <summary>
        /// Offline is what the player should be told about, rather than a <see cref="Status"/>
        /// read out of state that may be hours old.
        ///
        /// Not simply <see cref="IsOffline"/>: no feature and no unlock are settled before a
        /// connection is worth asking about, and a player who has not reached the unlock level
        /// is owed the lock, not a network complaint. That order used to live in the status
        /// ladder, above the line that returned Offline; it lives here now, in one place, so no
        /// caller has to remember it.
        /// </summary>
        public static bool ShowsOffline => IsOffline && _exists && _isUnlocked;

        // ---- what the game calls ---------------------------------------------------------

        /// <summary>A campaign level was won with <paramref name="score"/> left. Counted locally at
        /// once and queued; which tournament it lands in is the server's call (the one running when
        /// it arrives). Called from the win flow, before the win popup.
        /// Each win gets its own id, so it can be sent again freely until the server confirms it.
        /// The queue holds at most <see cref="TournamentConfig.maxPendingWins"/> wins; past that the
        /// oldest are dropped — they belong to a tournament that has closed anyway.
        /// Returns what the climb popup needs: where the player was, where they are now, and
        /// whether there is anything to animate at all.</summary>
        public static TournamentWinResult OnLevelWin(int score) {
            var res = new TournamentWinResult { fromIndex = -1, toIndex = -1 };
            if (!_exists || !_isUnlocked || score <= 0) return res;

            res.fromIndex = MyIndexWithPending;
            bool wasIn = res.fromIndex >= 0;

            _state.pending.Add(new TournamentWin {
                id = Guid.NewGuid().ToString("N"),
                score = score,
                wonAtTicks = MBServerTimeManagerV2.UTCNow.Ticks,
            });

            // Oldest first: a win that old belongs to a tournament that closed long ago anyway.
            while (_state.pending.Count > TournamentConfig.Instance.maxPendingWins) _state.pending.RemoveAt(0);

            _state.Save();

            res.newScore = MyScoreWithPending;
            res.toIndex = MyIndexWithPending;
            // Nothing to animate when the tournament isn't running: the win is queued all the same
            // and counts for the one it reaches (see MyScoreWithPending).
            res.scoreAdded = res.toIndex >= 0 ? score : 0;
            res.joined = !wasIn && res.toIndex >= 0;

            _ = Sync();
            return res;
        }

        /// <summary>Sync now. Scheduling lives in <see cref="MBTournamentSyncRunner"/>, so this
        /// also pushes the next scheduled sync away.</summary>
        public static Task Sync() => MBTournamentSyncRunner.RunNow();

        /// <summary>The Tournament Ended popup's button (Claim / Continue). The rank was already
        /// confirmed during sync, so the prize is granted here and nothing is sent. Marks the result
        /// as shown, so it never pops again — the server keeps returning that closed tournament.
        ///
        /// The grant sits INSIDE the same guard that marks it shown, and both happen before any
        /// animation: a prize cannot be handed out twice however the screen is opened, and a player
        /// who closes the app mid-effect has still been paid. A place that pays nothing lands here
        /// too — it just has no prize to grant.</summary>
        public static void CompleteEnded() {
            if (_state == null || !_state.lastSyncClosed.Exists || _state.closedShown) return;

            var prize = TournamentConfig.Instance?.PrizeFor(ClosedPlace);
            if (prize?.rewards != null)
                foreach (var reward in prize.rewards) reward?.Grant();

            _state.closedShown = true;
            _state.Save();

            // Sent from here because this is the only place a result is ever closed, by any of
            // the four routes in. Reported however it ended - a result nobody looked at counts
            // for the spread of places exactly as much as one that was celebrated.
            Analytics.TournamentEnd(ClosedDay, ClosedPlace,
                                    _state.lastSyncClosed.standings.Me?.score ?? 0, ClosedWonPrize,
                                    _state.lastSyncClosed.info?.difficulty);
        }


        // ---- internal --------------------------------------------------------------------

        /// <summary>There is a tournament feature at all: it was shipped, and the blob is loaded.
        /// Everything else about it is undecidable until both are true.</summary>
        static bool _exists => TournamentConfig.Instance != null && _state != null;

        /// <summary>The player sees the lobby only when the app opens, so the lock card is held
        /// back until a returning player would actually meet it — at level 1 it is just noise.</summary>
        static bool _isRevealed => AppData.LevelIdx.Value + 1 >= TournamentConfig.Instance.revealLevel;

        static bool _isUnlocked => AppData.LevelIdx.Value + 1 >= TournamentConfig.Instance.unlockLevel;

        /// <summary>A confirmed connection AND a trusted clock. Reachability alone lies — a captive
        /// wifi "has network" — so this is the checked one. And without server time the timer and
        /// "is it over" would run on a clock the player can set, so the card goes Offline instead.</summary>
        static bool _isOnline => InternetConnection.Instance != null
                                && InternetConnection.Instance.HasInternet
                                && MBServerTimeManagerV2.IsTimeSynced;
    }
}
