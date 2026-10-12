using Common;
using System;
using UnityEngine;
using UnityEngine.Diagnostics;
using UnityEngine.EventSystems;

namespace qp {
    /// <summary>
    /// Runtime debug panel — IMGUI, so it needs no prefab or scene presence and works in any
    /// scene, on device too. Opened from the settings popup by the switch code once
    /// AppData.DebugMode is on (see MBSettingsPopup). Add a button = add one line in OnGUI.
    /// </summary>
    public class MBDebugWin : MonoBehaviour {

        static MBDebugWin _instance;

        Vector2 _scroll;
        string _levelInput = "";
        string _streakInput = "";
        EventSystem _blockedEventSystem;   // the EventSystem we disabled while we're up

        public static void Open() {
            if (_instance == null) _instance = new GameObject("$DebugWin").AddComponent<MBDebugWin>();
        }

        /// <summary>The short side this panel's sizes were picked against. A screen twice as wide
        /// gets widgets twice as big, so it fills the same share of it everywhere.</summary>
        const float ReferenceShortSide = 480f;

        /// <summary>
        /// Scale from the RESOLUTION, not from Screen.dpi.
        ///
        /// dpi lies: the editor reports the monitor's (around 96, so the panel came out unusably
        /// small against a phone-shaped Game view), and plenty of Android devices report 0 or a
        /// made-up number. The short side is always true, and it is what decides how much room
        /// there is anyway.
        ///
        /// Read every frame because the Game view resizes and a device rotates.
        /// </summary>
        static float Scale =>
            Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / ReferenceShortSide);

        string _scoreInput = "", _placeInput = "";

        /// <summary>
        /// Everything here writes the SERVER's state and then syncs, because the client's copy is
        /// replaced by whatever the server says — setting a score on this side would be undone a
        /// moment later, which is exactly what a score should do.
        ///
        /// The whole panel disappears against a backend that offers no debug hooks, instead of
        /// showing buttons that quietly do nothing.
        /// </summary>
        void TournamentPanel() {
            // Outside the backend check: pulling the network is the device's business, not the
            // tournament's, and it is worth having whatever is behind this window.
            GUILayout.BeginHorizontal();
            GUILayout.Label(InternetConnection.ForceOffline ? "OFFLINE (forced)" : "online",
                            GUILayout.Width(160));
            if (Button(InternetConnection.ForceOffline ? "Go online" : "Go OFFLINE"))
                InternetConnection.ForceOffline = !InternetConnection.ForceOffline;
            GUILayout.EndHorizontal();

            var dbg = TournamentManager.Debug;
            if (dbg == null) { GUILayout.Label("Tournament — this backend has no debug hooks"); return; }

            GUILayout.Label($"Tournament ({TournamentManager.Status}, {TournamentManager.TimeLeftText} left, " +
                            $"score {TournamentManager.MyScoreWithPending}, place {TournamentManager.MyIndexWithPending + 1})");

            GUILayout.BeginHorizontal();
            if (Button("+1h")) Do(() => dbg.SkipTime(TimeSpan.FromHours(1)));
            if (Button("+6h")) Do(() => dbg.SkipTime(TimeSpan.FromHours(6)));
            if (Button("+24h")) Do(() => dbg.SkipTime(TimeSpan.FromHours(24)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _scoreInput = GUILayout.TextField(_scoreInput, GUILayout.Height(44), GUILayout.Width(120));
            if (Button("Set score") && int.TryParse(_scoreInput, out int score)) Do(() => dbg.SetScore(score));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _placeInput = GUILayout.TextField(_placeInput, GUILayout.Height(44), GUILayout.Width(120));
            if (Button("Finish in place") && int.TryParse(_placeInput, out int place)) Do(() => dbg.SetFinalPlace(place));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Button("End now")) Do(dbg.EndNow);
            if (Button("Restart")) Do(dbg.Restart);
            if (Button("CLEAR all")) Do(dbg.Clear);
            GUILayout.EndHorizontal();
        }

        /// <summary>Change the server, then sync — nothing shows up until the client asks.</summary>
        static void Do(Action change) {
            change();
            _ = TournamentManager.Sync();
        }

        void Close() => Destroy(gameObject);

        /// <summary>Escape closes it — and on Android that is the back button, which is the one
        /// thing a player reaches for when a panel is in the way. Handled here rather than with
        /// the other shortcuts because this object only exists while the panel is up, so there
        /// is nothing to ask and nothing to guard.</summary>
        void Update() {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        void OnDestroy() { if (_instance == this) _instance = null; }

        // Everything under the panel is blocked while it's up: uGUI via the EventSystem
        // (IMGUI doesn't go through it, so we stay clickable), the GP board via InputLocks.
        void OnEnable() {
            if (MBGameplay.instance != null) MBGameplay.instance.InputLocks++;
            _blockedEventSystem = EventSystem.current;
            if (_blockedEventSystem != null) _blockedEventSystem.enabled = false;
        }

        void OnDisable() {
            if (MBGameplay.instance != null) MBGameplay.instance.InputLocks--;
            if (_blockedEventSystem != null) { _blockedEventSystem.enabled = true; _blockedEventSystem = null; }
        }

        void OnGUI() {
            float scale = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;

            GUILayout.BeginArea(new Rect(10, 10, w - 20, h - 20), GUI.skin.box);
            GUILayout.Label($"DEBUG | level {AppData.LevelIdx + 1}/{LevelLoader.LevelCount} | attempts {AppData.LevelAttempts.Value} | bones lost {AppData.LastPlayData.bonesLost} | firebase {FirebaseState()}");
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Crashlytics");
            if (Button("Send non-fatal exception")) CDebug.LogError(new Exception("MBDebugWin: non-fatal test"));
            if (Button("CRASH NOW (native)")) Utils.ForceCrash(ForcedCrashCategory.AccessViolation);

            GUILayout.Space(16);
            GUILayout.Label("Boosters");
            foreach (var boost in AppData.Boosts)
                if (Button($"+5 {boost.Key} (now {boost.Value.Value})")) boost.Value.Value += 5;

            GUILayout.Space(16);
            GUILayout.Label("Level");
            if (Button("Level +1")) JumpLevel(+1);
            if (Button("Level -1")) JumpLevel(-1);
            GUILayout.BeginHorizontal();
            _levelInput = GUILayout.TextField(_levelInput, GUILayout.Height(44), GUILayout.Width(120));
            if (Button($"Set level (1-{LevelLoader.LevelCount})") && int.TryParse(_levelInput, out int lvl))
                AppData.LevelIdx.Value = Mathf.Clamp(lvl - 1, 0, Mathf.Max(0, LevelLoader.LevelCount - 1));
            GUILayout.EndHorizontal();
            if (Button("Clear saved board")) AppData.LastPlayData.Invalidate();
            if (Button("Restore bones")) { AppData.LastPlayData.bonesLost = 0; AppData.LastPlayData.Save(); }

            GUILayout.Space(16);
            GUILayout.Label($"Daily Streak (now {DailyStreakManager.Streak}, {DailyStreakManager.Status})");
            GUILayout.BeginHorizontal();
            if (Button("Streak -1")) DailyStreakManager.DebugSetStreak(DailyStreakManager.Streak - 1);
            if (Button("Streak +1")) DailyStreakManager.DebugSetStreak(DailyStreakManager.Streak + 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _streakInput = GUILayout.TextField(_streakInput, GUILayout.Height(44), GUILayout.Width(120));
            if (Button("Set streak") && int.TryParse(_streakInput, out int streak)) DailyStreakManager.DebugSetStreak(streak);
            GUILayout.EndHorizontal();
            if (Button("Last win = yesterday (win to ++)")) DailyStreakManager.DebugSetLastWinYesterday();
            if (Button("Simulate win (streak++ & grant reward)")) {
                DailyStreakManager.DebugSetLastWinYesterday();
                var r = DailyStreakManager.RegisterWin();
                Debug.Log($"[Debug] streak now {r.streak}, reward: {(r.reward != null ? $"{r.reward.type} {r.reward.Label}" : "none")}");
            }

            GUILayout.Space(16);
            var dc = DailyChallengeManager.State;
            GUILayout.Label($"Daily Challenge ({DailyChallengeManager.Status}, day {dc.dayIndex}, tier {dc.tier}, solved {dc.solved}, boards {DailyChallengeManager.BoardsSolved})");
            if (Button("DC: reset today (playable)")) DailyChallengeManager.DebugResetToday();
            if (Button("DC: mark solved today")) DailyChallengeManager.DebugMarkSolvedToday();

            GUILayout.Space(16);
            TournamentPanel();

            GUILayout.Space(16);
            GUILayout.Label("Ads (AppLovin MAX)");
            if (Button("Mediation Debugger")) MaxSdk.ShowMediationDebugger();
            if (Button($"Show Rewarded  (ready: {Ads.IsRewardedReady})"))
                Ads.ShowRewarded("debug", earned =>Debug.Log($"[Debug] rewarded closed — earned: {earned}"));
            if (Button($"Show Interstitial  (ready: {Ads.IsInterstitialReady})"))
                Ads.ShowInterstitial("debug", () =>Debug.Log("[Debug] interstitial closed"));
            if (Button($"Banner: {(Ads.BannerVisible ? "HIDE" : "SHOW")}")) {
                if (Ads.BannerVisible) Ads.HideBanner(); else Ads.ShowBanner();
            }

            GUILayout.Space(16);
            GUILayout.Label("Overlays");
            if (Button($"FPS overlay: {(AppData.ShowFps.Value ? "ON" : "OFF")}")) MBFpsOverlay.Toggle();
            if (Button($"Reaction FX loop (TEMP): {(MBReactionFpsTest.Running ? "ON" : "OFF")}")) MBReactionFpsTest.Toggle();

#if NOTIFICATION_INSTALLER
            GUILayout.Space(16);
            GUILayout.Label("Notifications");
            if (Button("Fire test notification")) NotificationManager.FireTestNotification();
            if (Button($"Fast mode (every X s): {(NotificationManager.DebugFastMode ? "ON" : "OFF")}"))
                NotificationManager.DebugFastMode = !NotificationManager.DebugFastMode;
            GUILayout.Label("Fast mode ON, then press HOME → the whole schedule fires every few seconds.");
#endif

            GUILayout.Space(16);
            if (Button("EXIT DEBUG MODE")) { AppData.DebugMode.Value = false; Close(); }
            if (Button("Close")) Close();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // A jump only moves the pointer — the stale saved attempt is ignored anyway because
        // LastPlayData.forLevelIdx no longer matches.
        static void JumpLevel(int delta) =>
            AppData.LevelIdx.Value = Mathf.Clamp(AppData.LevelIdx.Value + delta, 0, Mathf.Max(0, LevelLoader.LevelCount - 1));

        static bool Button(string label) => GUILayout.Button(label, GUILayout.Height(44));

        static string FirebaseState() {
#if !IGNORE_FIREBASE
            return FirebaseBootstrap.FBAvailable ? "ready" : "NOT READY";
#else
            return "ignored (define)";
#endif
        }
    }
}
