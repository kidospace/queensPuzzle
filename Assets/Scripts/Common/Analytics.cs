using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// All game analytics goes through here. Every report is both a Firebase event and a
    /// Crashlytics breadcrumb, so crash reports always show what the player was doing.
    /// </summary>
    public static class Analytics {

        /// <summary>Breadcrumb only — attached to the next crash report (and the Editor console).</summary>
        public static void CrashLog(string message) => CDebug.CrashLog(message);

        /// <summary>
        /// Every analytics event leaves through here, which is why the debug-mode check is here
        /// and in no other place. Adding an event means calling this, and it is covered.
        ///
        /// <see cref="CrashLog"/> is the other door and is never closed: breadcrumbs are what
        /// make a crash report readable, and a crash during a test session is the one worth
        /// reading.
        /// </summary>
        static void Send(string name) {
            if (!EventsOn) return;
            CDebug.Log(name);
        }

#if !IGNORE_FIREBASE
        static void Send(string name, params Firebase.Analytics.Parameter[] parameters) {
            if (!EventsOn) return;
            CDebug.Log(name, parameters);
        }
#endif

        public static void ScreenOpen(string screen) {
            CrashLog($"[screen] open {screen}");
        }

        public static void ScreenClose(string screen) {
            CrashLog($"[screen] close {screen}");
        }

        // ---- game flow: Firebase events. The level id and attempt number are DERIVED here —
        // daily runs report the day index + the day's attempts, campaign runs the level index +
        // AppData.LevelAttempts — so no call site can ever send the wrong pair.
        //
        // CONTRACT for callers: fire the event while the statics still describe the thing the
        // event is about — i.e. BEFORE advancing progress (LevelIdx++), BEFORE invalidating /
        // stashing LastPlayData, and BEFORE ExitDaily; AFTER LoadLevel, attempt bumps and
        // counter bumps. Win()/Fail() in MBGameplay document the exact order.

        static int LevelIdx => DailyChallengeManager.InDailyRun
            ? DailyChallengeManager.DayIndex : AppData.LevelIdx.Value;
        static int Attempts => DailyChallengeManager.InDailyRun
            ? DailyChallengeManager.State.attempts : AppData.LevelAttempts.Value;

        // Campaign level wins we mark as conversion events in the ad networks / Firebase.
        // 1-based level numbers; the very first level is lvl_win_1.
        // 12 = one stage past StartShowInterAtLevel (11) — "survived the interstitials" signal.
        static readonly int[] WinMilestones = { 1, 2, 3, 5, 11, 12, 21, 30 };

        public static void GameStart() {
            GameEvent("game_start");
            AppLovinEvent("level_start", AppData.LevelIdx.Value + 1);
        }

        public static void GameWin() {
            // Which tournament this win landed in. A tournament is always running for an
            // unlocked player, so there is no separate "tournament win" to send - everything
            // about play inside a tournament is a query over game_win grouped by this.
            GameEvent("game_win", "t_day", TournamentManager.CurrentDay);
            // Milestone conversion events — campaign only (daily day indices would pollute them).
            // GameWin fires before LevelIdx++, so LevelIdx.Value is the just-solved level (0-based).
            if (!DailyChallengeManager.InDailyRun) {
                int levelNum = AppData.LevelIdx.Value + 1;
                if (System.Array.IndexOf(WinMilestones, levelNum) >= 0)
                    GameEvent("lvl_win_" + levelNum);
            }
            AppLovinEvent("level_complete", AppData.LevelIdx.Value + 1);
            if (AppData.LevelIdx.Value == 0) AppLovinEvent("tutorial_complete");
        }

        public static void GameLose() {
            GameEvent("game_lose");
        }

        /// <summary>The level's play clock in whole seconds — what lvl_time_sec reports, and what the
        /// level trace stamps each move with (daily: the day's timer; campaign: the per-level counter).</summary>
        public static int PlayTimeSec => DailyChallengeManager.InDailyRun
            ? (int)DailyChallengeManager.State.timeSec : AppData.LevelTimeSec.Value;

        /// <summary>
        /// `lvl_trace` — the attempt move by move (<see cref="qp.LevelTrace"/>), right after its game_win
        /// or game_lose. Only what joins it to that event (same user, lvl_idx, daily, win, a moment
        /// apart) and what decodes it (level_hash → the board, tr_v → the format); everything else is
        /// on game_win/lose.
        /// The packed trace goes out in events of up to 960 chars (LevelTrace.EventChars), at most
        /// LevelTrace.MaxEvents of them, all in this frame: tr_part (0, 1, …) of tr_parts. Inside an
        /// event it is cut into tr_0 … tr_9, 96 chars each. Joined back: the parts by tr_part, the
        /// pieces by their number. A lose that is continued sends again at the attempt's real end,
        /// with all of it — the last one is whole.
        /// </summary>
        public static void SendTrace(bool win, string packed) {
            packed ??= "";
            CrashLog($"[game] lvl_trace {(win ? "win" : "lose")} level {LevelIdx} | {packed}");
#if !IGNORE_FIREBASE
            int size = qp.LevelTrace.EventChars;
            int parts = System.Math.Max(1, (packed.Length + size - 1) / size);
            for (int part = 0; part < parts; part++) {
                string piece = packed.Length > part * size
                    ? packed.Substring(part * size, System.Math.Min(size, packed.Length - part * size)) : "";
                var ps = new System.Collections.Generic.List<Firebase.Analytics.Parameter> {
                    new Firebase.Analytics.Parameter("lvl_idx", LevelIdx),
                    new Firebase.Analytics.Parameter("daily", DailyChallengeManager.InDailyRun ? 1 : 0),
                    new Firebase.Analytics.Parameter("win", win ? 1 : 0),
                    new Firebase.Analytics.Parameter("level_hash", LevelLoader.CurrentLevelHash),
                    // the trace format (LevelTrace.Version) — a decoder picks its reader by it, not by app version
                    new Firebase.Analytics.Parameter("tr_v", qp.LevelTrace.Version),
                    new Firebase.Analytics.Parameter("tr_part", part),
                    new Firebase.Analytics.Parameter("tr_parts", parts),
                };
                const int chunk = 96;   // Firebase keeps 100 chars of a string parameter
                for (int i = 0, n = 0; i < piece.Length; i += chunk, n++)
                    ps.Add(new Firebase.Analytics.Parameter("tr_" + n,
                        piece.Substring(i, System.Math.Min(chunk, piece.Length - i))));
                Send("lvl_trace", ps.ToArray());
            }
#else
            Send("lvl_trace");
#endif
        }

        /// <summary>Breadcrumb for the cold start, from the boot stage. The launch EVENT is
        /// `pd_session_start` in FirebaseLaunch, which runs once Firebase is up.</summary>
        public static void SessionStart() => CrashLog($"[session] start #{UserData.Instance.Sessions}");

        /// <summary>
        /// Firebase side of the launch — run from the boot stage AFTER Firebase is up (logs before
        /// that are dropped). `pd_session_start` is the launch event retention is derived from (the
        /// name `session_start` is reserved by Firebase), and the user properties ride on every
        /// BigQuery row so it joins to our users without going through GAID.
        /// </summary>
#if !IGNORE_FIREBASE
        public static void FirebaseLaunch() {

            if (!FirebaseBootstrap.FBAvailable) return;
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty("uid", _userId);
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty("first_version", _firstVersion.ToString());

            // The same id on every crash, so a crash in the Crashlytics BigQuery export (user.id)
            // joins to this player's events through the `uid` property above.
            Firebase.Crashlytics.Crashlytics.SetUserId(_userId);
            SetSourceProperty(AppData.SingularSource.Value);

            // The user properties above stay: they tag the device and Crashlytics needs the id to
            // attribute a crash. The session COUNT is a different thing - a day of testing would
            // be read as a day of players coming back.
            Send("pd_session_start",
                new Firebase.Analytics.Parameter("session", UserData.Instance.Sessions),
                new Firebase.Analytics.Parameter("lvl_idx", AppData.LevelIdx.Value));
        }

        /// <summary>Install network as a Firebase user property — at launch, and again when the
        /// Singular attribution callback resolves it (it can land after the launch stage).</summary>
        public static void SetSourceProperty(SingularSource source) {

            if (!FirebaseBootstrap.FBAvailable || string.IsNullOrEmpty(source?.network)) return;
            var network = source.network.Length > 36 ? source.network.Substring(0, 36) : source.network;   // Firebase value cap
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty("src_network", network);
        }
#endif
        // The user identity FirebaseLaunch reports, read once on the main thread.
        //
        // Both values come from PlayerPrefs (UserData.FirstVersion, UserID.GetUserIDLocal()),
        // which is a Unity API and not thread-safe, and GetUserIDLocal is the dangerous one: it
        // caches into a plain static with no memory barrier, so a thread that misses the main
        // thread's write falls through to the "no id yet" branch, MINTS A NEW GUID and
        // PlayerPrefs.Save()s it over the real one — silently changing the user's identity
        // mid-session and breaking every user_id join. Captured at boot, never read live.
        static string _userId;
        static int _firstVersion;

        /// <summary>
        /// Whether game events actually reach Firebase.
        ///
        /// Debug mode turns them OFF: a day of testing - skipping levels, forcing places, ending
        /// tournaments - would otherwise land in the same stream the real numbers are read from,
        /// and nothing downstream can tell a developer's win from a player's. Switched back on
        /// from the debug window when a run is meant to be measured.
        ///
        /// Only the automatic events to Firebase. Crashlytics is never touched: breadcrumbs are
        /// what make a crash report readable, and a crash during a test session is exactly the one
        /// worth reading. An explicit send from the debug panel goes out regardless too - someone
        /// pressing that button is asking for it on purpose.
        /// </summary>
        public static bool EventsOn => !AppData.DebugMode.Value || _forceEvents;

        /// <summary>Set from the debug window — send anyway, debug mode or not.</summary>
        public static bool ForceEvents {
            get => _forceEvents;
            set { _forceEvents = value; CrashLog($"[analytics] events {(EventsOn ? "ON" : "OFF")}"); }
        }
        static bool _forceEvents;

        /// <summary>Main thread, from MBStartup, before any SDK can fire a callback.</summary>
        public static void CaptureCommon() {
            _firstVersion = UserData.Instance.FirstVersion;
            _userId       = Common.UserID.GetUserIDLocal();
        }

        /// <summary>A boost the player actually used ("hint" / "queen" / "undo") — counters already bumped.</summary>
        public static void BoostUsed(string boost) {
            GameEvent("boost_used", "boost", boost);
            AppLovinEvent("use_prop");
        }

        /// <summary>The lose popup is offering the rewarded revive — once per popup show.</summary>
        public static void RewardedOpportunity() => AppLovinEvent("rewarded_ad_opportunity");

        // AppLovin in-game events (MaxSdk.TrackEvent, predefined names) — signals for their models.
        // Campaign only: nothing is sent during a daily run.
        // Reused: TrackEvent serializes it to JSON synchronously, and every caller is on the main thread.
        static readonly System.Collections.Generic.Dictionary<string, string> _appLovinData = new();
        static void AppLovinEvent(string name, int? value = null) {
            if (DailyChallengeManager.InDailyRun) return;
            CrashLog($"[applovin] {name}" + (value.HasValue ? $" value={value}" : ""));
            _appLovinData.Clear();
            if (value.HasValue) _appLovinData["value"] = value.Value.ToString();
            MaxSdk.TrackEvent(name, _appLovinData);
        }

        /// <summary>The fail-continue grant (later: video/coins) — its own event stream, not a boost.</summary>
        public static void LivesAdded(int amount) => GameEvent("lives_added", "amount", amount);

        /// <summary>A booster earned by watching a rewarded ad (no boost left → video).</summary>
        public static void BoostEarned(string boost) => GameEvent("boost_earned", "boost", boost);

        /// <summary>
        /// A tournament the player took part in has closed and been marked seen.
        ///
        /// <paramref name="place"/> is the one number this feature cannot be judged without: the
        /// bots are built from recorded players and multipliers picked by hand, and only the
        /// spread of final places says whether that guess was fair.
        /// </summary>
        public static void TournamentEnd(int day, int place, int score, bool prize, string difficulty) {
            CrashLog($"[tournament] end day {day} place {place} score {score} prize {(prize ? 1 : 0)} {difficulty}");
#if !IGNORE_FIREBASE
            Send("tournament_end",
                new Firebase.Analytics.Parameter("t_day", day),
                new Firebase.Analytics.Parameter("t_place", place),
                new Firebase.Analytics.Parameter("t_score", score),
                new Firebase.Analytics.Parameter("t_prize", prize ? 1 : 0),
                // The difficulty the group was drawn from. Without it a result is unreadable: a
                // 7th place in an Easy group and a 7th in a Hard one are different events, and the
                // comparison between them is the only way to tell whether the prize moved anyone.
                new Firebase.Analytics.Parameter("t_difficulty",
                    string.IsNullOrEmpty(difficulty) ? "?" : difficulty));
#else
            Send("tournament_end");
#endif
        }

        /// <summary>
        /// A prize actually collected, one count per boost.
        ///
        /// Counts and not a written-out list: "how much did this feature put into the economy
        /// last month" has to be SUM(t_queen), not a string every query has to take apart first.
        /// Sent rather than derived from the place, because the prize table will change and an
        /// event that can only be read against today's config stops being readable.
        /// </summary>
        public static void TournamentClaim(int day, int place, int undo, int hint, int queen) {
            CrashLog($"[tournament] claim day {day} place {place} undo {undo} hint {hint} queen {queen}");
#if !IGNORE_FIREBASE
            Send("tournament_claim",
                new Firebase.Analytics.Parameter("t_day", day),
                new Firebase.Analytics.Parameter("t_place", place),
                new Firebase.Analytics.Parameter("t_undo", undo),
                new Firebase.Analytics.Parameter("t_hint", hint),
                new Firebase.Analytics.Parameter("t_queen", queen));
#else
            Send("tournament_claim");
#endif
        }

        // TEMP — review prepare timing probe, read via BigQuery. REMOVE after the measurement.
        public static void ReviewPrepareTime(int ms, bool prepared) {
            CrashLog($"[review] prepare took {ms} ms (prepared={prepared})");
#if !IGNORE_FIREBASE
            Send("review_prepare_time", new Firebase.Analytics.Parameter("gr_ms", ms),
                                              new Firebase.Analytics.Parameter("gr_prepared", prepared ? 1 : 0));
#endif
        }

        // every game event carries the level, the attempt and the attempt's counters;
        // extraKey/extraVal (optional) adds the event's own parameter (boost name, amount, ...)
        static void GameEvent(string name, string extraKey = null, object extraVal = null) {
            var d = AppData.LastPlayData;
            bool daily = DailyChallengeManager.InDailyRun;
            int levelIdx = LevelIdx;
            int attempts = Attempts;
            var extra = extraKey != null ? $" {extraKey}={extraVal}" : "";
            int timeSec = daily ? (int)DailyChallengeManager.State.timeSec : AppData.LevelTimeSec.Value;
            CrashLog($"[game] {name}{extra} level {levelIdx} set {LevelLoader.CurrentLevelSetId} pack {LevelLoader.CurrentPackIndex} hash {LevelLoader.CurrentLevelHash} attempt {attempts} | hints {d.hintsUsed} queens {d.queenBoostsUsed} undos {d.undosUsed} lives+ {d.livesAdded} bones- {d.bonesLost} | time {timeSec}s");
#if !IGNORE_FIREBASE
            var ps = new System.Collections.Generic.List<Firebase.Analytics.Parameter> {
                new Firebase.Analytics.Parameter("lvl_idx", levelIdx),
                new Firebase.Analytics.Parameter("pack_idx", LevelLoader.CurrentPackIndex),
                new Firebase.Analytics.Parameter("level_set_id", LevelLoader.CurrentLevelSetId),
                new Firebase.Analytics.Parameter("level_hash", LevelLoader.CurrentLevelHash),
                new Firebase.Analytics.Parameter("lvl_attempts", attempts),
                new Firebase.Analytics.Parameter("daily", daily ? 1 : 0),
            };
            ps.AddRange(d.ToParams());
            // accumulated active solve time — final on game_win (daily: OnSolved runs first;
            // campaign: _ready is false at Win, so the clock has already stopped)
            ps.Add(new Firebase.Analytics.Parameter("lvl_time_sec", timeSec));
            if (extraKey != null)
                ps.Add(extraVal is int i ? new Firebase.Analytics.Parameter(extraKey, i)
                                         : new Firebase.Analytics.Parameter(extraKey, extraVal.ToString()));
            Send(name, ps.ToArray());
#else
            Send(name);
#endif
        }
    }
}
