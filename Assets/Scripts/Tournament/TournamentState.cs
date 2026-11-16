using System;
using System.Collections.Generic;
using Common;

namespace qp {

    /// <summary>
    /// The client's tournament blob (PlayerPrefs JSON). Holds only what can't be derived: the two
    /// snapshots the last sync brought back (the tournament running now, and the last closed one
    /// still waiting for its popup) and the wins not sent yet. The player's score / index / joined are derived
    /// from these (see <see cref="TournamentManager"/>). Mutated only by TournamentManager.
    /// Note: JsonUtility never stores null for nested classes — "none" is a snapshot with no id.
    /// </summary>
    [Serializable]
    public class TournamentState {

        static readonly PlayerPrefsHelper.ObjectHolder<TournamentState> _holder
            = new PlayerPrefsHelper.ObjectHolder<TournamentState>("qp_tournament_state");

        /// <summary>The tournament running now + the player's table in it, exactly as the last
        /// sync returned it.</summary>
        public TournamentSnapshot lastSyncCurrent = new TournamentSnapshot();

        /// <summary>The last closed tournament the player took part in, with its final table, as a
        /// sync returned it — what the Tournament Ended popup shows.</summary>
        public TournamentSnapshot lastSyncClosed = new TournamentSnapshot();

        /// <summary>The Tournament Ended popup for <see cref="lastSyncClosed"/> was already shown.
        /// The only thing here the server can't tell us: it keeps sending that closed tournament on
        /// every sync, so without this the popup would come back every 30 seconds. Reset by a sync
        /// that brings a different closed tournament.</summary>
        public bool closedShown;

        /// <summary>Wins the server hasn't confirmed yet (offline / server down / in flight).
        /// Every sync sends all of them; each carries its own id, so resending is free and a win
        /// leaves this list only once the server says it holds it.</summary>
        public List<TournamentWin> pending = new List<TournamentWin>();

        /// <summary>
        /// The player's last day of play: every win (daily challenge included, as the tournament
        /// counts them) in the 24 hours up to their latest one. Kept from the very first level,
        /// tournament unlocked or not, because a player's FIRST tournament is built around it —
        /// it is the only thing that says how much this player plays before they have finished a
        /// tournament of their own (see <see cref="RecentPoints"/>).
        /// </summary>
        public List<PlayedWin> lastDay = new List<PlayedWin>();

        public static TournamentState Load() => _holder.Value ?? new TournamentState();

        public void Save() => _holder.Save(this);

        public static void Delete() => _holder.Delete();

        // ---- reading the blob ---------------------------------------------------------

        /// <summary>The player's score as the server confirmed it in the current tournament.</summary>
        public int ConfirmedScore => lastSyncCurrent.standings.Me?.score ?? 0;

        /// <summary>Score of the wins not sent yet.</summary>
        public int PendingScore {
            get {
                int sum = 0;
                foreach (var w in pending) sum += w.score;
                return sum;
            }
        }

        // ---- the last day of play --------------------------------------------------------

        static readonly long DayTicks = TimeSpan.FromHours(24).Ticks;

        /// <summary>Remember a win, and forget whatever is more than a day older than it.</summary>
        public void RecordPlay(int points, long atTicks) {
            if (lastDay == null) lastDay = new List<PlayedWin>();
            lastDay.Add(new PlayedWin { atTicks = atTicks, points = points });
            lastDay.RemoveAll(w => w.atTicks <= atTicks - DayTicks);
        }

        /// <summary>
        /// What the player won in the last 24 hours — or, with nothing in them, in their last day
        /// of play (the 24 hours up to their latest win). 0 for a player who has never won.
        ///
        /// The last 24 hours is the best single guess at what a player will score in their first
        /// tournament (Nov 2026: shorter windows predicted worse, and sessions, pace or time of
        /// day added nothing). A player back after a break is guessed from the day they last
        /// played, which predicted them twice as well as the population median did.
        /// </summary>
        public int RecentPoints(long nowTicks) {
            if (lastDay == null || lastDay.Count == 0) return 0;
            int inLast24 = 0, all = 0;
            foreach (var w in lastDay) {
                all += w.points;
                if (w.atTicks > nowTicks - DayTicks) inLast24 += w.points;
            }
            return inLast24 > 0 ? inLast24 : all;
        }
    }

    /// <summary>One win in <see cref="TournamentState.lastDay"/>: when (UTC ticks) and how many points.</summary>
    [Serializable]
    public class PlayedWin {
        public long atTicks;
        public int points;
    }
}
