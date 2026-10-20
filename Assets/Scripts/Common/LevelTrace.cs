using System.Collections.Generic;
using System.Text;

namespace qp {

    /// <summary>
    /// The attempt move by move, for the lvl_trace event.
    ///
    /// RECORDING is dumb on purpose: every change is appended as it happens (<see cref="Add"/>), raw,
    /// into <see cref="LastPlayData.trace"/> — no state, no merging. PACKING happens once, when the
    /// attempt ends (<see cref="Pack"/>): strokes become one token, a queen double-tap's passing X
    /// goes, and the result is capped to what the event can carry.
    ///
    /// Kinds — cell = row * size + col, seconds = the level's play clock (what lvl_time_sec reports):
    ///   Q / W   the player put a correct / wrong queen
    ///   B       queen boost
    ///   A       an applied hint put a queen
    ///   C       continue after fail (cell 0)
    ///   R       came back to a saved board (cell 0) — with the minutes since it was last saved
    ///           (3 chars, up to 46655 ≈ 32 days; 46655 also = unknown)
    ///   H       a hint was shown — with its technique (SolveTechnique number) and size: Trial size 0
    ///           = an X proven by contradiction, Trial size 1 = no certain move, a queen shown,
    ///           technique 0 = "clear this wrong X"
    ///   X / E   the player marked / erased X
    ///   U       undo flipped a mark back
    ///   P       an applied hint flipped a mark
    ///   !       some moves were dropped for room (marks first; queens and hints always kept)
    /// Marks never touch a queen, so U and P are a flip between X and empty.
    ///
    /// RAW (saved): kind + cell (2) + seconds (3), H + technique (1) + size (1), R + minutes away (3).
    /// Base 36 (0-9 a-z).
    ///
    /// PACKED (sent; tr_v 3):
    ///   kind + cell (2) + TIME (1, or 4) [H: + technique (1) + size (1)] [R: + minutes away (3)]
    ///   TIME is the seconds since the token before (since 0 for the first): one char 0..34, or 'z'
    ///   and the absolute seconds (3) when the gap is 35 s or more.
    ///   The mark kinds (X E U P) continue the token before while it is the same kind, nothing else
    ///   came between, and the cell came at most 1 s after the token's first — a drag, even one that
    ///   crosses a second; no cell's time is more than 1 s off — up to 35 cells. A mark token adds how many cells (1) + one char per
    ///   further cell: its step from the cell before, (dr+2)*5 + (dc+2) when both steps are within 2
    ///   ('0'..'o'), else 'z' + the cell (2). Its TIME is its first cell's.
    ///   "X0n03ii" at the start of a 10x10 trace = cells 23, 34, 45 at 0 s, then next token's time
    ///   counts from 0 s.
    /// </summary>
    public static class LevelTrace {

        // Sent as tr_v. Bump it whenever the PACKED format changes, so old and new can both be read —
        // and teach the reader the new version too: TraceCodec.Decode in the Trace Player
        // (GPRecorder/Editor/TracePlayerWindow.cs), or it reads the new traces by the old rules.
        // 1: absolute 3-char times, marks merged within one second. 2: gap times, drags merged.
        // 3: R carries the minutes away.
        public const int Version = 3;

        const string B36 = "0123456789abcdefghijklmnopqrstuvwxyz";
        public const int EventChars = 960;    // one lvl_trace event: 10 parameters of 96 chars
        public const int MaxEvents = 3;       // a long trace goes out as up to this many events
        public const int PackedMax = EventChars * MaxEvents;
        public const int AwayUnknown = 46655;   // the most 3 chars hold: 'zzz'

        // The RAW (saved) format, kept in LastPlayData.traceV. Bump it whenever Add writes something
        // an older Pack would misread. A trace saved in another format (the app updated mid-level) is
        // not read at all: it starts over with '!' — the moves before the update are lost, and the
        // '!' says so — so Pack only ever knows today's format.
        // 0/1: builds with tr_v 1-2 (R had no minutes). 2: R + minutes away.
        public const int RawVersion = 2;
        // The raw trace exists only to be sent, so it is sized from what can be sent: a raw cell is
        // 6 chars and a packed one at least 1, so the cut always happens in Pack (which keeps queens
        // and hints), never here first. Typical attempts stay at 1-2 KB.
        const int RawMax = PackedMax * 6;
        const int RawMarksStop = RawMax - 600;

        static bool IsMark(char k) => k == 'X' || k == 'E' || k == 'U' || k == 'P';

        /// <summary>Append one raw token. Marks stop near the cap, so queens and hints always fit.</summary>
        public static void Add(LastPlayData d, char kind, int cell, int seconds, int technique = -1, int size = 0, int awayMin = 0) {
            d.trace ??= "";
            if (d.trace.Length > 0 && d.traceV != RawVersion) d.trace = "!";   // saved by another build: start over
            d.traceV = RawVersion;
            if (d.trace.Length > (IsMark(kind) ? RawMarksStop : RawMax)) {
                if (d.trace.IndexOf('!') < 0) d.trace += "!";
                return;
            }
            var sb = new StringBuilder(d.trace, d.trace.Length + 8);
            sb.Append(kind);
            Num(sb, cell, 2);
            Num(sb, seconds, 3);
            if (kind == 'H') { Num(sb, technique, 1); Num(sb, size, 1); }
            if (kind == 'R') Num(sb, awayMin, 3);
            d.trace = sb.ToString();
        }

        struct Tok { public char kind; public int cell, sec, technique, size, awayMin; public bool drop; }

        /// <summary>The raw trace as the event sends it — see the class summary. Pure: testable outside Unity.</summary>
        public static string Pack(string raw, int n) {
            if (string.IsNullOrEmpty(raw)) return "";
            bool cut = false;
            var toks = new List<Tok>();
            for (int p = 0; p < raw.Length;) {
                char k = raw[p++];
                if (k == '!') { cut = true; continue; }
                if (p + 5 > raw.Length) break;   // a torn tail (should not happen)
                var t = new Tok { kind = k, cell = Read(raw, ref p, 2), sec = Read(raw, ref p, 3) };
                if (k == 'H') { if (p + 2 > raw.Length) break; t.technique = Read(raw, ref p, 1); t.size = Read(raw, ref p, 1); }
                if (k == 'R') { if (p + 3 > raw.Length) break; t.awayMin = Read(raw, ref p, 3); }
                toks.Add(t);
            }

            // A queen double-tap: the first tap painted an X, the second tap's touch-down erased it,
            // then the queen landed — all on one cell, within a moment. Only the queen is a move.
            for (int i = 0; i < toks.Count; i++) {
                if (toks[i].kind != 'Q' && toks[i].kind != 'W') continue;
                int j = i - 1;
                if (j >= 0 && Tap(toks[j], 'E', toks[i])) { Drop(toks, j); j--; }
                if (j >= 0 && Tap(toks[j], 'X', toks[i])) Drop(toks, j);
            }

            // Group: a mark run (same kind, nothing between, each cell at most 1 s after the run's FIRST
            // cell — so the run's one time is never more than 1 s off for any of its cells) is one
            // group; anything else is a group of its own. A group's time is its first cell's.
            var groups = new List<(Tok head, List<int> cells)>();
            for (int i = 0; i < toks.Count; i++) {
                var t = toks[i];
                if (t.drop) continue;
                var cells = new List<int> { t.cell };
                if (IsMark(t.kind)) {
                    while (i + 1 < toks.Count && cells.Count < 35) {
                        var u = toks[i + 1];
                        if (u.drop || u.kind != t.kind || u.sec - t.sec > 1) break;
                        cells.Add(u.cell); i++;
                    }
                }
                groups.Add((t, cells));
            }

            // Encode in order, each time as the gap from the last group WRITTEN. Everything that is not
            // a mark always goes in — its room is held back at the worst size (a 4-char time) — and
            // marks fill what is left; a mark group that does not fit is dropped and the trace cut.
            int fixedLeft = 0;
            foreach (var g in groups) if (!IsMark(g.head.kind)) fixedLeft += WorstSize(g.head.kind);
            var outSb = new StringBuilder(PackedMax);
            int prevSec = 0;
            foreach (var (head, cells) in groups) {
                bool mark = IsMark(head.kind);
                string s = Encode(head, cells, prevSec, n);
                if (!mark) fixedLeft -= WorstSize(head.kind);
                if (outSb.Length + s.Length + fixedLeft > PackedMax - 1) {
                    cut = true;
                    if (mark) continue;   // drop this mark group, keep going
                    break;                // pathological: queens/hints alone overflow
                }
                outSb.Append(s);
                prevSec = head.sec;
            }
            if (cut) outSb.Append('!');
            return outSb.ToString();
        }

        // The most a token that is not a mark can take (its time at 4 chars) — the room held back for it.
        static int WorstSize(char kind) => kind == 'H' ? 9 : kind == 'R' ? 10 : 7;

        // One packed token — see the class summary.
        static string Encode(Tok head, List<int> cells, int prevSec, int n) {
            var sb = new StringBuilder(8 + cells.Count);
            sb.Append(head.kind);
            Num(sb, head.cell, 2);
            int gap = head.sec - prevSec;
            if (gap >= 0 && gap < 35) sb.Append(B36[gap]);
            else { sb.Append('z'); Num(sb, head.sec, 3); }
            if (head.kind == 'H') { Num(sb, head.technique, 1); Num(sb, head.size, 1); }
            if (head.kind == 'R') Num(sb, head.awayMin, 3);
            if (IsMark(head.kind)) {
                sb.Append(B36[cells.Count]);
                for (int i = 1; i < cells.Count; i++) {
                    int dr = cells[i] / n - cells[i - 1] / n, dc = cells[i] % n - cells[i - 1] % n;
                    if (dr >= -2 && dr <= 2 && dc >= -2 && dc <= 2) sb.Append(B36[(dr + 2) * 5 + dc + 2]);
                    else { sb.Append('z'); Num(sb, cells[i], 2); }
                }
            }
            return sb.ToString();
        }

        // `mark` of the same cell just before the queen, at most 2 seconds earlier
        static bool Tap(Tok mark, char kind, Tok queen) =>
            !mark.drop && mark.kind == kind && mark.cell == queen.cell && queen.sec - mark.sec <= 2;

        static void Drop(List<Tok> toks, int i) { var t = toks[i]; t.drop = true; toks[i] = t; }

        // fixed width, clamped to what the width can hold
        static void Num(StringBuilder sb, int value, int width) {
            int max = 1;
            for (int i = 0; i < width; i++) max *= 36;
            if (value < 0) value = 0;
            if (value > max - 1) value = max - 1;
            int at = sb.Length;
            sb.Append('0', width);
            for (int i = width - 1; i >= 0; i--) { sb[at + i] = B36[value % 36]; value /= 36; }
        }

        static int Read(string s, ref int p, int width) {
            int v = 0;
            for (int i = 0; i < width; i++) v = v * 36 + B36.IndexOf(s[p++]);
            return v;
        }
    }
}
