using System;
using System.Globalization;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Which country the player is in, as a two-letter ISO code — "IL", "BR", or "" when nothing
    /// can say.
    ///
    /// Used to pick the kind of names the tournament's rivals arrive under, and nothing else. So
    /// a wrong answer costs a group of names that reads slightly foreign, which is what a global
    /// leaderboard looks like anyway — there is nothing here worth being careful about.
    ///
    /// Asked fresh every time rather than decided once: a player who moves should get their new
    /// neighbours, and the best source only starts answering part-way through a launch. The saved
    /// value is a safety net for the launches where nobody answers at all, never the answer
    /// itself.
    ///
    /// No Init and no manager, and nothing here waits for anything.
    ///
    /// That is affordable because of WHEN it is read. The only caller builds a rival group, which
    /// happens when a tournament opens — and a tournament opens on a sync, which
    /// MBTournamentSyncRunner refuses to run below the unlock level. So the first group a player
    /// ever gets is built when they cross that level, long into a session, by which time the ad
    /// SDK has been up for a while and <see cref="_fromAds"/> has a real answer. Every group
    /// after it opens a session or more later still. There is no moment where this is asked at
    /// boot and has to settle for the weakest source.
    ///
    /// If something here ever DID have to be ready at a particular time, it would not be solved
    /// in this file: MBStartup is where that belongs — it already stages work and waits on it
    /// with a timeout, which is what "ready by then" actually requires.
    /// </summary>
    public static class Country {

        /// <summary>The last answer anyone gave. Only read when nobody will give one now.</summary>
        static readonly PlayerPrefsHelper.StringHolder Saved =
            new PlayerPrefsHelper.StringHolder("qp_country", "");

        /// <summary>Two letters, upper case. Empty means nobody has ever known.</summary>
        public static string Code {
            get {
                string found = _ask();
                if (!string.IsNullOrEmpty(found)) { Saved.Value = found; return found; }
                return Saved.Value;
            }
        }

        /// <summary>
        /// Best first, and the first one that answers wins.
        ///
        /// The ad SDK knows where the device actually IS — its own lookup, no permission and no
        /// request of ours — but only once it has started. The region is whatever the player set
        /// in the OS, which is usually right and is there from the first frame. The language is
        /// the last word and only speaks up when it is spoken in one country.
        /// </summary>
        static string _ask() {
            string code = _fromAds() ?? _fromRegion() ?? _fromLanguage();
            return code != null && code.Length == 2 ? code.ToUpperInvariant() : "";
        }

        /// <summary>AppLovin's own geography. Null until the SDK is up — which is most of the
        /// first launch, and none of the ones after it.</summary>
        static string _fromAds() {
            try {
                if (!MaxSdk.IsInitialized()) return null;
                string code = MaxSdk.GetSdkConfiguration()?.CountryCode;
                return string.IsNullOrEmpty(code) ? null : code;
            } catch (Exception) {
                return null;   // asked too early, or no SDK on this platform
            }
        }

        /// <summary>The OS region. Throws, or answers "IV", when the build strips the culture
        /// data (Player Settings → Use Invariant Culture) — both mean it does not know.</summary>
        static string _fromRegion() {
            try {
                string code = RegionInfo.CurrentRegion?.TwoLetterISORegionName;
                return string.IsNullOrEmpty(code) || code == "IV" ? null : code;
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>Only languages with one home. English, Spanish, Arabic, Portuguese and French
        /// are spoken across too many countries to guess from, so they are not here — an empty
        /// answer and the global pool of names is the honest outcome for them.</summary>
        static string _fromLanguage() {
            switch (Application.systemLanguage) {
                case SystemLanguage.Hebrew:     return "IL";
                case SystemLanguage.Japanese:   return "JP";
                case SystemLanguage.Korean:     return "KR";
                case SystemLanguage.Thai:       return "TH";
                case SystemLanguage.Vietnamese: return "VN";
                case SystemLanguage.Greek:      return "GR";
                case SystemLanguage.Turkish:    return "TR";
                case SystemLanguage.Polish:     return "PL";
                case SystemLanguage.Hungarian:  return "HU";
                case SystemLanguage.Czech:      return "CZ";
                case SystemLanguage.Romanian:   return "RO";
                case SystemLanguage.Ukrainian:  return "UA";
                case SystemLanguage.Indonesian: return "ID";
                case SystemLanguage.Finnish:    return "FI";
                case SystemLanguage.Danish:     return "DK";
                case SystemLanguage.Norwegian:  return "NO";
                case SystemLanguage.Icelandic:  return "IS";
                case SystemLanguage.Estonian:   return "EE";
                case SystemLanguage.Latvian:    return "LV";
                case SystemLanguage.Lithuanian: return "LT";
                case SystemLanguage.Bulgarian:  return "BG";
                case SystemLanguage.Slovak:     return "SK";
                case SystemLanguage.Slovenian:  return "SI";
                default:                        return null;
            }
        }
    }
}
