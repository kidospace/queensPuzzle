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
    /// Tokens — the kind letter, then numbers in base 36 (0-9 a-z); cell = row * size + col,
    /// seconds = the level's play clock (what lvl_time_sec reports):
    ///   Q / W   the player put a correct / wrong queen          kind + cell (2) + seconds (3)
    ///   B       queen boost                                     "
    ///   A       an applied hint put a queen                     "
    ///   C       continue after fail (cell 0)                    "
    ///   R       came back to a saved board (cell 0)             "
    ///   H       a hint was shown                                " + technique (1) + size (1)
    ///             technique = SolveTechnique number; Trial size 0 = an X proven by contradiction,
    ///             Trial size 1 = no certain move, a queen shown; 0 = "clear this wrong X"
    ///   X / E   the player marked / erased X                    kind + cell (2) + seconds (3)
    ///   U       undo flipped a mark back                        "
    ///   P       an applied hint flipped a mark                  "
    ///   !       some moves were dropped for room (marks first; queens and hints always kept)
    /// In the PACKED form the four mark kinds (X E U P) of one kind in one second are one token:
    ///   kind + first cell (2) + seconds (3) + how many cells (1) + one char per further cell —
    ///   its step from the cell before, (dr+2)*5 + (dc+2) when both steps are within 2 ('0'..'o'),
    ///   else 'z' + the cell (2). "X0n01m3ii" = on 10x10, cells 23, 34, 45 at 58 seconds.
    /// Marks never touch a queen, so U and P are a flip between X and empty.
    /// </summary>
    public static class LevelTrace {

        // Sent as tr_v. Bump it whenever the PACKED format changes, so old and new can both be read.
        public const int Version = 1;

        const string B36 = "0123456789abcdefghijklmnopqrstuvwxyz";
        const int RawMax = 6000;              // the saved raw trace — a guard, real attempts stay far below
        const int RawMarksStop = RawMax - 600;
        public const int PackedMax = 960;     // 10 event parameters of 96 chars

        static bool IsMark(char k) => k == 'X' || k == 'E' || k == 'U' || k == 'P';

        /// <summary>Append one raw token. Marks stop near the cap, so queens and hints always fit.</summary>
        public static void Add(LastPlayData d, char kind, int cell, int seconds, int technique = -1, int size = 0) {
            d.trace ??= "";
            if (d.trace.Length > (IsMark(kind) ? RawMarksStop : RawMax)) {
                if (d.trace.IndexOf('!') < 0) d.trace += "!";
                return;
            }
            var sb = new StringBuilder(d.trace, d.trace.Length + 8);
            sb.Append(kind);
            Num(sb, cell, 2);
            Num(sb, seconds, 3);
            if (kind == 'H') { Num(sb, technique, 1); Num(sb, size, 1); }
            d.trace = sb.ToString();
        }

        struct Tok { public char kind; public int cell, sec, technique, size; public bool drop; }

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

            // Encode: everything that is not a mark always goes in; marks fill what is left, in order.
            var pieces = new List<(string s, bool mark)>();
            int fixedLen = 0;
            for (int i = 0; i < toks.Count; i++) {
                var t = toks[i];
                if (t.drop) continue;
                var sb = new StringBuilder(12);
                sb.Append(t.kind); Num(sb, t.cell, 2); Num(sb, t.sec, 3);
                if (!IsMark(t.kind)) {
                    if (t.kind == 'H') { Num(sb, t.technique, 1); Num(sb, t.size, 1); }
                    pieces.Add((sb.ToString(), false));
                    fixedLen += sb.Length;
                    continue;
                }
                // a run of the same mark kind in the same second → one token
                int count = 1, prev = t.cell;
                var steps = new StringBuilder();
                while (i + 1 < toks.Count && count < 35) {
                    var u = toks[i + 1];
                    if (u.drop || u.kind != t.kind || u.sec != t.sec) break;
                    int dr = u.cell / n - prev / n, dc = u.cell % n - prev % n;
                    if (dr >= -2 && dr <= 2 && dc >= -2 && dc <= 2) steps.Append(B36[(dr + 2) * 5 + dc + 2]);
                    else { steps.Append('z'); Num(steps, u.cell, 2); }
                    prev = u.cell; count++; i++;
                }
                sb.Append(B36[count]).Append(steps);
                pieces.Add((sb.ToString(), true));
            }

            var outSb = new StringBuilder(PackedMax);
            int markRoom = PackedMax - 1 - fixedLen;
            foreach (var (s, mark) in pieces) {
                if (mark) {
                    if (s.Length > markRoom) { cut = true; continue; }
                    markRoom -= s.Length;
                }
                if (outSb.Length + s.Length > PackedMax - 1) { cut = true; break; }   // pathological: too many queens/hints
                outSb.Append(s);
            }
            if (cut) outSb.Append('!');
            return outSb.ToString();
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
