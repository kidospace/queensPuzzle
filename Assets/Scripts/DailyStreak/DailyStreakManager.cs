using Common;
using QueensPuzzle;

namespace qp {

    /// <summary>Where the daily streak stands for THIS player — drives the lobby button.</summary>
    public enum EStreakStatus {
        Offline,   // no trusted server time — the streak can't be verified/advanced
        Active,    // unlocked and online — shows streak + progress
        Broken,    // lost to one missed day and still savable — shows the LOST streak, not 0
    }

    /// <summary>What happened to a repair offer. Watched / Declined end it; AdFailed (the video
    /// failed or was closed early) leaves it open to try again.</summary>
    public enum ERepairResult {
        Watched,
        Declined,
        AdFailed,
    }

    /// <summary>What a win did to the streak — returned by <see cref="DailyStreakManager.RegisterWin"/>
    /// so the win flow can show the right UI.</summary>
    public struct StreakResult {
        public bool advanced;    // the streak went up this call (online, first win today) — show streak UI
        public int previous;     // the streak value before this win (for the old→new reveal)
        public int streak;       // the streak value after this win
        public Reward reward;    // null = no milestone; non-null = milestone reached, show reward UI
        /// <summary>&gt; 0: this win is the first since a streak of that length broke to one
        /// missed day, and nothing has happened to the streak YET. The win flow offers the repair
        /// and calls <see cref="DailyStreakManager.ResolveRepair"/>, which returns the real result.</summary>
        public int repair;
    }

    /// <summary>
    /// Daily streak logic. Online-only: a win counts toward the streak only when server time is
    /// trusted (MBServerTimeManagerV2.IsTimeSynced). A day is the player's LOCAL calendar day —
    /// server time in the device's time zone (<see cref="Today"/>), so it turns at their midnight.
    ///
    /// Everything follows from how many days ago the last win was (<see cref="_daysSinceWin"/>):
    ///   0  won today — today's win is counted, nothing more to do
    ///   1  the streak is alive — today's first win makes it one longer
    ///   2  one day missed — savable with a rewarded video (Broken), or else gone
    ///   3+ gone — the streak shows 0, and the next win starts over at 1
    /// The stored streak is never zeroed: it is the length of the last run, and whether it still
    /// counts is read off the day of the last win. Every cycleLength-th day (7, 14, …) grants a gift.
    ///
    /// REPAIR. Most players who lose a long streak come back the very next day (Oct 2026: 55% of
    /// 7–13-day streaks, 65% of 14+). On that day, before their first win counts, they are offered
    /// a video to keep it: watched, yesterday counts and today's win carries it on (12 → 13).
    /// Only for streaks of at least <see cref="DailyStreakConfig.repairMinStreak"/>, and once per
    /// 7-day stretch of the streak — the bar the player sees.
    ///
    /// Persisted: <see cref="_streak"/>, <see cref="_lastWinDay"/>, <see cref="_repairedAt"/>,
    /// and <see cref="_localDays"/> (the one-time move off UTC days).
    /// </summary>
    public static class DailyStreakManager {

        const string ConfigResource = "DailyStreakConfig";

        static readonly System.DateTime Epoch =
            new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        // ---- persisted ----------------------------------------------------------------

        /// <summary>The length of the last run — current only while the last win was today or
        /// yesterday (see <see cref="Streak"/>).</summary>
        static readonly PlayerPrefsHelper.IntHolder _streak
            = new PlayerPrefsHelper.IntHolder("qp_streak", 0);

        static readonly PlayerPrefsHelper.IntHolder _lastWinDay
            = new PlayerPrefsHelper.IntHolder("qp_streak_last_day", -1);

        /// <summary>The streak value saved last, in the run going now; 0 = none (cleared when a
        /// new run starts). The one thing about repair that cannot be read off the days.</summary>
        static readonly PlayerPrefsHelper.IntHolder _repairedAt
            = new PlayerPrefsHelper.IntHolder("qp_streak_repaired_at", 0);

        /// <summary>1 once <see cref="_lastWinDay"/> counts the player's LOCAL days; 0 = it still
        /// holds a UTC day from a build before (see <see cref="_toLocalDaysOnce"/>).</summary>
        static readonly PlayerPrefsHelper.IntHolder _localDays
            = new PlayerPrefsHelper.IntHolder("qp_streak_local_days", 0);

        // ---- public API -----------------------------------------------------------------

        /// <summary>Called from the win hook (MBGameplay.Win). Online only. Counts today's first
        /// win — one longer, or a new run at 1 — and grants any milestone gift. Returns what
        /// happened so the win flow can show the streak/reward UI.
        ///
        /// If the streak can be saved, this win changes NOTHING yet: it returns
        /// <see cref="StreakResult.repair"/> &gt; 0, the win flow asks the player, and
        /// <see cref="ResolveRepair"/> counts the win once they have answered.</summary>
        public static StreakResult RegisterWin() {
            if (Config == null || !IsOnline || _daysSinceWin == 0)   // off/offline; one win/day
                return new StreakResult { advanced = false, previous = Streak, streak = Streak, reward = null };

            if (CanRepair) return new StreakResult { repair = _streak.Value };

            return _advance(saved: false, declined: false);
        }

        /// <summary>The player answered the repair offer that <see cref="RegisterWin"/> raised:
        /// Watched — yesterday counts, and today's win carries the streak on (12 → 13); Declined —
        /// a new run at 1, as it would have been without the offer. Returns the result to show.</summary>
        public static StreakResult ResolveRepair(ERepairResult result) {
            _toLocalDaysOnce();
            bool saved = result == ERepairResult.Watched;
            // The day can turn while the popup is open. A video watched is honoured anyway — the
            // player paid for it; a decline then is just a plain win.
            if (!saved && !CanRepair) return RegisterWin();

            Analytics.StreakRepair(_streak.Value, result, fromLobby: false);
            if (saved) {
                _repairedAt.Value = _streak.Value;
                _lastWinDay.Value = Today - 1;
            }
            return _advance(saved, declined: !saved);
        }

        /// <summary>
        /// The video was watched from the lobby (the Broken card opens the same offer): the streak
        /// is saved now — yesterday counts, the card is back on its number, and today's first win
        /// carries it on like any other day.
        /// </summary>
        public static void RepairFromLobby() {
            _toLocalDaysOnce();
            if (!CanRepair) return;
            Analytics.StreakRepair(_streak.Value, ERepairResult.Watched, fromLobby: true);
            _repairedAt.Value = _streak.Value;
            _lastWinDay.Value = Today - 1;
        }

        /// <summary>"No thanks" from the lobby: the same answer as after a win — the streak is gone.
        /// The card shows 0, nothing is offered with the next win, and that win starts a new run at 1.</summary>
        public static void DeclineFromLobby() {
            _toLocalDaysOnce();
            if (!CanRepair) return;
            Analytics.StreakRepair(_streak.Value, ERepairResult.Declined, fromLobby: true);
            Analytics.StreakLost(_streak.Value, _daysSinceWin - 1, true);
            _streak.Value = 0;   // reported lost here, so the next win starts at 1 without reporting it again
        }

        /// <summary>The repair popup is on screen, where (the lobby card or after a win), and whether
        /// a video was ready — so a low watch rate can be told apart from ads that never loaded.</summary>
        public static void NoteRepairOffered(bool adReady, bool fromLobby) =>
            Analytics.StreakRepairOffer(_streak.Value, adReady, fromLobby);

        /// <summary>The player pressed watch but the video failed or was closed early; the offer
        /// stays open.</summary>
        public static void NoteRepairAdFailed(bool fromLobby) =>
            Analytics.StreakRepair(_streak.Value, ERepairResult.AdFailed, fromLobby);

        /// <summary>Offline / Broken (a streak lost to one missed day that can still be saved) / Active.</summary>
        public static EStreakStatus Status =>
            !IsOnline ? EStreakStatus.Offline :
            CanRepair ? EStreakStatus.Broken :
            EStreakStatus.Active;

        /// <summary>The streak as it stands: the run while it is alive (last win today or
        /// yesterday), else 0.</summary>
        public static int Streak => _daysSinceWin <= 1 ? _streak.Value : 0;

        /// <summary>The streak that broke and can still be saved — what the Broken card shows
        /// instead of 0. 0 when there is none.</summary>
        public static int LostStreak => CanRepair ? _streak.Value : 0;

        /// <summary>The streak can be saved right now: exactly one day missed, at least
        /// <see cref="DailyStreakConfig.repairMinStreak"/> long, and no save yet in its current
        /// 7-day stretch (streaks 1–7 are one stretch, 8–14 the next — the stretches the bar fills).</summary>
        public static bool CanRepair {
            get {
                if (Config == null || !IsOnline || _daysSinceWin != 2) return false;
                int s = _streak.Value;
                int at = _repairedAt.Value;
                return s >= Config.repairMinStreak
                    && (at <= 0 || (at - 1) / CycleLength != (s - 1) / CycleLength);
            }
        }

        /// <summary>Debug only (MBDebugWin) — force the streak to a value and pin the last win to
        /// today.</summary>
        public static void DebugSetStreak(int value) {
            _toLocalDaysOnce();
            _streak.Value = value < 0 ? 0 : value;   // streak has no upper bound — it climbs forever
            _lastWinDay.Value = Today;
        }

        /// <summary>Debug only — pretend the last win was yesterday, so the next real win advances
        /// the streak by one instead of being treated as already-won-today.</summary>
        public static void DebugSetLastWinYesterday() { _toLocalDaysOnce(); _lastWinDay.Value = Today - 1; }

        /// <summary>Debug only — back after missing one day: the streak (at least the repair
        /// minimum) last won the day before yesterday, no save used in this stretch. The lobby
        /// shows the Broken card and the next win offers the repair.</summary>
        public static void DebugMakeRepairable() {
            _toLocalDaysOnce();
            int min = Config != null ? Config.repairMinStreak : 3;
            if (_streak.Value < min) _streak.Value = min;
            _lastWinDay.Value = Today - 2;
            _repairedAt.Value = 0;
        }

        /// <summary>The configured sprite for a boost type (reward UI), or null.</summary>
        public static UnityEngine.Sprite SpriteFor(EBoostType type) => Config != null ? Config.SpriteFor(type) : null;

        /// <summary>Days per reward cycle (from config; 7 if config missing). The streak keeps
        /// climbing forever; a random gift drops every time it hits a multiple of this.</summary>
        public static int CycleLength => Config != null ? Config.cycleLength : 7;

        // ---- helpers --------------------------------------------------------------------

        /// <summary>
        /// A trusted clock AND a live connection.
        ///
        /// The clock is what stops the streak being farmed by moving the device date. The
        /// connection is a product call: nothing is monetisable without one, so days are not
        /// handed out — and rewards not granted — while there is no line.
        /// </summary>
        static bool IsOnline => MBServerTimeManagerV2.IsTimeSynced
                             && InternetConnection.Instance != null
                             && InternetConnection.Instance.HasInternet;

        /// <summary>Days since the last win — the one number the whole streak is read from.</summary>
        static int _daysSinceWin {
            get { _toLocalDaysOnce(); return Today - _lastWinDay.Value; }
        }

        /// <summary>
        /// A build before this one stored the last win as a UTC day, and its local day can be one
        /// either side of it — read as-is, an Israeli player's 1 a.m. win would suddenly look like a
        /// missed day and break the streak on the day they update. So the first time, a last win
        /// up to two days back counts as yesterday: nobody loses a streak to the update, and the
        /// worst it does is hand someone one day. Waits for trusted time, like everything else.
        /// </summary>
        static void _toLocalDaysOnce() {
            if (_localDaysDone || !MBServerTimeManagerV2.IsTimeSynced) return;
            _localDaysDone = true;
            if (_localDays.Value == 1) return;

            int last = _lastWinDay.Value, today = Today;
            if (last >= 0 && today - last <= 2) _lastWinDay.Value = today - 1;
            _localDays.Value = 1;
            CDebug.CrashLog($"[streak] last win moved to local days: {last} -> {_lastWinDay.Value} (today {today})");
        }
        static bool _localDaysDone;

        // Today's win, counted: one longer while the streak is alive, else a new run at 1 (and the
        // old run is reported lost — with how many days were missed, and whether the player turned
        // down saving it). `saved` = this comes straight after a repair.
        static StreakResult _advance(bool saved, bool declined) {
            int prev = Streak;
            int s = prev + 1;
            if (s == 1) {
                if (_streak.Value > 0) Analytics.StreakLost(_streak.Value, _daysSinceWin - 1, declined);
                _repairedAt.Value = 0;   // a new run: its first stretch has its save
            }

            _streak.Value = s;
            _lastWinDay.Value = Today;

            Reward reward = _milestoneRewardFor(s);   // null unless s is a milestone day
            reward?.Grant();

            Analytics.StreakAdvance(s, reward != null, saved);
            return new StreakResult { advanced = true, previous = prev, streak = s, reward = reward };
        }

        // The gift for a given streak day: a random reward every Nth day (7, 14, 21, …), null otherwise.
        static Reward _milestoneRewardFor(int streak) =>
            Config != null && streak > 0 && streak % CycleLength == 0 ? Config.RewardRandom() : null;

        /// <summary>
        /// The player's day: the trusted server time, shifted by the device's time zone, counted
        /// from the epoch. Local and not UTC because the UTC day turns at 8 p.m. on the US east
        /// coast — 36% of US streaks that broke to "one missed day" (Sep–Oct 2026) were players who
        /// had played on two days running by their own clock. The time still comes from the
        /// server, so changing the device DATE does nothing; changing the time ZONE moves the day
        /// by a few hours at most, which is not worth guarding against.
        /// </summary>
        static int Today {
            get {
                var utc = System.DateTime.SpecifyKind(MBServerTimeManagerV2.UTCNow, System.DateTimeKind.Utc);
                return (int)((utc + _localOffset(utc)).Date - Epoch).TotalDays;
            }
        }

        // The device's offset from UTC at that moment (DST included), within the real ±14h; UTC if
        // the platform cannot say.
        static System.TimeSpan _localOffset(System.DateTime utc) {
            try {
                var offset = System.TimeZoneInfo.Local.GetUtcOffset(utc);
                return offset.TotalHours >= -14 && offset.TotalHours <= 14 ? offset : System.TimeSpan.Zero;
            }
            catch { return System.TimeSpan.Zero; }
        }

        static DailyStreakConfig _config;
        static DailyStreakConfig Config {
            get {
                if (_config == null) {
                    _config = UnityEngine.Resources.Load<DailyStreakConfig>(ConfigResource);
                    if (_config == null)
                        CDebug.LogError($"[DailyStreakManager] Resources/{ConfigResource}.asset not found - streak is off.");
                }
                return _config;
            }
        }
    }
}
