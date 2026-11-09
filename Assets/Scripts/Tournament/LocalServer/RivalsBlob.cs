using System;
using System.Security.Cryptography;
using System.Text;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Zero-parse reader for the rivals blob — recorded 48-hour play histories of real players,
    /// and the lists that say how strong each rival is, packed by pawdoku-winstats-server (see its
    /// tournament/pack.ts for the byte layout; QRIV v1).
    ///
    /// Like <see cref="WinStats"/>, the bytes ARE the data structure. The index is sorted by the
    /// score each recording ends on, so picking an opponent is a binary search over ~30 KB and
    /// then one seek — the 245 KB of wins is never touched for the thousands of recordings that
    /// were not chosen.
    ///
    /// Every stride comes from the HEADER, not from a constant here, so the server can grow a row
    /// (skins are coming) without a version bump: this reader strides by the header's numbers and
    /// reads only the fields it knows.
    ///
    /// ---- THE RULE: THE FILE ONLY EVER GROWS AT THE END ----
    /// Every phone keeps reading this file with the code it shipped with. Two numbers say what a
    /// file holds, and they work like a version and a sub-version:
    ///   - version (byte 4): bumped only for a change an old reader cannot survive. This reader
    ///     rejects any version it does not know, and the server serves a new one from a new URL.
    ///   - headerSize (byte 5): grows with every field added at the end of the header. A field
    ///     exists when the header reaches it; nothing past what this reader knows is read.
    ///
    ///   version  headerSize  what arrived
    ///   1        12          the first file: recordings and their wins
    ///   1        14          medianScore (Oct 2026)
    ///   1        22          listsOffset + builtAt: the rival lists (Nov 2026) — required from here
    ///
    /// A file from before the lists (headerSize below 22) is OLD, not broken: a phone that last
    /// downloaded before the server changed still holds one. It is set aside quietly in favour of
    /// the baked copy and a fresh download. Anything else that does not check out is BROKEN, and
    /// is reported (<see cref="Report"/>) — those are the ones worth hearing about.
    /// </summary>
    public static class RivalsBlob {

        public const byte Version = 1;

        /// <summary>The copy shipped in the build — the floor, used until a download replaces it.</summary>
        const string BakedResource = "rivals";

        // Header: magic(4) version(1) headerSize(1) indexEntrySize(1) playSize(1) recordCount(4),
        // and after those the fields that arrived later — each read only if headerSize reaches it.
        const int MinHeaderSize = 4 + 1 + 1 + 1 + 1 + 4;
        const int MedianScoreAt = MinHeaderSize;          // uint16
        const int MedianScoreHeaderSize = MedianScoreAt + 2;
        const int ListsOffsetAt = MedianScoreHeaderSize;  // uint32, from the start of the file
        const int BuiltAtAt = ListsOffsetAt + 4;          // uint32, unix seconds
        const int ListsHeaderSize = BuiltAtAt + 4;        // a header this long carries the lists

        // ---- index entry schema — the single source of truth for field order --------------
        // Offsets are derived from these; nothing else may assume the layout.
        const int TotalSize = 2;    // uint16, the score this recording ends on
        const int OwnerSize = 4;    // uint32, truncated hash of the player it came from
        const int OffsetSize = 4;   // uint32, from the start of the play region
        const int KnownEntrySize = TotalSize + OwnerSize + OffsetSize;

        const int TotalAtEntry = 0;
        const int OwnerAtEntry = TotalSize;
        const int OffsetAtEntry = TotalSize + OwnerSize;

        // A play is packed into a uint16: the minute in the high bits, points - 1 in the low two.
        const int PointsBits = 2;
        const int PointsMask = (1 << PointsBits) - 1;
        public const int MaxMinute = 2879;   // 48 hours; a win cannot sit outside its window

        // ---- the lists section — see the server's tournament/lists.ts --------------------
        // sectionHeaderSize(1) groups(1) difficulties(1) rivalsPerList(1), then
        // (groups - 1) x uint16 cutoffs, then groups x difficulties x rivalsPerList x uint16
        // multiples x100, strongest first, difficulties in the order Easy, Normal, Hard.
        const int ListsSectionMinHeader = 4;

        /// <summary>Easy, Normal, Hard — what the game draws from. A file may carry more after
        /// them (a later difficulty); this reader uses these three and skips the rest.</summary>
        public const int Difficulties = 3;

        /// <summary>A multiple outside this is not a rival anyone could meet.</summary>
        const float MinMultiple = 0.05f, MaxMultiple = 20f;

        /// <summary>The server existed from Oct 2026; a build time outside this is not a time.</summary>
        const long EarliestBuild = 1790812800;   // 2026-10-01 UTC
        const long LatestBuild = 4102444800;     // 2100-01-01 UTC

        /// <summary>A recording with no owner — from a build before the app sent an id.</summary>
        public const uint NoOwner = 0;

        static byte[] _blob;
        static int _count;
        static int _medianScore;
        static int _entrySize;     // from the header; may exceed KnownEntrySize
        static int _playSize;      // from the header
        static int _indexPos;
        static int _playsPos;
        static int[] _cutoffs;     // base where each group after the first starts
        static float[][] _lists;   // [group * Difficulties + difficulty], strongest first
        static long _builtAt;
        static bool _downloaded;
        static bool _loadTried;

        /// <summary>How many recordings are available; 0 when nothing is loaded.</summary>
        public static int Count => Load() ? _count : 0;

        /// <summary>
        /// What a typical player scores in 48 hours, measured over everyone — or 0 when nothing
        /// is loaded.
        ///
        /// It has to be told to us. The recordings are drawn to a fixed quota per score bucket, so
        /// their own middle is a property of the quota and not of the players; only the server has
        /// ever seen the real distribution. The game needs it to size a first tournament, before
        /// this player has finished one of their own.
        /// </summary>
        public static int MedianScore => Load() ? _medianScore : 0;

        /// <summary>When the server built the file in use (unix seconds), or 0 when none is loaded.
        /// Reported with every result, so a phone stuck on an old file shows up.</summary>
        public static long BuiltAt => Load() ? _builtAt : 0;

        /// <summary>True when the file in use is the downloaded one, false for the baked copy.</summary>
        public static bool FromDownload => Load() && _downloaded;

        /// <summary>How many groups of players the lists are cut into; 0 when nothing is loaded.</summary>
        public static int Groups => Load() ? _cutoffs.Length + 1 : 0;

        /// <summary>
        /// Which group a player who usually scores <paramref name="baseScore"/> belongs to: 0 below
        /// the first cutoff, and one more for every cutoff they reach.
        /// </summary>
        public static int GroupOf(int baseScore) {
            if (!Load()) return 0;
            int group = 0;
            foreach (int cutoff in _cutoffs)
                if (baseScore >= cutoff) group++;
            return group;
        }

        /// <summary>
        /// The multiples of a player's base that a group's rivals end on, strongest first, for
        /// <paramref name="difficulty"/> 0 = Easy, 1 = Normal, 2 = Hard. Null when nothing is
        /// loaded. Shared — the caller reads it and does not keep or change it.
        /// </summary>
        public static float[] ListFor(int group, int difficulty) {
            if (!Load()) return null;
            group = Mathf.Clamp(group, 0, _cutoffs.Length);
            difficulty = Mathf.Clamp(difficulty, 0, Difficulties - 1);
            return _lists[group * Difficulties + difficulty];
        }

        // ---- loading ---------------------------------------------------------------------

        /// <summary>
        /// The downloaded copy, and the only file anyone writes. <see cref="RivalsDownload"/>
        /// replaces it; nothing else touches it, and nothing hands this class bytes.
        ///
        /// MAIN THREAD ONLY — <see cref="Application.persistentDataPath"/> is a Unity API. Callers
        /// off the player loop read it once at boot and keep it.
        /// </summary>
        public static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        const string FileName = "rivals.bytes";

        /// <summary>
        /// Load the file, if it is not already loaded.
        ///
        /// ON DEMAND, and released again by <see cref="Unload"/> the moment the caller is done:
        /// this is ~280 KB that a tournament needs for the few milliseconds it takes to pick
        /// nineteen opponents, once every 48 hours. Keeping it resident for a whole session would
        /// be paying for all of it to save none of it. There is no work to do at startup at all.
        ///
        /// TWO sources in ONE order, decided here and nowhere else: the downloaded file if there
        /// is one and it checks out, the copy baked into the build otherwise. A downloaded file
        /// that does not check out is deleted with its ETag, so the next launch downloads a whole
        /// new one instead of being told nothing changed — the baked copy covers the gap.
        ///
        /// Only the cheap checks run here (<see cref="CheckLight"/>: the header, the median, the
        /// lists — a few hundred numbers). The walk over every win ran when the file was
        /// downloaded, off the main thread.
        ///
        /// MAIN THREAD, because <see cref="Resources.Load"/> is. That holds today: the only
        /// caller is a group build, which runs from the sync runner — a MonoBehaviour — so the
        /// await before it returns to the player loop. Moving the sync off the main thread would
        /// have to deal with this.
        ///
        /// Public so a caller that is about to use the blob and then release it can say so at the
        /// top rather than leaving a lone Unload at the bottom with nothing to pair against. Every
        /// read still calls it itself, so nobody HAS to.
        /// </summary>
        public static bool Load() {
            if (_loadTried) return _blob != null;
            _loadTried = true;

            byte[] file = _fromFile();
            if (file != null) {
                if (_adopt(file, downloaded: true)) return true;
                RivalsDownload.Forget();
            }
            return _adopt(_fromResources(), downloaded: false);
        }

        static byte[] _fromResources() {
            var baked = Resources.Load<TextAsset>(BakedResource);
            if (baked == null) return null;          // nothing baked either — no rivals, no crash
            byte[] bytes = baked.bytes;
            Resources.UnloadAsset(baked);
            return bytes;
        }

        /// <summary>
        /// Let the bytes go. Safe to call at any time — the next read loads them again, and a
        /// tournament already underway holds its own copy of the nineteen recordings it chose.
        /// </summary>
        public static void Unload() {
            _blob = null;
            _loadTried = false;
            _count = 0;
            _cutoffs = null;
            _lists = null;
        }

        static byte[] _fromFile() {
            try {
                string path = FilePath;
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
            } catch (Exception e) {
                Debug.LogWarning("[RivalsBlob] downloaded blob unreadable: " + e.Message);
                return null;
            }
        }

        static bool _adopt(byte[] bytes, bool downloaded) {
            if (bytes == null) return false;

            string err;
            bool old = false;
            try { err = CheckLight(bytes, out old); }
            catch (Exception e) { err = $"crash: {e.GetType().Name}: {e.Message}"; }

            if (err != null) {
                // A downloaded file from before the lists is what an update leaves behind — not a
                // fault. A BAKED one from before them means a build went out without them: report.
                if (!(old && downloaded)) Report(downloaded ? "load-file" : "load-baked", err);
                return false;
            }

            _blob = bytes;
            _entrySize = bytes[6];
            _playSize = bytes[7];
            _count = ReadInt(bytes, 8);
            _indexPos = bytes[5];                    // headerSize
            _medianScore = ReadUShort(bytes, MedianScoreAt);
            _builtAt = ReadUInt(bytes, BuiltAtAt);
            _readLists(bytes, out _cutoffs, out _lists);
            _downloaded = downloaded;

            _playsPos = _indexPos + _count * _entrySize;
            _loadTried = true;
            return true;
        }

        /// <summary>
        /// Say that a file was rejected, and why: Crashlytics (a wave of one reason across many
        /// phones is a broken server file) and a <c>rivals_rejected</c> event (to count it).
        /// <paramref name="where"/> is download / load-file / load-baked.
        /// </summary>
        internal static void Report(string where, string reason) {
            CDebug.LogError($"[RivalsBlob] {where} rejected: {reason}");
            Analytics.RivalsRejected(where, reason);
        }

        // ---- checks ----------------------------------------------------------------------

        /// <summary>The structure only: magic, version, sizes, and that the index fits.</summary>
        static string CheckHeader(byte[] b) {
            if (b == null || b.Length < MinHeaderSize) return "too short";
            if (b[0] != 'Q' || b[1] != 'R' || b[2] != 'I' || b[3] != 'V') return "bad magic";
            if (b[4] != Version) return $"version {b[4]}, reader understands {Version}";

            int headerSize = b[5], entrySize = b[6], playSize = b[7];
            if (headerSize < MinHeaderSize) return $"headerSize {headerSize} is below the {MinHeaderSize} this reader needs";
            if (entrySize < KnownEntrySize) return $"indexEntrySize {entrySize} is below the {KnownEntrySize} this reader needs";
            if (playSize < 2) return $"playSize {playSize} is too small";
            if (b.Length < headerSize + 4) return "truncated header";

            int count = ReadInt(b, 8);
            if (count <= 0) return $"recordCount {count}";
            long indexEnd = (long)headerSize + (long)count * entrySize;
            if (indexEnd > b.Length) return $"index of {count} entries runs past the end";
            return null;
        }

        /// <summary>
        /// Everything that can be checked without walking the wins: the header, the median, the
        /// build time and the lists. Null when sound. <paramref name="old"/> is set when the file
        /// is simply from before the lists existed.
        /// </summary>
        static string CheckLight(byte[] b, out bool old) {
            old = false;
            string err = CheckHeader(b);
            if (err != null) return err;

            if (b[5] < ListsHeaderSize) {
                old = true;
                return $"headerSize {b[5]}: a file from before the lists";
            }

            int median = ReadUShort(b, MedianScoreAt);
            if (median <= 0) return $"medianScore {median}";

            long builtAt = ReadUInt(b, BuiltAtAt);
            if (builtAt < EarliestBuild || builtAt > LatestBuild) return $"builtAt {builtAt} is not a time";

            return _readLists(b, out _, out _);
        }

        /// <summary>
        /// Read the lists section and check it on the way. Null when sound.
        ///
        /// Checks only what will stay true as the file grows: the section and everything it says
        /// it holds fits inside the file (more may follow it), cutoffs rise, every list falls and
        /// stays in range, and in every group Easy is kinder than Normal is kinder than Hard. How
        /// many groups and how many rivals per list are the server's business, read from here.
        /// </summary>
        static string _readLists(byte[] b, out int[] cutoffs, out float[][] lists) {
            cutoffs = null;
            lists = null;

            long at = ReadUInt(b, ListsOffsetAt);
            long indexEnd = (long)b[5] + (long)ReadInt(b, 8) * b[6];
            if (at < indexEnd || at + ListsSectionMinHeader > b.Length) return $"listsOffset {at} is outside the file";

            int sectionHeader = b[at], groups = b[at + 1], difficulties = b[at + 2], perList = b[at + 3];
            if (sectionHeader < ListsSectionMinHeader) return $"lists header {sectionHeader} is below {ListsSectionMinHeader}";
            if (groups < 1) return "lists have no groups";
            if (difficulties < Difficulties) return $"lists have {difficulties} difficulties, the game needs {Difficulties}";
            if (perList < 1) return "lists are empty";

            long cutoffsAt = at + sectionHeader;
            long valuesAt = cutoffsAt + (groups - 1) * 2L;
            long end = valuesAt + (long)groups * difficulties * perList * 2;
            if (end > b.Length) return $"lists run past the end ({end} > {b.Length})";

            var c = new int[groups - 1];
            for (int i = 0; i < c.Length; i++) {
                c[i] = ReadUShort(b, (int)(cutoffsAt + i * 2));
                if (c[i] < 1) return $"cutoff {i} is {c[i]}";
                if (i > 0 && c[i] <= c[i - 1]) return $"cutoffs do not rise at {i} ({c[i - 1]}, {c[i]})";
            }

            var l = new float[groups * Difficulties][];
            for (int g = 0; g < groups; g++) {
                float lastSum = 0;
                for (int d = 0; d < Difficulties; d++) {
                    var list = new float[perList];
                    float sum = 0;
                    long from = valuesAt + ((long)(g * difficulties + d) * perList) * 2;
                    for (int i = 0; i < perList; i++) {
                        float m = ReadUShort(b, (int)(from + i * 2)) / 100f;
                        if (m < MinMultiple || m > MaxMultiple) return $"group {g} difficulty {d} rival {i} is {m}";
                        if (i > 0 && m > list[i - 1]) return $"group {g} difficulty {d} rises at rival {i}";
                        list[i] = m;
                        sum += m;
                    }
                    if (d > 0 && sum <= lastSum) return $"group {g}: difficulty {d} is not harder than {d - 1}";
                    lastSum = sum;
                    l[g * Difficulties + d] = list;
                }
            }

            cutoffs = c;
            lists = l;
            return null;
        }

        // ---- the index -------------------------------------------------------------------

        static int EntryPos(int i) => _indexPos + i * _entrySize;

        /// <summary>The score recording <paramref name="i"/> ends its 48 hours on.</summary>
        public static int TotalAt(int i) => ReadUShort(_blob, EntryPos(i) + TotalAtEntry);

        /// <summary>Which player it came from, hashed — see <see cref="OwnerOf"/>.</summary>
        public static uint OwnerAt(int i) => (uint)ReadInt(_blob, EntryPos(i) + OwnerAtEntry);

        /// <summary>
        /// The first recording scoring at least <paramref name="total"/>, or <see cref="Count"/>
        /// when none does. The index is sorted, so a span of candidates for "ends near 48" is
        /// this and a scan forward.
        /// </summary>
        public static int LowerBound(int total) {
            if (!Load()) return 0;
            int lo = 0, hi = _count;
            while (lo < hi) {
                int mid = (lo + hi) >> 1;
                if (TotalAt(mid) < total) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        // ---- the wins --------------------------------------------------------------------

        /// <summary>
        /// Recording <paramref name="i"/>'s wins, still packed — one ushort each, in time order.
        /// Read once when a rival is chosen and kept by the caller, so the blob can be replaced
        /// underneath a running tournament without its opponents changing.
        /// </summary>
        public static ushort[] PlaysAt(int i) {
            if (!Load()) return Array.Empty<ushort>();
            int at = _playsPos + ReadInt(_blob, EntryPos(i) + OffsetAtEntry);
            int n = ReadUShort(_blob, at);
            at += 2;

            var plays = new ushort[n];
            for (int k = 0; k < n; k++, at += _playSize) plays[k] = ReadUShort(_blob, at);
            return plays;
        }

        /// <summary>Minutes from the start of the window, 0..<see cref="MaxMinute"/>.</summary>
        public static int MinuteOf(ushort play) => play >> PointsBits;

        /// <summary>Bones still standing when that level was won, 1..3.</summary>
        public static int PointsOf(ushort play) => (play & PointsMask) + 1;

        // ---- owners ----------------------------------------------------------------------

        /// <summary>
        /// A player id as the blob stores it: the first four bytes of its SHA-256, which is what
        /// the server wrote (the first eight hex characters of the same digest).
        ///
        /// It exists so a player can recognise their OWN recordings and skip them — meeting your
        /// own habits, and later your own skins, worn by a stranger is the one thing this data
        /// could get wrong. It is truncated and one-way because every player downloads the blob:
        /// it answers "is this me?" for the one id the asker already has, and nothing else.
        /// </summary>
        public static uint OwnerOf(string userId) {
            if (string.IsNullOrEmpty(userId)) return NoOwner;
            using (var sha = SHA256.Create()) {
                byte[] d = sha.ComputeHash(Encoding.UTF8.GetBytes(userId));
                return (uint)((d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3]);
            }
        }

        // ---- validation (editor / download time) ------------------------------------------

        /// <summary>
        /// Walk the whole blob the way the game will read it: the header, the lists, every index
        /// entry, every offset, every win. Returns null when it is sound, or why it is not.
        ///
        /// What gets baked is therefore guaranteed readable by exactly the code that will read
        /// it — the same deal <see cref="WinStats.ValidateBlob"/> makes. A file from before the
        /// lists fails here: the server has them, so one without them did not come from it.
        /// </summary>
        public static string Validate(byte[] b) {
            string err = CheckLight(b, out _);
            if (err != null) return err;

            int headerSize = b[5], entrySize = b[6], playSize = b[7];
            int count = ReadInt(b, 8);
            int playsPos = headerSize + count * entrySize;

            int lastTotal = -1;
            for (int i = 0; i < count; i++) {
                int e = headerSize + i * entrySize;
                int total = ReadUShort(b, e + TotalAtEntry);
                if (total < lastTotal) return $"entry {i}: index is not sorted by total ({total} after {lastTotal})";
                lastTotal = total;

                long at = (long)playsPos + (uint)ReadInt(b, e + OffsetAtEntry);
                if (at + 2 > b.Length) return $"entry {i}: offset runs past the end";

                int n = ReadUShort(b, (int)at);
                at += 2;
                if (n < 1) return $"entry {i}: a recording with no wins";
                if (at + (long)n * playSize > b.Length) return $"entry {i}: {n} wins run past the end";

                int sum = 0;
                int lastMinute = -1;
                for (int k = 0; k < n; k++, at += playSize) {
                    ushort play = ReadUShort(b, (int)at);
                    int minute = MinuteOf(play), points = PointsOf(play);
                    if (minute > MaxMinute) return $"entry {i}, win {k}: minute {minute} is outside the window";
                    if (minute < lastMinute) return $"entry {i}, win {k}: wins are out of order";
                    lastMinute = minute;
                    sum += points;
                }
                if (sum != total) return $"entry {i}: wins sum to {sum}, the index says {total}";
            }
            return null;
        }

        // ---- little-endian reads ----------------------------------------------------------

        static int ReadInt(byte[] b, int at) =>
            b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

        static long ReadUInt(byte[] b, int at) => (uint)ReadInt(b, at);

        static ushort ReadUShort(byte[] b, int at) => (ushort)(b[at] | (b[at + 1] << 8));
    }
}
