using System;
using System.Collections.Generic;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Builds the nineteen opponents a tournament is run against.
    ///
    /// THE WHOLE IDEA: we choose what each rival ENDS ON. The recording decides everything else —
    /// when they play, how long for, whether they vanish for a day. So the competition is designed
    /// and the behaviour is not simulated, because it was never simulated: it was recorded.
    ///
    /// Targets are multiples of what THIS player usually scores in 48 hours. A list of those
    /// multiples is a whole tournament's difficulty in one line.
    ///
    /// The lists are NOT here: they arrive with the rivals file (<see cref="RivalsBlob.ListFor"/>)
    /// and are measured on the server every day — see pawdoku-winstats-server/src/tournament/
    /// lists.ts for how, and why. In short: each list sits on the 95%, 90% … 10% points of how far
    /// a settled player's real 48 hours lands from their base, times a scale per difficulty, and
    /// there is one set of lists per GROUP of players by base — because a player who usually
    /// scores 15 doubles it far more easily than one who usually scores 150, and one list for both
    /// handed the light player five times the prizes. After the list comes one more rival, the
    /// dead account (<see cref="DeadAccountScore"/>).
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

        // ---- which difficulty -------------------------------------------------------------
        // The lists themselves come from the file. Measured there, Easy gives a prize about a
        // third of the time, Normal about a fifth, and Hard under one time in ten — the week the
        // prize is nearly gone, and the reason winning one means something.

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
        /// of the recordings rather than with a release. A blob without it is not loaded at all
        /// (see <see cref="RivalsBlob"/>), so 0 here only ever means there are no rivals either.
        /// </summary>
        public static int TypicalScore => RivalsBlob.MedianScore;

        /// <summary>What a group was built from — reported with its result.</summary>
        public struct Built {
            public Kind kind;
            /// <summary>Which group of players by base, 1-based; 0 when nothing was built.</summary>
            public int group;
            /// <summary>When the server made the file it came from (unix seconds).</summary>
            public long fileBuiltAt;
            /// <summary>"download" or "baked".</summary>
            public string fileSource;
        }

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
        /// an extreme. <paramref name="built"/> reports what this group actually is and where its
        /// list came from.</param>
        /// <param name="firstEver">The player's first tournament, which is always Easy: their
        /// usual score is still an estimate, and this is not the moment for the week the prize is
        /// nearly gone.</param>
        public static List<LocalRival> Build(int myScore, uint myOwner, string myName, int myLevel,
                                             Kind last, bool firstEver, out Built built) {
            // Taken and released in the same breath, so the whole lifetime is one method. The
            // recordings and the name lists are read here and nowhere else: everything chosen is
            // copied out below, and the ~280 KB and ~70 KB behind them are not wanted again for
            // another 48 hours.
            RivalsBlob.Load();
            RivalNames.Load();
            try { return _build(myScore, myOwner, myName, myLevel, last, firstEver, out built); }
            finally { RivalsBlob.Unload(); RivalNames.Unload(); }
        }

        static List<LocalRival> _build(int myScore, uint myOwner, string myName, int myLevel,
                                       Kind last, bool firstEver, out Built built) {
            built = new Built { kind = Kind.Unknown, fileSource = "" };   // nothing drawn unless built
            var rivals = new List<LocalRival>(Size);
            if (RivalsBlob.Count == 0) {
                // Both the downloaded file and the baked one are missing or failed their checks —
                // and each of those was reported where it happened. This is the outcome: no group.
                CDebug.LogError("[RivalGroup] no recordings available — the tournament has no rivals");
                return rivals;
            }

            var rnd = new System.Random(Guid.NewGuid().GetHashCode());
            built.kind = firstEver ? Kind.Easy : _pickKind(rnd, last);
            int group = RivalsBlob.GroupOf(myScore);
            float[] list = RivalsBlob.ListFor(group, built.kind == Kind.Easy ? 0 : built.kind == Kind.Hard ? 2 : 1);
            built.group = group + 1;
            built.fileBuiltAt = RivalsBlob.BuiltAt;
            built.fileSource = RivalsBlob.FromDownload ? "download" : "baked";

            // The list, then one more: the dead account.
            int slots = list.Length + 1;
            var usedRecordings = new HashSet<int>();
            var usedNames = new HashSet<string> { myName ?? "" };
            var usedWords = new HashSet<string>();

            // Decided once, for the whole group: where in the world they are from, and which of
            // them are players who named themselves.
            //
            // A COUNT and not a chance per rival. A share of 24% can land on a table where
            // eighteen of the nineteen kept the name the game gave them, which is honest and
            // reads as nothing at all — and that is the first table a player ever sees.
            string country = Country.Code;
            var renamed = _whoRenamed(myLevel, slots, rnd);

            for (int i = 0; i < slots; i++) {
                bool isLast = i == slots - 1;
                int target = isLast
                    ? DeadAccountScore
                    : Mathf.Max(1, Mathf.RoundToInt(myScore * list[i] * _jitter(rnd)));

                int pick = _pickRecording(target, myOwner, usedRecordings, rnd);
                if (pick < 0) continue;                 // bank too small to fill the group
                usedRecordings.Add(pick);

                var rival = new LocalRival {
                    name = _pickName(usedNames, usedWords, country, renamed[i]),
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
        /// <remarks>An Unknown <paramref name="last"/> — a save that predates the field — suppresses
        /// nothing, which is right: there is no repeat to avoid. A first tournament never gets
        /// here; it is always Easy.</remarks>
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
            // Today the bank tops out near 1000 points, so the top rival hits it in ~3% of Normal
            // groups and ~5% of Hard ones (Oct 2026, with the measured sets). When that grows,
            // t_score against t_base on tournament_end says by how much — no instrumentation
            // needed here.
            int best = -1, bestGap = int.MaxValue;
            for (int i = 0; i < RivalsBlob.Count; i++) {
                if (used.Contains(i) || RivalsBlob.OwnerAt(i) == myOwner) continue;
                int gap = Mathf.Abs(RivalsBlob.TotalAt(i) - target);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Which rivals are players who named themselves, one flag each, shuffled.
        ///
        /// Shuffled because the rest of this method walks the group from the strongest target
        /// score down — handing out the flags in order would make the renamed ones the ones at
        /// the top of the table, every time, in every tournament anybody ever played.
        /// </summary>
        static bool[] _whoRenamed(int level, int size, System.Random rnd) {
            var cfg = TournamentConfig.Instance;
            int count = cfg != null
                ? Mathf.Clamp(Mathf.RoundToInt(size * cfg.RealNameShare(level)), cfg.minRenamed, size)
                : size / 2;

            var flags = new bool[size];
            for (int i = 0; i < count; i++) flags[i] = true;
            for (int i = size - 1; i > 0; i--) {
                int j = rnd.Next(i + 1);
                (flags[i], flags[j]) = (flags[j], flags[i]);
            }
            return flags;
        }

        /// <summary>
        /// A name nobody in this group already has. <see cref="RivalNames"/> decides what kind of
        /// name it is; this only keeps the table from repeating itself.
        ///
        /// For a generated name that means no repeated WORD, not just no repeated name. It is two
        /// words drawn from thirty-five and thirty-five, so a dozen of them in one group share an
        /// adjective or a noun almost every time — "Quiet Sparrow" above "Quiet Acorn" is what
        /// makes a table read as one thing repeated instead of as people.
        /// </summary>
        static string _pickName(HashSet<string> used, HashSet<string> usedWords,
                                string country, bool renamed) {
            for (int attempt = 0; attempt < 20; attempt++) {
                string name = RivalNames.Pick(country, renamed);
                if (!used.Add(name)) continue;
                if (renamed) return name;

                int space = name.IndexOf(' ');
                if (space < 0) return name;

                string first = name.Substring(0, space), second = name.Substring(space + 1);
                if (!usedWords.Contains(first) && !usedWords.Contains(second)) {
                    usedWords.Add(first);
                    usedWords.Add(second);
                    return name;
                }
                used.Remove(name);   // not taken after all — a later rival may still get it
            }
            // Every draw collided — vanishingly unlikely against pools this size, and a repeat is
            // better than no rival.
            return RivalNames.Pick(country, renamed);
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
