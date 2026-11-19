// LevelGen — the level builder outside Unity. Everything that decides anything about a board is
// the game's own code (PoolBuilder, LevelGenerator, WeightRater, TimeRater, CampaignCurve), linked
// from Assets. This file only reads and writes files.
//
//   LevelGen build <job file> <set folder>     fill the job's slots, one .asset per level in the folder
//   LevelGen sim   <job file> <boards>         the same, one board after another and nothing written:
//                                              a fixed order, to compare with the editor line by line
//   LevelGen rate  <set folder> [max level]    rate the levels of a set (weight, seconds)
//   LevelGen fill  <drawing> <set folder> <first number> <how many> [tries]
//                                              complete a drawing (a text grid: a palette colour per cell, "." = empty)
//                                              into levels spread from its shortest to its longest, what was drawn untouched
//
// The job file is what the editor writes with PoolBuilder.Pack: gates, options and the slots.
// To stop a build: create "<job file>.stop" (or Ctrl+C). Levels already written stay.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using QueensPuzzle;

static class Program
{
    static int Main(string[] args)
    {
        // "--log <file>": everything printed goes to that file instead, line by line as it happens —
        // what the editor's window shows. The file can be read while it is being written.
        int logAt = Array.IndexOf(args, "--log");
        if (logAt >= 0 && logAt + 1 < args.Length)
        {
            var file = new FileStream(args[logAt + 1], FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var log = new StreamWriter(file, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
            args = args.Take(logAt).Concat(args.Skip(logAt + 2)).ToArray();
        }
        else Console.OutputEncoding = Encoding.UTF8;
        try { return Run(args); }
        catch (Exception e) { Console.WriteLine("failed: " + e); return 2; }
    }

    static int Run(string[] args)
    {
        if (args.Length >= 3 && args[0] == "build") return Build(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "sim") return Sim(args[1], int.Parse(args[2]));
        if (args.Length >= 3 && args[0] == "colors") return Colors(args[1], args[2]);
        if (args.Length >= 5 && args[0] == "fill") return Fill(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), args.Length > 5 ? int.Parse(args[5]) : 20000);
        if (args.Length >= 2 && args[0] == "rate") return Rate(args[1], args.Length > 2 ? int.Parse(args[2]) : int.MaxValue);
        Console.Error.WriteLine("usage: LevelGen build <job file> <set folder> | sim <job file> <boards> | rate <set folder> [max level]");
        return 1;
    }

    // ---- build ---------------------------------------------------------------------------------

    static int Build(string jobFile, string folder)
    {
        PoolBuilder.Unpack(File.ReadAllText(jobFile), out var gates, out var open, out var opt);
        Directory.CreateDirectory(folder);
        string stopFile = jobFile + ".stop";
        if (File.Exists(stopFile)) File.Delete(stopFile);
        int slots = open.Count;
        int drawnSlots = open.Count(t => t.drawn);
        Console.WriteLine($"build: {slots} slots{(drawnSlots > 0 ? $" ({drawnSlots} drawn, {opt.drawings.Count} drawings in the pool)" : "")} · threads {opt.threadCount} · seed base {opt.seedBase} · colours {(opt.colors == null ? "off" : "on")} · into {folder}");

        var clock = Stopwatch.StartNew();
        double lastLine = 0;
        var (made, attempts, uncolourable, undrawn) = PoolBuilder.Run(gates, open, opt,
            f =>
            {
                WriteLevel(folder, f);
                Console.WriteLine($"{clock.Elapsed:hh\\:mm\\:ss} level {f.slot.level} filled · {f.slot.boardSize}x{f.slot.boardSize} · asks {f.slot.targetSeconds:0} s · seed {f.seed}"
                                + (f.revealedRows == null ? "" : $" · queen revealed in row {f.revealedRows[0]}")
                                + (f.drawing == 0 ? "" : $" · drawing {f.drawing} ({opt.drawings.Find(d => d.number == f.drawing).name})"));
            },
            (done, left, boards) =>
            {
                if (clock.Elapsed.TotalSeconds - lastLine >= 10)
                {
                    lastLine = clock.Elapsed.TotalSeconds;
                    Console.WriteLine($"{clock.Elapsed:hh\\:mm\\:ss} filled {done} · open {left} · boards {boards} · {boards / Math.Max(1, clock.Elapsed.TotalSeconds):0} a second");
                }
                return !File.Exists(stopFile);
            });

        Console.WriteLine($"done: filled {made} of {slots} · boards {attempts} · {clock.Elapsed.TotalSeconds:0} s"
                        + (uncolourable > 0 ? $" · {uncolourable} board(s) discarded: no colour set fits" : "")
                        + (undrawn > 0 ? $" · {undrawn} drawn slot(s) built as ordinary levels: no drawing of their size whose kind is free there" : "")
                        + (open.Count > 0 ? " · still open: " + string.Join(",", open.Select(t => t.level)) : ""));
        return 0;
    }

    // A level file exactly as Unity writes one (LevelData). An existing file keeps its .meta, so the
    // level keeps its identity; a new one gets its .meta from Unity the next time the editor looks.
    static void WriteLevel(string folder, in PoolBuilder.Filled f)
    {
        var sb = new StringBuilder();
        sb.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n")
          .Append("  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n")
          .Append("  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n")
          .Append("  m_Script: {fileID: 11500000, guid: 878b0ea1c7b752745bfb146f9e70659b, type: 3}\n")
          .Append("  m_Name: ").Append(f.slot.level).Append("\n  m_EditorClassIdentifier: \n")
          .Append("  size: ").Append(f.slot.boardSize).Append('\n')
          .Append("  regions: ").Append(Hex(f.region)).Append('\n')
          .Append("  solutionColumns: ").Append(Hex(f.cols)).Append('\n')
          .Append("  weight: ").Append(f.weight).Append('\n')
          .Append("  seed: ").Append(f.seed).Append('\n')
          .Append("  revealedRows: ").Append(Hex(f.revealedRows)).Append('\n')
          .Append("  regionColors: ").Append(Hex(f.regionColors)).Append('\n')
          .Append("  drawing: ").Append(f.drawing).Append('\n');
        string path = Path.Combine(folder, f.slot.level + ".asset");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString());       // whole file first, then swapped in: the editor never sees half a level
        File.Move(tmp, path, true);
    }

    // ---- sim -----------------------------------------------------------------------------------

    static int Sim(string jobFile, int boards)
    {
        PoolBuilder.Unpack(File.ReadAllText(jobFile), out var gates, out var open, out var opt);
        for (int s = opt.seedBase; s < opt.seedBase + boards && open.Count > 0; s++)
        {
            int size = open[(s - opt.seedBase) % open.Count].boardSize;
            if (!LevelGenerator.TryGenerateRaw(size, s, 250, out int[] region, out int[] cols, out _))
            {
                Console.WriteLine($"{{\"seed\":{s},\"size\":{size},\"ok\":false}}");
                continue;
            }
            var rep = WeightRater.Rate(size, region, cols);
            uint h = 2166136261;
            foreach (int v in region) { h ^= (uint)v; h *= 16777619; }
            foreach (int v in cols) { h ^= (uint)v; h *= 16777619; }
            int level = -1, row = -1, weight = rep.weight;
            double secs = -1;
            if (rep.trials == 0)
            {
                var time = TimeRater.Rate(size, region, cols, rep);
                secs = time.seconds;
                int slot = PoolBuilder.FindSlot(gates, open, size, region, cols, rep, time, out int[] revealed, out weight);
                if (slot >= 0)
                {
                    level = open[slot].level;
                    row = revealed == null ? -1 : revealed[0];
                    open.RemoveAt(slot);
                }
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"seed\":{0},\"size\":{1},\"ok\":true,\"hash\":{2},\"trials\":{3},\"seconds\":{4:R},\"level\":{5},\"row\":{6},\"weight\":{7}}}",
                s, size, h, rep.trials, secs, level, row, weight));
        }
        return 0;
    }

    // ---- fill ----------------------------------------------------------------------------------

    static int Fill(string drawingFile, string folder, int first, int howMany, int tries)
    {
        var drawing = StencilFiller.Drawing.Parse(File.ReadAllText(drawingFile), out string bad);
        if (drawing == null) { Console.WriteLine("failed: " + bad); return 1; }
        int n = drawing.size;
        int[] paint = drawing.paint;
        int[] ids = StencilFiller.FromPalette(paint, out int[] idToColor);
        string problem = idToColor.Length >= n ? "every colour is used — nothing left to fill the empty cells with" : StencilFiller.ValidateStencil(n, ids);
        if (problem != null) { Console.WriteLine("failed: " + problem); return 1; }

        var clock = Stopwatch.StartNew();
        var all = StencilFiller.Complete(n, ids, tries, 1000, null, howMany * 40);
        Console.WriteLine($"{Path.GetFileNameWithoutExtension(drawingFile)}: {n}x{n}, {idToColor.Length} painted colours · {all.Count} completions in {clock.Elapsed.TotalSeconds:0.0} s"
                        + (all.Count > 0 ? $" · from {all[0].time.seconds:0} s to {all[all.Count - 1].time.seconds:0} s" : ""));
        if (all.Count == 0) return 0;

        Directory.CreateDirectory(folder);
        int[] colors = LevelColors.Stored(StencilFiller.Colors(n, idToColor));
        for (int k = 0; k < Math.Min(howMany, all.Count); k++)
        {
            var f = all[howMany == 1 ? all.Count / 2 : (int)Math.Round((all.Count - 1) * (double)k / (howMany - 1))];   // spread over the range
            WriteLevel(folder, new PoolBuilder.Filled
            {
                slot = new LevelTarget { level = first + k, boardSize = n },
                region = f.region, cols = f.cols, weight = f.rep.weight, seed = f.seed, regionColors = colors,
            });
            Console.WriteLine($"  level {first + k}: {f.time.seconds:0} s · wall {f.time.wall:0.00} · start {f.time.start:0.00} · {CampaignCurve.TrickSteps(f.rep)} trick steps");
            for (int r = 0; r < n; r++)
                Console.WriteLine("     " + string.Join(" ", Enumerable.Range(0, n).Select(c => ((char)(f.region[r * n + c] < idToColor.Length ? 'A' + f.region[r * n + c] : 'a' + f.region[r * n + c])).ToString())));
        }
        return 0;
    }

    // ---- colors --------------------------------------------------------------------------------

    // A check: the colours the job's rules give each level of a set, and whether the colours
    // stored on it break the rules.
    static int Colors(string jobFile, string dir)
    {
        var rules = LevelColors.Unpack(File.ReadAllText(jobFile));
        if (rules == null) { Console.Error.WriteLine("the job carries no colour rules"); return 1; }
        foreach (var path in Directory.GetFiles(dir, "*.asset"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(path), out int n)) continue;
            string y = File.ReadAllText(path);
            int size = int.Parse(Field(y, "size"));
            int[] region = Ints(Field(y, "regions")), stored = Ints(Field(y, "regionColors"));
            var adj = LevelColors.Adjacency(size, region);
            int[] pick = LevelColors.Pick(size, n, rules, adj);
            Console.WriteLine($"{n} {(pick == null ? "null" : string.Join(",", pick))} {(LevelColors.HasIssues(size, stored, rules, adj) ? 1 : 0)}");
        }
        return 0;
    }

    // ---- rate ----------------------------------------------------------------------------------

    static int Rate(string dir, int max)
    {
        var files = Directory.GetFiles(dir, "*.asset")
            .Select(f => (path: f, ok: int.TryParse(Path.GetFileNameWithoutExtension(f), out int n), n))
            .Where(x => x.ok && x.n <= max).OrderBy(x => x.n).ToList();
        int rated = 0, failed = 0;
        var clock = Stopwatch.StartNew();
        foreach (var (path, _, n) in files)
        {
            try
            {
                string y = File.ReadAllText(path);
                int size = int.Parse(Field(y, "size"));
                int[] region = Ints(Field(y, "regions")), sol = Ints(Field(y, "solutionColumns")), revealed = Ints(Field(y, "revealedRows"));
                var rep = WeightRater.Rate(size, region, sol, revealed);
                var time = TimeRater.Rate(size, region, sol, rep, revealed);
                rated++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"level\":{0},\"size\":{1},\"revealed\":{2},\"storedWeight\":{3},\"weight\":{4},\"trials\":{5},\"seconds\":{6:R},\"wall\":{7:R},\"start\":{8:R},\"steps\":{9}}}",
                    n, size, revealed.Length, Field(y, "weight"), rep.weight, rep.trials, time.seconds, time.wall, time.start, CampaignCurve.TrickSteps(rep)));
            }
            catch (Exception e) { failed++; Console.Error.WriteLine($"level {n}: {e.GetType().Name}: {e.Message}"); }
        }
        Console.Error.WriteLine($"-- {rated} rated in {clock.Elapsed.TotalSeconds:0.0}s, failed: {failed}");
        return 0;
    }

    // ---- Unity's text form of a level ------------------------------------------------------------

    static string Field(string yaml, string name)
    {
        var m = Regex.Match(yaml, @"^  " + name + @": ?(.*)$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    // an int[] is hex, 4 bytes a value, low byte first
    static int[] Ints(string hex)
    {
        var a = new int[hex.Length / 8];
        for (int i = 0; i < a.Length; i++)
        {
            uint v = 0;
            for (int b = 0; b < 4; b++) v |= (uint)Convert.ToByte(hex.Substring(i * 8 + b * 2, 2), 16) << (8 * b);
            a[i] = (int)v;
        }
        return a;
    }

    static string Hex(int[] a)
    {
        if (a == null) return "";
        var sb = new StringBuilder(a.Length * 8);
        foreach (int v in a)
            for (int b = 0; b < 4; b++) sb.Append((((uint)v >> (8 * b)) & 0xff).ToString("x2"));
        return sb.ToString();
    }
}
