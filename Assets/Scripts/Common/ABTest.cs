using Common;
using UnityEngine;

namespace qp {
    /// <summary>
    /// A/B tests. A test is a name and its groups. A player's group is drawn the first time the test
    /// is asked about with its groups, kept on the device for good (it is never drawn again, whatever
    /// ships later), and set as the Firebase user property "ab_&lt;test&gt;" - so every event after it,
    /// of any kind, says which group the player is in.
    ///
    ///     string g = ABTest.Group("levels_v4");                                   // null: not in it
    ///     if (g == null &amp;&amp; newPlayer) g = ABTest.Group("levels_v4", new[] { "levels", "levels_ab" });
    ///
    /// Firebase limits: 25 user properties of our own per app (uid, first_version and src_network
    /// are taken), a name up to 24 characters ("ab_" + the test: 21 left), a value up to 36.
    /// Stop asking about a test that is over, and its property stops being sent on new installs.
    /// </summary>
    public static class ABTest {

        const string Prefix = "abtst_";

        /// <summary>This player's group in the test, or null when it was never drawn for them.</summary>
        public static string Group(string test) {
            string key = Prefix + test;
            return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        }

        /// <summary>This player's group in the test - drawn now, once and for good, when it was not yet.</summary>
        public static string Group(string test, string[] groups) {
            string key = Prefix + test;
            if (PlayerPrefs.HasKey(key)) {
                return PlayerPrefs.GetString(key);
            }
            
            if (key.Length > 24) Debug.LogError($"[ABTest] test name too long for a Firebase user property: {key}");
            var group = groups[Random.Range(0, groups.Length)];

            PlayerPrefs.SetString(key, group);
            PlayerPrefs.Save();
            Analytics.ABActivate(test, group);
            return group;
        }

        /// <summary>Debug panel only: put this device in a group (and keep it), to try every group on one device.</summary>
        public static void Force(string test, string group) {
            PlayerPrefs.SetString(Prefix + test, group);
            PlayerPrefs.Save();
            Report(test, group);
        }

        static void Report(string test, string group) {
#if !IGNORE_FIREBASE
            if (!FirebaseBootstrap.FBAvailable) return;   // ReportAll sends it at launch
            Firebase.Analytics.FirebaseAnalytics.SetUserProperty(Prefix + test, group.Length > 36 ? group.Substring(0, 36) : group);
#endif
        }
    }
}
