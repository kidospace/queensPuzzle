using Singular;
using System;
using System.Collections;
using UnityEngine;

namespace qp {

    // AppLovin MAX wrapper: banner + interstitial + rewarded, with auto-load/retry and
    // impression-level ad-revenue forwarding to Singular (+ Firebase). Nothing here is wired to
    // gameplay yet — callers use ShowRewarded / ShowInterstitial / ShowBanner / HideBanner.
    //
    // MAX must be initialized first (MaxBoot); Init() only loads ads, it never blocks the boot.
    public static class Ads {

        // ---- ad unit IDs (from the AppLovin dashboard) ----
        const string RewardedAndroid     = "4380e51f1bc2f8ed";
        const string RewardedIOS         = "0882b282111db249";
        const string InterstitialAndroid = "72a837c75ecbf3b6";
        const string InterstitialIOS     = "7e20cec71f4a2f13";
        const string BannerAndroid       = "265b0eafdc3e0341";
        const string BannerIOS           = "f0afe802fa7b4a9b";

#if UNITY_IOS
        static readonly string RewardedId = RewardedIOS, InterstitialId = InterstitialIOS, BannerId = BannerIOS;
#else
        static readonly string RewardedId = RewardedAndroid, InterstitialId = InterstitialAndroid, BannerId = BannerAndroid;
#endif

        const int MaxRetryPow = 6;           // backoff caps at 2^6 = 64s
        static readonly Color BannerBg = new Color32(0xDF, 0xF0, 0xF5, 0xFF);   // #DFF0F5 — the game's banner strip

        static AdsRunner _runner;
        static readonly object _singularGate = new object();   // see OnRevenuePaid
        static int _rewardedRetry, _interstitialRetry;
        static bool _rewardedEarned;
        static Action<bool> _onRewardedDone;
        static Action _onInterstitialClosed;
        static bool _bannerCreated;

        // Fullscreen ads play their own audio (iOS expects the game to go silent), so ALL game
        // audio is paused during the show. The previous pause state is saved and restored —
        // never blindly set to false, something else may have paused audio first.
        static bool _audioPausedBefore;

        static void PauseGameAudio() { _audioPausedBefore = AudioListener.pause; AudioListener.pause = true; }
        static void RestoreGameAudio() { AudioListener.pause = _audioPausedBefore; }

        // Loaded-state tracked by us (OnAdLoaded sets, show/load-fail clears) so ready polls
        // never call into the SDK — MaxSdk.Is*Ready logs when nothing was loaded, and it's a
        // JNI hop that UI code tends to poll every frame.
        static bool _rewardedLoaded, _interstitialLoaded;

        public static bool IsRewardedReady => Usable(RewardedId) && _rewardedLoaded;
        public static bool IsInterstitialReady => Usable(InterstitialId) && _interstitialLoaded;

        // Boot task (stage after MAX): subscribe callbacks and kick off loading. Returns
        // immediately — ad loading happens in the background, the boot never waits on it.
        public static void Init() {
#if UNITY_EDITOR
            // Editor stubs are fixed-size debug prefabs (the banner one is a hardcoded 168px strip),
            // not a preview of the real ad — they only misrepresent the layout. Must run before
            // CreateBanner(), which is where the stub gets instantiated.
            MaxSdk.DisableStubAds();
#endif
            if (_runner == null) {
                var go = new GameObject("$Ads");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<AdsRunner>();
            }

            _ = MBAdCurtain.Instance;   // instantiate the curtain prefab now, not mid-show

            // Revenue → Singular (+ Firebase) for every format (Singular's documented MAX hook).
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRevenuePaid;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnRevenuePaid;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnRevenuePaid;

            // ...and the price of the first loaded ads of every format (see OnLoadedPrice).
            LoadLoadedCounts();
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnLoadedPrice;
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnLoadedPrice;
            MaxSdkCallbacks.Banner.OnAdLoadedEvent += OnLoadedPrice;

            WireRewarded();
            WireInterstitial();

            if (Usable(RewardedId)) MaxSdk.LoadRewardedAd(RewardedId);
            if (Usable(InterstitialId)) MaxSdk.LoadInterstitial(InterstitialId);
            CreateBanner();

            if (!Usable(RewardedId) || !Usable(InterstitialId) || !Usable(BannerId))
                Debug.LogWarning("[Ads] Some ad unit IDs are still placeholders — fill them in Ads.cs. Those formats won't load.");
        }

        // ================== rewarded ==================

        static void WireRewarded() {
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent       += (id, info) => { _rewardedLoaded = true; _rewardedRetry = 0; };
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent   += (id, err)  => { _rewardedLoaded = false; RetryLoad(ref _rewardedRetry, () => MaxSdk.LoadRewardedAd(RewardedId)); };
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (id, reward, info) => _rewardedEarned = true;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent    += (id, info) => MBAdCurtain.Instance.AdDisplayed();
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (id, err, info) => FinishRewarded();
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent       += (id, info) => FinishRewarded();
        }

        /// <summary>Reusable "watch to earn": show a rewarded ad and run onEarned only if the
        /// reward was actually granted. Use anywhere a rewarded video grants something (boosters,
        /// bones, …).</summary>
        public static void WatchToEarn(string placement, Action onEarned) => ShowRewarded(placement, earned => { if (earned) onEarned?.Invoke(); });

        /// <summary>Show a rewarded ad. onDone(true) if the reward was earned, else onDone(false).
        /// `placement` names where in the game it was shown — MAX echoes it back on the revenue
        /// callback (AdInfo.Placement), so every impression's revenue carries its source.</summary>
        public static void ShowRewarded(string placement, Action<bool> onDone) {
            if (!IsRewardedReady) { onDone?.Invoke(false); return; }

            // our flag can't see expiry — one SDK check right before the show (a load was
            // definitely requested by now, so MAX won't log the "no load" warning)
            if (!MaxSdk.IsRewardedAdReady(RewardedId)) {
                _rewardedLoaded = false;
                MaxSdk.LoadRewardedAd(RewardedId);
                onDone?.Invoke(false);
                return;
            }

            _onRewardedDone = onDone;
            _rewardedEarned = false;
            _rewardedLoaded = false;   // consumed — the next OnAdLoaded flips it back
            // Curtain first: MAX's hidden callback can fire late, and without the black cover the
            // player would glimpse this screen again before the callback swaps to the next one.
            MBAdCurtain.Instance.FadeIn(() => {
                PauseGameAudio();
                MaxSdk.ShowRewardedAd(RewardedId, placement);
            }, "rewarded");
        }

        static void FinishRewarded() {
            RestoreGameAudio();
            var cb = _onRewardedDone; _onRewardedDone = null;
            bool earned = _rewardedEarned; _rewardedEarned = false;
            MaxSdk.LoadRewardedAd(RewardedId);   // preload the next one
            cb?.Invoke(earned);                  // runs under the curtain (scene load / popup swap)
            DropCurtainSoon();
        }

        // ================== interstitial ==================

        static float _lastInterClosedAt = -9999f;   // realtime of the last interstitial CLOSE

        /// <summary>True when an interstitial is loaded AND the cooldown since the last one closed
        /// has elapsed. The level gate is the caller's job (GameConfig.StartShowInterAtLevel).</summary>
        public static bool CanShowInterstitial =>
            IsInterstitialReady && Time.realtimeSinceStartup - _lastInterClosedAt >= GameConfig.InterCooldownSec;

        static void WireInterstitial() {
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent        += (id, info) => { _interstitialLoaded = true; _interstitialRetry = 0; };
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent    += (id, err)  => { _interstitialLoaded = false; RetryLoad(ref _interstitialRetry, () => MaxSdk.LoadInterstitial(InterstitialId)); };
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent     += (id, info) => MBAdCurtain.Instance.AdDisplayed();
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (id, err, info) => FinishInterstitial(displayed: false);
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent        += (id, info) => FinishInterstitial(displayed: true);
        }

        /// <summary>Show an interstitial; onClosed fires when it's dismissed (or immediately if none ready).
        /// `placement` — same as ShowRewarded.</summary>
        public static void ShowInterstitial(string placement, Action onClosed = null) {
            if (!IsInterstitialReady) { onClosed?.Invoke(); return; }

            // same expiry guard as ShowRewarded
            if (!MaxSdk.IsInterstitialReady(InterstitialId)) {
                _interstitialLoaded = false;
                MaxSdk.LoadInterstitial(InterstitialId);
                onClosed?.Invoke();
                return;
            }

            _onInterstitialClosed = onClosed;
            _interstitialLoaded = false;   // consumed — the next OnAdLoaded flips it back
            // Curtain first — same reason as ShowRewarded.
            MBAdCurtain.Instance.FadeIn(() => {
                PauseGameAudio();
                MaxSdk.ShowInterstitial(InterstitialId, placement);
            }, "inter");
        }

        static void FinishInterstitial(bool displayed) {
            RestoreGameAudio();
            // cooldown only when an ad was actually SEEN — a failed show must not block the next try
            if (displayed) _lastInterClosedAt = Time.realtimeSinceStartup;
            var cb = _onInterstitialClosed; _onInterstitialClosed = null;
            MaxSdk.LoadInterstitial(InterstitialId);   // preload the next one
            cb?.Invoke();                              // runs under the curtain (scene load / popup swap)
            DropCurtainSoon();
        }

        // ================== banner ==================

        static void CreateBanner() {
            if (_bannerCreated || !Usable(BannerId)) return;
            MaxSdk.CreateBanner(BannerId, new MaxSdkBase.AdViewConfiguration(MaxSdkBase.AdViewPosition.BottomCenter));
            MaxSdk.SetBannerBackgroundColor(BannerId, BannerBg);
            MaxSdk.HideBanner(BannerId);   // created hidden — shown on demand
            _bannerCreated = true;
        }

        public static bool BannerVisible { get; private set; }
        public static void ShowBanner() { if (_bannerCreated) { MaxSdk.ShowBanner(BannerId); BannerVisible = true; } }
        public static void HideBanner() { if (_bannerCreated) { MaxSdk.HideBanner(BannerId); BannerVisible = false; } }

        // Banner strip height for THIS device in screen pixels. Banners are ADAPTIVE (the
        // AdViewConfiguration default), so the reserved strip must match MAX's real adaptive
        // height — the legacy 50dp/90dp is only the fallback (editor, or the native call failing).
        public static float GetBannerHeightPixels() {
            const float PhoneDp = 50f, TabletDp = 90f;
            float density = Screen.dpi > 0f ? Screen.dpi / 160f : 2.625f;   // ~xxhdpi fallback
#if UNITY_EDITOR
            bool tablet = Mathf.Min(Screen.width, Screen.height) / density >= 600f;   // 600dp = tablet cutoff
            return (tablet ? TabletDp : PhoneDp) * density;
#else
            float dp = MaxSdkUtils.GetAdaptiveBannerHeight();   // -1 width = full screen, current orientation
            if (dp <= 0f) dp = MaxSdkUtils.IsTablet() ? TabletDp : PhoneDp;
            return dp * density;
#endif
        }

        // ================== revenue → Singular (+ Firebase) ==================

        static void OnRevenuePaid(string adUnitId, MaxSdkBase.AdInfo info) {
            if (info == null || info.Revenue <= 0) return;

            // Debug mode reports no revenue. The Firebase half is covered by the gate inside
            // CDebug, but Singular is a separate SDK that nothing else guards - and ad revenue is
            // the worst thing to fake: it feeds attribution and bidding, not just a dashboard. A
            // tester showing ads from the debug panel would have been paying himself.
            if (!Analytics.EventsOn) return;

            // Singular — the ROAS piece (fields per Singular's MAX ad-revenue doc, enriched).
            var data = new SingularAdData("AppLovin", "USD", info.Revenue)
                .WithNetworkName(info.NetworkName)
                .WithAdType(info.AdFormat)
                .WithAdUnitId(info.AdUnitIdentifier)
                .WithAdPlacmentName(info.Placement)
                .WithPrecision(info.RevenuePrecision);
            // MAX delivers banner revenue on the main thread but fullscreen revenue on a background
            // one, so this method runs on two threads at once. On iOS SingularSDK.Event builds each
            // event into ONE global native NSDictionary (Init -> Push xN -> Send -> Free, none of
            // them taking a handle), so two builds interleaving there produce an event with mixed
            // values, or a crash on the free. This is the app's only caller that touches it, so
            // serialising here is enough. Not a main-thread dispatch: Android pauses the Unity
            // activity behind a fullscreen ad, which would park the report for the ad's whole
            // duration and lose it if the process dies there.
            lock (_singularGate) SingularSDK.AdRevenue(data);

#if !IGNORE_FIREBASE

            Common.CDebug.Log("ad_impression",
                    new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
                    new Firebase.Analytics.Parameter("ad_source", info.NetworkName ?? ""),
                    new Firebase.Analytics.Parameter("ad_format", info.AdFormat ?? ""),
                    new Firebase.Analytics.Parameter("ad_unit_name", info.AdUnitIdentifier ?? ""),
                    new Firebase.Analytics.Parameter("placement", info.Placement ?? ""),
                    new Firebase.Analytics.Parameter("precision", info.RevenuePrecision ?? ""),
                    new Firebase.Analytics.Parameter("currency", "USD"),
                    new Firebase.Analytics.Parameter("value", info.Revenue));
#endif
            Debug.Log($"[Ads] revenue {info.Revenue:F6} USD — {info.NetworkName} / {info.AdFormat} → Singular");
        }

        // ================== price of a LOADED ad → Firebase ==================

        // What a player's ads are worth is only known from ad_impression — and that needs an ad to
        // be SHOWN: none before StartShowInterAtLevel, and never a rewarded one for a player who
        // does not watch them. So everyone who leaves in the first levels has no price at all.
        // But MAX knows the winning bid as soon as an ad is LOADED, and ads load from boot. Report
        // that bid for the first LoadedPricesPerFormat loads of each format — the same number of
        // readings for every player, whatever level he is on.
        //
        // Firebase only, and never as revenue: a loaded ad earned nothing. It is NOT forwarded to
        // Singular, and the price travels as `bid_usd` (no `value` / `currency`), so no report or
        // network can add it to revenue.
        const int LoadedPricesPerFormat = 10;
        const string LoadedCountsKey = "qp_ad_loaded_counts";   // "REWARDED:3,INTER:10" — loads reported so far, per ad format
        static readonly System.Collections.Generic.Dictionary<string, int> _loadedCounts = new System.Collections.Generic.Dictionary<string, int>();
        static bool _loadedCountsDirty;   // both under lock (_loadedCounts): MAX may call back off the main thread.
                                          // The lock is taken once per ad LOAD and once per pause — never per frame.

        static void OnLoadedPrice(string adUnitId, MaxSdkBase.AdInfo info) {
            if (info == null || info.Revenue <= 0) return;
            if (!Analytics.EventsOn) return;   // debug mode reports nothing, like OnRevenuePaid

            string format = info.AdFormat ?? "";
            int n;
            lock (_loadedCounts) {
                _loadedCounts.TryGetValue(format, out n);
                if (n >= LoadedPricesPerFormat) return;
                _loadedCounts[format] = ++n;
                _loadedCountsDirty = true;
            }

#if !IGNORE_FIREBASE
            Common.CDebug.Log("ad_loaded",
                    new Firebase.Analytics.Parameter("ad_source", info.NetworkName ?? ""),
                    new Firebase.Analytics.Parameter("ad_format", format),
                    new Firebase.Analytics.Parameter("ad_unit_name", info.AdUnitIdentifier ?? ""),
                    new Firebase.Analytics.Parameter("precision", info.RevenuePrecision ?? ""),
                    new Firebase.Analytics.Parameter("lvl_idx", AppData.LevelIdx.Value),
                    new Firebase.Analytics.Parameter("n", n),
                    new Firebase.Analytics.Parameter("bid_usd", info.Revenue));
#endif
        }

        static void LoadLoadedCounts() {
            lock (_loadedCounts) {
                _loadedCounts.Clear();
                foreach (var pair in PlayerPrefs.GetString(LoadedCountsKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
                    int at = pair.LastIndexOf(':');
                    if (at > 0 && int.TryParse(pair.Substring(at + 1), out int n)) _loadedCounts[pair.Substring(0, at)] = n;
                }
            }
        }

        // main thread (AdsRunner, on pause / quit): PlayerPrefs cannot be written from MAX's callback thread
        static void SaveLoadedCounts() {
            string text;
            lock (_loadedCounts) {
                if (!_loadedCountsDirty) return;
                _loadedCountsDirty = false;
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kv in _loadedCounts) parts.Add(kv.Key + ":" + kv.Value);
                text = string.Join(",", parts);
            }
            PlayerPrefs.SetString(LoadedCountsKey, text);
            PlayerPrefs.Save();
        }

        // ================== helpers ==================

        static bool Usable(string id) {
#if UNITY_EDITOR
            // Nothing can be shown in the editor: Init() calls DisableStubAds(), because the stubs
            // are fixed-size debug prefabs that misrepresent the layout rather than preview an ad.
            //
            // But MAX still raises OnAdLoaded there, so without this the game believed an ad was
            // ready, raised the curtain, and sat behind a black screen until the display timeout
            // gave up ("no ad displayed in time"). An ad that cannot be displayed is not ready.
            return false;
#else
            return !string.IsNullOrEmpty(id);
#endif
        }

        // Reveal the screen two frames after the close-callback ran, so whatever it triggered
        // (a scene load, a popup) has rendered before the black lifts.
        static void DropCurtainSoon() {
            if (_runner != null) _runner.RunAfterFrames(2, MBAdCurtain.Instance.FadeOut);
            else MBAdCurtain.Instance.FadeOut();
        }

        static bool Online => Common.InternetConnection.Instance == null || Common.InternetConnection.Instance.HasInternet;

        const float MaxWaitForNetSec = 60f;

        // A load failed → wait, then load again. The whole retry rule is here (the first load of
        // an ad never comes this way — it always goes out at once).
        //   Online  → exponential backoff (2^n s, capped), per MAX's recommended retry pattern.
        //   Offline → wait for the connection instead, so a reconnect loads at once. The failure
        //             says nothing about the ad servers, so the backoff starts over. At most
        //             MaxWaitForNetSec, in case the connection check itself is wrong.
        // `retry` is the format's own failure counter, `load` its load call. (A coroutine cannot
        // take a ref, so the counter is updated here and the coroutine only waits and loads.)
        static void RetryLoad(ref int retry, Action load) {
            if (_runner == null) return;
            bool online = Online;
            retry = online ? retry + 1 : 0;
            _runner.StartCoroutine(RetryLoadCo(online, retry, load));
        }

        static IEnumerator RetryLoadCo(bool online, int retry, Action load) {
            if (online) yield return new WaitForSecondsRealtime(Mathf.Pow(2f, Mathf.Min(MaxRetryPow, retry)));
            else {
                float giveUpAt = Time.realtimeSinceStartup + MaxWaitForNetSec;
                yield return new WaitUntil(() => Online || Time.realtimeSinceStartup >= giveUpAt);
            }
            load();
        }

        // Tiny coroutine host for retry delays and DontDestroyOnLoad ownership.
        class AdsRunner : MonoBehaviour {
            // the loaded-ad counts reach PlayerPrefs here, on the main thread, when the app leaves
            // the screen — a kill before that only means a few bids are reported once more
            void OnApplicationPause(bool paused) { if (paused) SaveLoadedCounts(); }
            void OnApplicationQuit() => SaveLoadedCounts();

            public void RunAfterFrames(int frames, Action action) => StartCoroutine(AfterFrames(frames, action));
            IEnumerator AfterFrames(int f, Action a) { for (int i = 0; i < f; i++) yield return null; a?.Invoke(); }
        }
    }
}
