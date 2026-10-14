using System;
using System.Collections.Generic;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Builds the nineteen opponents a tournament is run against.
    ///
    /// THE WHOLE IDEA: we choose what each rival ENDS ON. The recording decides everything else —
    /// when they play, how long for, whether they vanish for a day. So the competition is designed
    /// and the behaviour is not simulated, because it was never simulated: it was recorded.
    ///
    /// Targets are multiples of what THIS player usually scores in 48 hours, so the climb costs a
    /// weak player and a strong one the same effort. A set of nineteen multipliers is a whole
    /// tournament's difficulty in one line:
    ///
    ///     normal:  1.8  1.5  1.25 │ 1.15  1.08  1.03 │ (player = 1.0) │ 0.96 …
    ///
    /// Four rivals above them, so playing as usual finishes around 7th and third place — the last
    /// prize — costs about 25% more. Every win moves them a row, because a win is worth ~2.4 points
    /// and the rivals just above are a point or two apart.
    ///
    /// Which set is drawn VARIES, and that matters more than the sets do. A player who is always
    /// 7th, always 25% from a prize, is looking at a pattern; a player who comes 3rd without trying
    /// one week and cannot crack 8th the next is looking at luck. So the prize is deliberately not
    /// available every time.
    /// </summary>
    public static class RivalGroup {

        /// <summary>Which difficulty a group was drawn from. Worth a name because it is the one
        /// thing about a tournament that decides the outcome before the player touches it, so it
        /// has to be remembered (no two Hards in a row) and reported (was the extra play real?).
        ///
        /// Unknown is first so it is the DEFAULT, and that is deliberate: a save written before
        /// this field existed comes back with whatever the initializer says, and a player who
        /// updates mid-tournament would otherwise report a confident "Normal" for a group that
        /// was drawn as something else. A missing answer is harmless; a wrong one quietly ruins
        /// the only comparison the field was added for.</summary>
        public enum Kind { Unknown, Easy, Normal, Hard }

        /// <summary>Rivals per group — the player makes twenty.</summary>
        public const int Size = 19;

        // ---- the three difficulty sets ---------------------------------------------------
        // Each is nineteen multipliers of the player's usual 48-hour score, biggest first. How
        // many sit above 1.0 is the whole design: that is where the player lands by doing nothing
        // differently. The last slot is not a multiplier — see DeadAccountScore.

        /// <summary>Four above: finishes ~5th by default, a prize within reach of a normal week.</summary>
        static readonly float[] Easy = {
            1.20f, 1.10f,
            0.95f, 0.89f, 0.83f, 0.77f, 0.71f, 0.65f, 0.59f, 0.53f,
            0.47f, 0.41f, 0.35f, 0.29f, 0.23f, 0.17f, 0.11f, 0.06f, 0.03f,
        };

        /// <summary>Six above: finishes ~7th, third place costs about 25% more play.</summary>
        static readonly float[] Normal = {
            1.80f, 1.50f, 1.25f, 1.15f, 1.08f, 1.03f,
            0.96f, 0.90f, 0.84f, 0.77f, 0.69f, 0.60f, 0.50f,
            0.41f, 0.32f, 0.24f, 0.16f, 0.09f, 0.04f,
        };

        /// <summary>Ten above, one of them far out of reach. This is the week the prize is gone —
        /// and the reason winning one means something.</summary>
        static readonly float[] Hard = {
            5.50f, 3.40f, 2.50f, 2.10f, 1.80f, 1.60f, 1.45f, 1.30f, 1.20f, 1.10f,
            0.92f, 0.84f, 0.75f, 0.65f, 0.55f, 0.44f, 0.33f, 0.22f, 0.11f,
        };

        const float EasyChance = 0.25f;
        const float HardChance = 0.15f;   // the rest are Normal

        /// <summary>Each multiplier is nudged by up to this much, so two groups of the same kind
        /// are never the same group.</summary>
        const float Jitter = 0.08f;

        /// <summary>The last slot is an absolute score, not a multiple: someone who opened the game
        /// once. As a multiplier it would scale with the player, and a strong player's "dead
        /// account" would be busier than most people's whole week.</summary>
        const int DeadAccountScore = 2;

        /// <summary>
        /// Rivals arrive within this many minutes of the tournament opening.
        ///
        /// A recording's window is a CALENDAR window, so its first win sits wherever that person
        /// happened to play — sometimes forty hours in. That is not a player who joined late, it
        /// is a player whose evening fell there, and replaying it as-is leaves the table empty
        /// for the hours when the player is most curious about it.
        ///
        /// A real group is filled from the next players to win, so it is full within minutes. So
        /// each recording is slid left until its first win lands somewhere in here: the gaps, the
        /// quiet night and the sprint at the end all survive exactly as recorded, and only the
        /// question of when the person started is replaced. Sliding left can never run past the
        /// end of the window, so nothing is lost off the back.
        ///
        /// Spread across the whole stretch rather than bunched at zero, so the player — whose own
        /// first win lands somewhere in here too — is sometimes third into the group and
        /// sometimes fifteenth, instead of always being the one everyone else arrives after.
        ///
        /// Five, because that is the window the player's own first win falls in: a level takes
        /// about two and a half minutes (the median gap between wins in a session, measured over
        /// the export). Spread wider and they beat an empty table to the top; spread narrower and
        /// the group is already complete before they have played a hand.
        /// </summary>
        const int JoinWithinMinutes = 5;

        /// <summary>
        /// What a player scores in 48 hours when we have never seen them finish a tournament: the
        /// median of everyone past the unlock level. Wrong for any particular player and right for
        /// most, and only used once — the next group is built on what they actually did.
        ///
        /// Taken from the blob, so it tracks the game as it grows and arrives with every refresh
        /// of the recordings rather than with a release.
        /// </summary>
        public static int TypicalScore =>
            RivalsBlob.MedianScore > 0 ? RivalsBlob.MedianScore : FallbackMedianScore;

        /// <summary>For a blob that is missing or predates the field. Measured over the Firebase
        /// export when this was written: 16 wins at ~2.36 points.</summary>
        public const int FallbackMedianScore = 38;

        /// <summary>How close a recording has to land to its target before we take it. Widened
        /// when nothing is near enough; the bank holds dozens of candidates per target at the
        /// scores most players live at.</summary>
        static readonly float[] Tolerances = { 0.05f, 0.10f, 0.20f, 0.40f };

        // ---- building --------------------------------------------------------------------

        /// <summary>
        /// Nineteen rivals for a player who usually scores <paramref name="myScore"/>.
        /// <paramref name="myOwner"/> keeps the player's own recordings out of their group, and
        /// <paramref name="myName"/> keeps a rival from arriving under their name.
        /// </summary>
        /// <param name="last">The kind the player got last time, so the draw can avoid repeating
        /// an extreme. <paramref name="chosen"/> reports what this group actually is.</param>
        public static List<LocalRival> Build(int myScore, uint myOwner, string myName, int myLevel,
                                             Kind last, out Kind chosen) {
            // Taken and released in the same breath, so the whole lifetime is one method. The
            // recordings and the name lists are read here and nowhere else: everything chosen is
            // copied out below, and the ~280 KB and ~70 KB behind them are not wanted again for
            // another 48 hours.
            RivalsBlob.Load();
            RivalNames.Load();
            try { return _build(myScore, myOwner, myName, myLevel, last, out chosen); }
            finally { RivalsBlob.Unload(); RivalNames.Unload(); }
        }

        static List<LocalRival> _build(int myScore, uint myOwner, string myName, int myLevel,
                                       Kind last, out Kind chosen) {
            chosen = Kind.Unknown;   // nothing was drawn unless the group is actually built
            var rivals = new List<LocalRival>(Size);
            if (RivalsBlob.Count == 0) {
                Debug.LogWarning("[RivalGroup] no recordings available — the tournament has no rivals");
                return rivals;
            }

            var rnd = new System.Random(Guid.NewGuid().GetHashCode());
            chosen = _pickKind(rnd, last);
            float[] set = chosen == Kind.Easy ? Easy : chosen == Kind.Hard ? Hard : Normal;
            var usedRecordings = new HashSet<int>();
            var usedNames = new HashSet<string> { myName ?? "" };

            // Drawn once, for the whole group: how many of them look like players who named
            // themselves, and where in the world they are from.
            var cfg = TournamentConfig.Instance;
            float realNames = cfg != null ? cfg.RealNameShare(myLevel) : 0.5f;
            string country = Country.Code;

            for (int i = 0; i < set.Length; i++) {
                bool isLast = i == set.Length - 1;
                int target = isLast
                    ? DeadAccountScore
                    : Mathf.Max(1, Mathf.RoundToInt(myScore * set[i] * _jitter(rnd)));

                int pick = _pickRecording(target, myOwner, usedRecordings, rnd);
                if (pick < 0) continue;                 // bank too small to fill the group
                usedRecordings.Add(pick);

                var rival = new LocalRival {
                    name = _pickName(usedNames, country, realNames),
                    total = RivalsBlob.TotalAt(pick),
                };
                _unpack(RivalsBlob.PlaysAt(pick), rnd, rival);
                rivals.Add(rival);
            }
            return rivals;
        }

        /// <summary>
        /// The draw, with one rule on top of it: never the same EXTREME twice running.
        ///
        /// The roll is independent every time, which is the point — a prize that cannot be
        /// predicted is what makes winning one mean anything. But independence also produces runs,
        /// and a run of two reads as a verdict rather than as luck: two Hards means a player
        /// spends four days nowhere near a prize with nothing on screen to explain it, and two
        /// Easys teaches them the prize was never worth playing for. Both are rare — 2.25% and
        /// 6.25% — and both are the worst thing the feature can do to the player it happens to.
        /// A repeat is turned into Normal, which costs the long-run mix about a point and a half
        /// of each extreme and is well worth it.
        /// </summary>
        /// <remarks>An Unknown <paramref name="last"/> — a first tournament, or one that predates
        /// the field — suppresses nothing, which is right: there is no repeat to avoid.</remarks>
        static Kind _pickKind(System.Random rnd, Kind last) {
            double roll = rnd.NextDouble();
            Kind k = roll < EasyChance ? Kind.Easy
                   : roll < EasyChance + HardChance ? Kind.Hard
                   : Kind.Normal;
            return k != Kind.Normal && k == last ? Kind.Normal : k;
        }

        static float _jitter(System.Random rnd) => 1f + (float)(rnd.NextDouble() * 2 - 1) * Jitter;

        /// <summary>
        /// A recording ending near <paramref name="target"/> — at random among those close enough,
        /// never one of this player's own, never one already in this group.
        ///
        /// Random and not nearest: nearest would hand the same player the same opponents every
        /// time, since their usual score barely moves between tournaments.
        /// </summary>
        static int _pickRecording(int target, uint myOwner, HashSet<int> used, System.Random rnd) {
            foreach (float tolerance in Tolerances) {
                var candidates = new List<int>();
                int span = Mathf.Max(1, Mathf.RoundToInt(target * tolerance));
                int from = RivalsBlob.LowerBound(target - span);
                for (int i = from; i < RivalsBlob.Count && RivalsBlob.TotalAt(i) <= target + span; i++) {
                    if (used.Contains(i) || RivalsBlob.OwnerAt(i) == myOwner) continue;
                    candidates.Add(i);
                }
                if (candidates.Count > 0) return candidates[rnd.Next(candidates.Count)];
            }

            // Past every tolerance, this player has outgrown the recordings: their target is
            // above anything anyone in the bank actually scored. The closest one it is, which
            // compresses the top of their table and makes the tournament EASIER than designed.
            //
            // That is deliberate. We could stitch two recordings into one to reach any target,
            // and chose not to: it is the only place we would be inventing behaviour instead of
            // replaying it, and the point of all of this is that it was never invented. It also
            // fixes itself from two directions — the bank is resampled daily, so its ceiling
            // rises as the game grows, and a player this strong deserves real opponents, which
            // is what a real server will bring.
            //
            // Today this is under 1% of players (the ceiling bites above ~470 points in 48
            // hours). When that stops being true, "how many players score above the bank's top"
            // is one BigQuery query away — no instrumentation needed here.
            int best = -1, bestGap = int.MaxValue;
            for (int i = 0; i < RivalsBlob.Count; i++) {
                if (used.Contains(i) || RivalsBlob.OwnerAt(i) == myOwner) continue;
                int gap = Mathf.Abs(RivalsBlob.TotalAt(i) - target);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        /// <summary>A name nobody in this group already has. <see cref="RivalNames"/> decides what
        /// kind of name it is; this only keeps the table from showing the same one twice.</summary>
        static string _pickName(HashSet<string> used, string country, float realNames) {
            for (int attempt = 0; attempt < 20; attempt++) {
                string name = RivalNames.Pick(country, realNames);
                if (used.Add(name)) return name;
            }
            // Every draw collided — vanishingly unlikely against pools this size, and a duplicate
            // name is better than no rival.
            return RivalNames.Pick(country, realNames);
        }

        /// <summary>
        /// Read the recording out of the blob into plain minutes and points — the packing is the
        /// blob's business and stops here — and slide it so this rival joins in the first few
        /// minutes, see <see cref="JoinWithinMinutes"/>.
        ///
        /// Everything inside keeps its spacing; only where it begins moves.
        /// </summary>
        static void _unpack(ushort[] plays, System.Random rnd, LocalRival into) {
            into.minutes = new int[plays.Length];
            into.points = new int[plays.Length];
            if (plays.Length == 0) return;

            int shift = RivalsBlob.MinuteOf(plays[0]) - rnd.Next(JoinWithinMinutes + 1);
            if (shift < 0) shift = 0;                       // already starts early enough

            for (int i = 0; i < plays.Length; i++) {
                into.minutes[i] = Mathf.Max(0, RivalsBlob.MinuteOf(plays[i]) - shift);
                into.points[i] = RivalsBlob.PointsOf(plays[i]);
            }
        }
    }
}
