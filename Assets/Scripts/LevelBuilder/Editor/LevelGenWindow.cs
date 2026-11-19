using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Level builds running OUTSIDE the editor (Tools/LevelGen): start one, watch them, stop them.
    ///
    /// A run is a separate program, so the editor stays free and several runs can go at once; it
    /// survives a script compile and entering Play. It builds with the game's own code — the same
    /// PoolBuilder the in-editor build uses. Its levels wait in the run's own folder and are moved
    /// into the set in one go when it ends, so the editor imports once and not all along.
    ///
    /// Nothing is kept in memory here: every run has a folder under Library/LevelGen/runs with its
    /// job, its log and a few facts, and the window only reads those. The log is read from where
    /// the last read stopped, so a long run costs nothing to watch.
    /// </summary>
    public class LevelGenWindow : EditorWindow
    {
        const int SeedStep = 10000000;   // each run draws its boards from its own stretch of seeds
        const int LogLines = 200;

        static string Root => Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        static string RunsDir => Root + "/Library/LevelGen/runs";
        static string Exe => Root + "/Library/LevelGen/build/LevelGen.exe";
        static string ToolDir => Root + "/Tools/LevelGen";

        [MenuItem("QueensPuzzle/Level Gen Runs")]
        static void Open() => GetWindow<LevelGenWindow>("Level Gen Runs").minSize = new Vector2(520, 320);

        // ---- starting a run ------------------------------------------------------------------

        /// <summary>
        /// Starts a build of these open slots outside the editor, for
        /// <paramref name="outputFolder"/> (an Assets/... set folder). The levels wait in the run's
        /// folder and are moved into the set when the run ends — or earlier, on Import. Returns
        /// false, with the reason shown, when it cannot start.
        /// </summary>
        public static bool Start(string title, string outputFolder, GenerationGates gates, List<LevelTarget> open, PoolBuilder.Options opt)
        {
            string folder = Root + "/" + outputFolder;
            var levels = new HashSet<int>(open.Select(t => t.level));
            var running = LoadRuns().Where(r => r.Running).ToList();
            foreach (var r in running)
                if (r.folder == folder && r.Levels().Overlaps(levels))
                {
                    EditorUtility.DisplayDialog("Level Gen", $"Run #{r.id} is already building some of these levels into {outputFolder}.\nStop it first, or pick another range.", "OK");
                    return false;
                }
            if (!(running.Count > 0 && File.Exists(Exe)) && !EnsureExe(out _buildError))
            {
                EditorUtility.DisplayDialog("Level Gen", "The builder could not be built:\n\n" + _buildError, "OK");
                return false;
            }

            Directory.CreateDirectory(RunsDir);
            int id = 1;
            foreach (var d in Directory.GetDirectories(RunsDir))
                if (int.TryParse(Path.GetFileName(d), out int n) && n >= id) id = n + 1;
            string dir = RunsDir + "/" + id;
            Directory.CreateDirectory(dir);

            opt.seedBase += SeedStep * running.Count;   // no two runs at once draw the same boards
            File.WriteAllText(dir + "/job.txt", PoolBuilder.Pack(gates, open, opt));

            var p = Process.Start(new ProcessStartInfo
            {
                FileName = Exe,
                // into the run's own folder, not the set: the editor sees nothing until the levels are imported
                Arguments = $"build \"{dir}/job.txt\" \"{dir}/levels\" --log \"{dir}/log.txt\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Root,
            });
            File.WriteAllLines(dir + "/info.txt", new[]
            {
                title, folder, p.Id.ToString(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), open.Count.ToString(),
                opt.threadCount.ToString(), opt.seedBase.ToString(),
            });
            // the window comes up (opened, or brought to the front) showing the run that just started
            Open();
            var w = GetWindow<LevelGenWindow>("Level Gen Runs");
            w._runs = LoadRuns();
            w._selected = id;
            return true;
        }

        static string _buildError;

        // The exe is built from the game's sources, so it is rebuilt whenever one of them is newer.
        static bool EnsureExe(out string error)
        {
            error = null;
            DateTime built = File.Exists(Exe) ? File.GetLastWriteTimeUtc(Exe) : DateTime.MinValue;
            var sources = new List<string>(Directory.GetFiles(ToolDir, "*.cs*", SearchOption.TopDirectoryOnly));
            foreach (string line in File.ReadAllLines(ToolDir + "/LevelGen.csproj"))
            {
                int a = line.IndexOf("Include=\"../../", StringComparison.Ordinal);
                if (a < 0) continue;
                a += "Include=\"../../".Length;
                sources.Add(Root + "/" + line.Substring(a, line.IndexOf('"', a) - a));
            }
            if (sources.All(s => File.GetLastWriteTimeUtc(s) <= built)) return true;

            EditorUtility.DisplayProgressBar("Level Gen", "Building the builder (dotnet build)…", 0.5f);
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{ToolDir}\" -c Release -v q -nologo",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                });
                string outText = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0 && File.Exists(Exe)) { File.SetLastWriteTimeUtc(Exe, DateTime.UtcNow); return true; }
                error = outText.Length > 1500 ? outText.Substring(0, 1500) : outText;
                return false;
            }
            catch (Exception e) { error = "dotnet did not start (" + e.Message + "). Is the .NET SDK installed?"; return false; }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ---- a run, as its folder tells it -----------------------------------------------------

        sealed class RunInfo
        {
            public int id, pid, slots, threads, seedBase;
            public string dir, title, folder, started;
            public long read;                 // how much of the log has been taken in
            public int filled;
            public string status = "starting…", done;
            public readonly List<string> lines = new List<string>();
            bool? alive;

            public bool Finished => done != null;
            public bool Running => !Finished && Alive;
            bool Alive
            {
                get
                {
                    if (alive == false) return false;
                    try { var p = Process.GetProcessById(pid); alive = !p.HasExited && p.ProcessName.StartsWith("LevelGen", StringComparison.OrdinalIgnoreCase); }
                    catch { alive = false; }
                    return alive.Value;
                }
            }

            /// <summary>Levels built and not yet moved into the set.</summary>
            public int Waiting => Directory.Exists(dir + "/levels") ? Directory.GetFiles(dir + "/levels", "*.asset").Length : 0;

            public HashSet<int> Levels()
            {
                var set = new HashSet<int>();
                foreach (string line in File.ReadAllLines(dir + "/job.txt"))
                    if (line.StartsWith("slot ")) set.Add(int.Parse(line.Split(' ')[1]));
                return set;
            }

            // take in what the run has written since the last look
            public void ReadLog()
            {
                string path = dir + "/log.txt";
                if (!File.Exists(path)) return;
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Length <= read) return;
                    f.Seek(read, SeekOrigin.Begin);
                    var buf = new byte[f.Length - read];
                    int got = f.Read(buf, 0, buf.Length);
                    int end = Array.LastIndexOf(buf, (byte)'\n', got - 1);   // whole lines only
                    if (end < 0) return;
                    read += end + 1;
                    foreach (string raw in Encoding.UTF8.GetString(buf, 0, end).Split('\n'))
                    {
                        string line = raw.TrimEnd('\r');
                        if (line.Length == 0) continue;
                        if (line.StartsWith("done:") || line.StartsWith("failed:")) done = line;
                        else if (line.Contains(" level ") && line.Contains(" filled ")) filled++;
                        else if (line.Contains(" filled ") && line.Contains(" boards ")) { status = line.Substring(line.IndexOf(' ') + 1); continue; }   // the 10-second summary: shown in the row, not in the log
                        lines.Add(line);
                    }
                    if (lines.Count > LogLines) lines.RemoveRange(0, lines.Count - LogLines);
                }
            }
        }

        static List<RunInfo> LoadRuns()
        {
            var list = new List<RunInfo>();
            if (!Directory.Exists(RunsDir)) return list;
            foreach (string d in Directory.GetDirectories(RunsDir))
            {
                string info = d + "/info.txt";
                if (!int.TryParse(Path.GetFileName(d), out int id) || !File.Exists(info)) continue;
                string[] l = File.ReadAllLines(info);
                if (l.Length < 7) continue;
                list.Add(new RunInfo
                {
                    id = id, dir = d.Replace('\\', '/'), title = l[0], folder = l[1], pid = int.Parse(l[2]), started = l[3],
                    slots = int.Parse(l[4]), threads = int.Parse(l[5]), seedBase = int.Parse(l[6]),
                });
            }
            list.Sort((a, b) => b.id.CompareTo(a.id));
            return list;
        }

        // ---- importing what a run wrote ---------------------------------------------------------
        // A run builds into its own folder under Library, which the editor does not watch — so a
        // run never makes the editor import anything while one works in it. The levels are moved
        // into the set in one go: when the run ends (from here and not from the window, so it
        // happens with the window closed too), or when Import is pressed on a run still going.

        /// <summary>Moves the levels a run has built so far into its set. Returns how many.</summary>
        static int Import(RunInfo r)
        {
            string from = r.dir + "/levels";
            if (!Directory.Exists(from)) return 0;
            string[] files = Directory.GetFiles(from, "*.asset");   // whole files only: the run writes ".tmp" and renames
            if (files.Length == 0) return 0;
            Directory.CreateDirectory(r.folder);
            foreach (string f in files)
            {
                // over an existing level its .meta stays, so the level keeps its identity
                File.Copy(f, r.folder + "/" + Path.GetFileName(f), true);
                File.Delete(f);
            }
            AssetDatabase.Refresh();
            return files.Length;
        }

        static double _nextWatch;

        [InitializeOnLoadMethod]
        static void Watch() => EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < _nextWatch) return;
            _nextWatch = EditorApplication.timeSinceStartup + 2.0;
            foreach (var r in LoadRuns())
                if (!r.Running && r.Waiting > 0) Import(r);
        };

        // ---- the window ------------------------------------------------------------------------

        List<RunInfo> _runs = new List<RunInfo>();
        int _selected = -1;
        Vector2 _runScroll, _logScroll;
        double _nextLook;

        void OnEnable() { _runs = LoadRuns(); foreach (var r in _runs) r.ReadLog(); }

        // about once a second: pick up new runs and what the running ones wrote
        void OnInspectorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextLook) return;
            _nextLook = EditorApplication.timeSinceStartup + 1.0;

            var fresh = LoadRuns();
            foreach (var r in fresh)
            {
                var old = _runs.Find(x => x.id == r.id);
                if (old != null) { r.read = old.read; r.filled = old.filled; r.status = old.status; r.done = old.done; r.lines.AddRange(old.lines); }
                r.ReadLog();
            }
            _runs = fresh;
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{_runs.Count(r => r.Running)} running · {_runs.Count} in all", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove all finished", EditorStyles.toolbarButton)) ClearFinished();
            }

            if (_runs.Count == 0)
            {
                EditorGUILayout.HelpBox("No runs yet. Start one from the Campaign Builder: \"Generate outside Unity\".", MessageType.Info);
                return;
            }

            _runScroll = EditorGUILayout.BeginScrollView(_runScroll, GUILayout.MaxHeight(Mathf.Min(170, 24 * _runs.Count + 8)));
            foreach (var r in _runs) DrawRun(r);
            EditorGUILayout.EndScrollView();

            var sel = _runs.Find(r => r.id == _selected) ?? _runs[0];
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Run #{sel.id} — log (the last {LogLines} lines)", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, EditorStyles.helpBox);
            for (int i = sel.lines.Count - 1; i >= 0; i--)   // newest first
                EditorGUILayout.LabelField(sel.lines[i], EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        void DrawRun(RunInfo r)
        {
            bool running = r.Running;
            string state = r.Finished ? r.done : running ? r.status : "stopped — no result line (closed or crashed)";
            using (new EditorGUILayout.HorizontalScope())
            {
                var c = GUI.color;
                GUI.color = running ? new Color(0.35f, 0.8f, 0.4f) : r.Finished ? Color.gray : new Color(0.9f, 0.5f, 0.3f);
                GUILayout.Label("●", GUILayout.Width(14));
                GUI.color = c;
                if (GUILayout.Toggle(r.id == _selected || (_selected < 0 && r == _runs[0]), $"#{r.id}", EditorStyles.miniButton, GUILayout.Width(40))) _selected = r.id;
                GUILayout.Label($"{r.title} · {r.filled}/{r.slots} · {r.threads} threads · {r.started.Substring(11, 5)}", GUILayout.Width(300));
                GUILayout.Label(state, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                int waiting = running ? r.Waiting : 0;   // a run that ended is imported by itself
                using (new EditorGUI.DisabledScope(waiting == 0))
                    if (GUILayout.Button(waiting > 0 ? $"Import {waiting}" : "Import", EditorStyles.miniButton, GUILayout.Width(74)))
                        Import(r);   // what is built so far, into the set now; the run goes on
                using (new EditorGUI.DisabledScope(!running))
                    if (GUILayout.Button("Stop", EditorStyles.miniButton, GUILayout.Width(44)))
                        File.WriteAllText(r.dir + "/job.txt.stop", "");   // the run sees it within a moment and ends cleanly
                using (new EditorGUI.DisabledScope(running))
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        Import(r);   // nothing built is lost with the row
                        Directory.Delete(r.dir, true);
                        _runs.Remove(r);
                        GUIUtility.ExitGUI();
                    }
            }
        }

        void ClearFinished()
        {
            foreach (var r in _runs)
                if (!r.Running) { Import(r); Directory.Delete(r.dir, true); }
            _runs = LoadRuns();
            _selected = -1;
        }
    }
}
