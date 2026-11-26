namespace qp {
    /// <summary>
    /// Game tuning config — ads pacing and rewarded-ad payouts. Plain values, edit here.
    /// </summary>
    public static class GameConfig {

        // Ads start from these levels (1-based); earlier levels stay ad-free.
        public static int StartShowBannerAtLevel = 11;
        public static int StartShowInterAtLevel = 11;

        // Minimum gap between interstitials.
        public static float InterCooldownSec = 120f;

        // The lose-popup Revive button (rewarded video for more bones) shows from this level on.
        public static int StartShowReviveAtLevel = 5;

        // Level-start "X% pass on their first try" popup shows from this level on (1-based) —
        // the opening levels stay clean. TODO: re-check this number against the real first-try
        // data from time to time (it moves whenever the opening levels change).
        public static int StartShowFirstTryAtLevel = 9;

        // In-game reactions (ReactionDirector: unstuck, comeback, streak, clean) fire from this
        // level on (1-based). 1-3 are tiny guided boards, 4-7 the first one-step lesson and a
        // rest; 8 opens line→region, where players first really stop and think.
        public static int StartShowReactionsAtLevel = 8;

        // Granted when the player finishes a rewarded ad.
        public static int BoosterAddedAfterRewarded = 1;
        public static int BonesAddedAfterRewarded = 3;
    }
}
