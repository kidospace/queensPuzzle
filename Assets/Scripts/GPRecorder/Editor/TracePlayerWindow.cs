using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QueensPuzzle;
using UnityEditor;
using UnityEngine;

namespace qp {

    /// <summary>
    /// Trace Player — replays a player's level trace (lvl_trace, see LevelTrace) on the real board,
    /// for debugging. Paste a row copied from the reports server (JSON: lvl_idx, daily, level_hash,
    /// tr_v, trace…) or a bare packed trace string with the level picked by hand. The trace becomes
    /// an in-memory GPRecord and plays through the GP Recorder's replay machinery (GPReplayer):
    /// same handover into play mode, same board seek. Nothing in the game changes.
    ///
    /// The bottom is a video-style seek bar: the playhead moves with the replay and can be dragged
    /// anywhere; markers show wrong queens, hints, boosts, undo, continue and coming back.
    /// Like any GP Recorder replay it starts the board fresh, so the editor's own saved attempt for
    /// the current level is reset.
    /// </summary>
    public class TracePlayerWindow : EditorWindow {

        [MenuItem("QueensPuzzle/Trace Player")]
        static void Open() => GetWindow<TracePlayerWindow>("Trace Player");

        // ---- state that survives the domain reload on play-mode entry -------------------------

        [SerializeField] string _input = "";
        [SerializeField] int _manualLevel = 1;          // 1-based, when the input is a bare trace
        [SerializeField] bool _manualDaily;
        [SerializeField] int _manualVersion = LevelTrace.Version;
        [SerializeField] GPRecord _record = new GPRecord();
        [SerializeField] List<Marker> _markers = new List<Marker>();
        [SerializeField] string _info = "";
        [SerializeField] string _error = "";
        [SerializeField] float _playhead;
        [SerializeField] float _speed = 1f;
        [SerializeField] bool _pending;                 // start the replay once play mode is up
        Vector2 _scroll;
        bool _scrubbing;

        static string TempPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "TracePlayer", "record.json"));

        /// <summary>A point on the seek bar: what happened, when, and where.</summary>
        [System.Serializable]
        class Marker {
            public float time;
            public char kind;
            public string label;
            public int cell = -1;
        }

        // ---- lifecycle ------------------------------------------------------------------------

        void OnEnable() {
            wantsMouseMove = true;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayMode;

        bool Ours => SessionState.GetString(GPReplayer.ReplayRecordKey, "") == TempPath;

        void OnPlayMode(PlayModeStateChange c) {
            if (c == PlayModeStateChange.ExitingPlayMode) {
                if (Ours) GPReplayer.Stop();
                Time.timeScale = 1f;
            } else if (c == PlayModeStateChange.EnteredEditMode) {
                if (_pending) {   // a different trace was loaded while playing: restart on it
                    EditorApplication.delayCall += () => {
                        // set again: the GP Recorder window (if open) clears the key on this same callback
                        SessionState.SetString(GPReplayer.ReplayRecordKey, TempPath);
                        if (EnsureGameplayScene()) EditorApplication.isPlaying = true;
                        else _pending = false;
                    };
                    return;
                }
                if (Ours) SessionState.EraseString(GPReplayer.ReplayRecordKey);
            }
        }

        void Update() {
            if (!EditorApplication.isPlaying || !Ours) return;
            // the recorder's session flags are statics (reset by the reload) — a replayed trace must
            // never really win, lose a life or fail: a full board would run the real Win()
            GPRecorder.FailMode = GPRecord.EFailMode.NoCost;
            GPRecorder.NoWin = true;

            if (_pending && MBGameplay.instance != null && MBGameplay.instance.Ready && !GPReplayer.IsReplaying) {
                _pending = false;
                GPReplayer.Play(_record, _playhead);
            }
            Time.timeScale = GPReplayer.IsReplaying ? _speed : 1f;
            if (GPReplayer.IsReplaying) { _playhead = GPReplayer.PlayheadTime; Repaint(); }
        }

        // ---- GUI ------------------------------------------------------------------------------

        void OnGUI() {
            EditorGUILayout.LabelField("Paste a trace row (JSON from the reports server) or a bare packed trace", EditorStyles.miniBoldLabel);
            _input = EditorGUILayout.TextArea(_input, GUILayout.Height(54));
            bool bare = !_input.TrimStart().StartsWith("{");
            if (bare) {
                using (new EditorGUILayout.HorizontalScope()) {
                    _manualLevel = Mathf.Max(1, EditorGUILayout.IntField("Level (1-based)", _manualLevel));
                    _manualDaily = EditorGUILayout.ToggleLeft("Daily (day index)", _manualDaily, GUILayout.Width(130));
                    _manualVersion = EditorGUILayout.IntPopup("tr_v", _manualVersion, new[] { "1", "2", "3" }, new[] { 1, 2, 3 }, GUILayout.Width(200));
                }
            }
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Load", GUILayout.Width(80))) Load();
                if (GUILayout.Button("Clear", GUILayout.Width(80))) { _input = ""; _record = new GPRecord(); _markers.Clear(); _info = _error = ""; _playhead = 0; }
                GUILayout.FlexibleSpace();
            }
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (!_record.IsValid) return;
            EditorGUILayout.HelpBox(_info, MessageType.None);

            // the moves, the current one highlighted; click to jump there
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            int current = _markers.FindLastIndex(m => m.time <= _playhead + 0.001f);
            for (int i = 0; i < _markers.Count; i++) {
                var m = _markers[i];
                var r = EditorGUILayout.GetControlRect(false, 18);
                if (i == current) EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.08f));
                EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 3, 12, 12), ColorOf(m.kind));
                GUI.Label(new Rect(r.x + 20, r.y, 60, r.height), Clock(m.time), EditorStyles.miniLabel);
                if (GUI.Button(new Rect(r.x + 80, r.y, r.width - 80, r.height), m.label, EditorStyles.label)) SeekTo(m.time);
            }
            EditorGUILayout.EndScrollView();

            TransportGUI();
            SeekBarGUI();
        }

        void TransportGUI() {
            using (new EditorGUILayout.HorizontalScope()) {
                bool playing = GPReplayer.IsReplaying && Ours;
                if (GUILayout.Button(playing ? "❚❚ Pause" : "▶ Play", GUILayout.Width(90))) {
                    if (playing) GPReplayer.Stop();
                    else Play();
                }
                if (GUILayout.Button("⏮", GUILayout.Width(30))) SeekTo(0f);
                GUILayout.Space(10);
                GUILayout.Label("Speed", GUILayout.Width(40));
                foreach (float s in new[] { 0.5f, 1f, 2f, 4f })
                    if (GUILayout.Toggle(Mathf.Approximately(_speed, s), s + "×", EditorStyles.miniButton, GUILayout.Width(38)))
                        _speed = s;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{Clock(_playhead)} / {Clock(_record.Duration)}", EditorStyles.boldLabel);
            }
        }

        void SeekBarGUI() {
            var area = GUILayoutUtility.GetRect(10, 52, GUILayout.ExpandWidth(true));
            var bar = new Rect(area.x + 8, area.y + 6, area.width - 16, 8);
            float dur = Mathf.Max(0.01f, _record.Duration);
            float X(float t) => bar.x + bar.width * Mathf.Clamp01(t / dur);
            float T(float x) => Mathf.Clamp01((x - bar.x) / bar.width) * dur;

            EditorGUI.DrawRect(bar, new Color(0.25f, 0.25f, 0.25f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, X(_playhead) - bar.x, bar.height), new Color(0.35f, 0.55f, 0.95f));

            // markers on a row under the bar; the board-only ones (marks, queens) stay off it
            foreach (var m in _markers) {
                if (!OnBar(m.kind)) continue;
                var r = new Rect(X(m.time) - 6, bar.yMax + 6, 12, 14);
                EditorGUI.DrawRect(r, ColorOf(m.kind));
                GUI.Label(r, new GUIContent(m.kind.ToString(), $"{Clock(m.time)}  {m.label}"), CenteredMini);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) {
                    SeekTo(m.time);
                    Event.current.Use();
                }
            }
            // playhead
            EditorGUI.DrawRect(new Rect(X(_playhead) - 1, bar.y - 4, 2, bar.height + 8), Color.white);

            // drag anywhere on the bar to scrub
            var e = Event.current;
            var hit = new Rect(bar.x, bar.y - 6, bar.width, bar.height + 12);
            if (e.type == EventType.MouseDown && hit.Contains(e.mousePosition)) { _scrubbing = true; SeekTo(T(e.mousePosition.x)); e.Use(); }
            else if (e.type == EventType.MouseDrag && _scrubbing) { SeekTo(T(e.mousePosition.x)); e.Use(); }
            else if (e.type == EventType.MouseUp && _scrubbing) { _scrubbing = false; e.Use(); }
            if (e.type == EventType.MouseMove) Repaint();
        }

        static GUIStyle _centered;
        static GUIStyle CenteredMini => _centered ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };

        static bool OnBar(char k) => k == 'W' || k == 'B' || k == 'A' || k == 'P' || k == 'H' || k == 'U' || k == 'C' || k == 'R' || k == '!';

        static Color ColorOf(char k) {
            switch (k) {
                case 'W': return new Color(0.95f, 0.35f, 0.35f);   // wrong queen
                case 'H': return new Color(0.98f, 0.82f, 0.25f);   // hint shown
                case 'B': case 'A': case 'P': return new Color(0.75f, 0.5f, 0.95f);   // boost / applied hint
                case 'U': return new Color(0.4f, 0.7f, 1f);        // undo
                case 'C': return new Color(1f, 0.55f, 0.75f);      // continue after fail
                case 'R': return new Color(0.4f, 0.85f, 0.5f);     // came back
                case '!': return new Color(1f, 0.6f, 0.2f);        // cut
                case 'Q': return new Color(0.85f, 0.85f, 0.85f);
                default: return new Color(0.5f, 0.5f, 0.5f);       // marks
            }
        }

        static string Clock(float t) => $"{(int)(t / 60)}:{(int)(t % 60):00}";

        // ---- play / seek ----------------------------------------------------------------------

        void Play() {
            if (!_record.IsValid) return;
            if (_playhead >= _record.Duration - 0.01f) _playhead = 0f;
            if (GPReplayer.CanDrive(_record) && Ours) { GPReplayer.Play(_record, _playhead); return; }
            // not on this level yet: hand the record over and (re)enter play mode
            HideGameViewStats();
            _record.Save(TempPath);
            SessionState.SetString(GPReplayer.ReplayRecordKey, TempPath);
            _pending = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;   // OnPlayMode restarts
            else if (EnsureGameplayScene()) EditorApplication.isPlaying = true;
            else _pending = false;
        }

        void SeekTo(float t) {
            _playhead = Mathf.Clamp(t, 0f, _record.Duration);
            if (GPReplayer.CanDrive(_record) && Ours) {
                bool wasPlaying = GPReplayer.IsReplaying;
                if (wasPlaying) GPReplayer.Stop();
                GPReplayer.Seek(_record, _playhead);
                if (wasPlaying) GPReplayer.Play(_record, _playhead);
            }
            Repaint();
        }

        // Same as the GP Recorder: the Game view's Stats panel sits over the board — no public API,
        // so its m_Stats flag is flipped by reflection (a no-op if Unity renames it).
        static void HideGameViewStats() {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var field = type?.GetField("m_Stats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(type)) {
                field.SetValue(w, false);
                ((EditorWindow)w).Repaint();
            }
        }

        // Same as the GP Recorder: replays need MBGameplay, so play mode starts on the Gameplay scene.
        static bool EnsureGameplayScene() {
            const string scenePath = "Assets/Scenes/Gameplay.unity";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == scenePath) return true;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) { Debug.LogError($"[TracePlayer] Scene not found: {scenePath}"); return false; }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            return true;
        }

        // ---- load: input → level + decoded moves → GPRecord + markers -------------------------

        void Load() {
            _error = "";
            try {
                string text = _input.Trim();
                string trace; int version, levelHash = 0, lvlIdx; bool daily; string extra = "";
                if (text.StartsWith("{")) {
                    trace = JsonString(text, "trace") ?? throw new System.Exception("no \"trace\" in the row");
                    version = JsonInt(text, "tr_v") ?? 1;
                    levelHash = JsonInt(text, "level_hash") ?? 0;
                    lvlIdx = JsonInt(text, "lvl_idx") ?? ((JsonInt(text, "lvl") ?? 1) - 1);
                    daily = (JsonInt(text, "daily") ?? 0) != 0;
                    int? win = JsonInt(text, "win");
                    string ver = JsonString(text, "version");
                    bool restart = (JsonInt(text, "restart") ?? 0) != 0;
                    bool left = (JsonInt(text, "left") ?? 0) != 0;
                    if (left) extra += " · left mid-level (the attempt so far)";
                    else if (restart) extra += " · restart (the attempt was thrown away)";
                    else if (win.HasValue) extra += win == 1 ? " · win" : " · lose";
                    if (ver != null) extra += " · app " + ver;
                } else {
                    trace = text; version = _manualVersion; lvlIdx = _manualLevel - 1; daily = _manualDaily;
                }

                var level = FindLevel(levelHash, lvlIdx, daily, out string source)
                    ?? throw new System.Exception($"level not found (hash {levelHash}, {(daily ? "day" : "level")} {lvlIdx + 1})");
                var events = TraceCodec.Decode(trace, version, level.size);
                Build(level, events);
                _info = $"{(daily ? "Daily day " + lvlIdx : "Level " + (lvlIdx + 1))} · {level.size}×{level.size} · {source}{extra} · tr_v {version} · "
                      + $"{Clock(_record.Duration)} · {Summary(events)}";
                _playhead = 0f;
                if (GPReplayer.CanDrive(_record) && Ours) GPReplayer.Seek(_record, 0f);
            } catch (System.Exception e) {
                _error = e.Message;
                _record = new GPRecord();
                _markers.Clear();
            }
        }

        static string Summary(List<TraceCodec.Ev> evs) {
            int Count(char k) => evs.Where(e => e.kind == k).Sum(e => e.cells.Length);
            var parts = new List<string>();
            foreach (var (k, name) in new[] { ('Q', "queens"), ('W', "wrong"), ('B', "queen boosts"), ('H', "hints"), ('A', "hint queens"), ('P', "hint marks"), ('U', "undone"), ('C', "continues"), ('R', "came back"), ('X', "X marks"), ('E', "erased") }) {
                int c = Count(k);
                if (c > 0) parts.Add($"{c} {name}");
            }
            if (evs.Any(e => e.kind == '!')) parts.Add("CUT");
            return string.Join(", ", parts);
        }

        // Moves of one second are spread across that second, in order, so they don't land at once.
        void Build(LevelPack.Level level, List<TraceCodec.Ev> evs) {
            int n = level.size;
            var rec = new GPRecord { level = GPRecord.LevelSnapshot.From(level), failMode = GPRecord.EFailMode.NoCost, noWin = true };
            var markers = new List<Marker>();
            var board = new MBCell.ECellType[n * n];
            if (level.revealedRows != null)
                foreach (int row in level.revealedRows) board[row * n + level.solutionColumns[row]] = MBCell.ECellType.QUEEN;

            // how many timed items each second holds (every cell change, and every event without one)
            var perSecond = new Dictionary<int, int>();
            foreach (var e in evs) perSecond[e.sec] = perSecond.GetValueOrDefault(e.sec) + Mathf.Max(1, e.cells.Length);
            var used = new Dictionary<int, int>();
            float NextTime(int sec) {
                int i = used.GetValueOrDefault(sec);
                used[sec] = i + 1;
                return sec + 0.9f * i / perSecond[sec];
            }
            string Cell(int c) => $"row {c / n + 1}, col {c % n + 1}";

            foreach (var e in evs) {
                if (e.kind == '!') { markers.Add(new Marker { time = e.sec, kind = '!', label = "Trace cut here — later marks were dropped for room" }); continue; }
                if (e.cells.Length == 0 || TraceCodec.IsEventOnly(e.kind)) {
                    markers.Add(new Marker { time = NextTime(e.sec), kind = e.kind, label = Describe(e, n), cell = e.cells.Length > 0 ? e.cells[0] : -1 });
                    continue;
                }
                float first = -1f;
                foreach (int c in e.cells) {
                    if (c < 0 || c >= n * n) continue;
                    MBCell.ECellType to;
                    switch (e.kind) {
                        case 'X': to = MBCell.ECellType.X; break;
                        case 'E': to = MBCell.ECellType.EMPTY; break;
                        case 'U': case 'P': to = board[c] == MBCell.ECellType.X ? MBCell.ECellType.EMPTY : MBCell.ECellType.X; break;
                        case 'W': to = MBCell.ECellType.WRONG_QUEEN; break;
                        default: to = MBCell.ECellType.QUEEN; break;   // Q, B, A
                    }
                    board[c] = to;
                    float t = NextTime(e.sec);
                    if (first < 0) first = t;
                    rec.actions.Add(new GPRecordAction { time = t, x = c % n, y = c / n, to = to });
                }
                string label = e.kind switch {
                    'X' => $"X ×{e.cells.Length} from {Cell(e.cells[0])}",
                    'E' => $"erase ×{e.cells.Length} from {Cell(e.cells[0])}",
                    'Q' => $"queen {Cell(e.cells[0])}",
                    _ => Describe(e, n),
                };
                markers.Add(new Marker { time = Mathf.Max(0f, first), kind = e.kind, label = label, cell = e.cells[0] });
            }
            rec.endTime = (evs.Count > 0 ? evs.Max(e => e.sec) : 0) + 1.5f;
            rec.Sort();
            _record = rec;
            _markers = markers.OrderBy(m => m.time).ToList();
        }

        static string Describe(TraceCodec.Ev e, int n) {
            string Cell(int c) => $"row {c / n + 1}, col {c % n + 1}";
            switch (e.kind) {
                case 'W': return $"WRONG queen {Cell(e.cells[0])}";
                case 'B': return $"Queen boost → {Cell(e.cells[0])}";
                case 'A': return $"Hint applied → queen {Cell(e.cells[0])}";
                case 'P': return $"Hint applied → {e.cells.Length} mark(s) from {Cell(e.cells[0])}";
                case 'U': return $"Undo → {e.cells.Length} cell(s) back";
                case 'C': return "Continue after fail (lives refilled)";
                case 'R': return e.a < 0 ? "Came back to the board" : e.a >= LevelTrace.AwayUnknown ? "Came back (time away unknown)" : $"Came back after {Away(e.a)}";
                case 'H': {
                    var tech = (SolveTechnique)e.a;
                    string what = tech == SolveTechnique.Trial ? (e.b == 1 ? "no certain move — a queen shown" : "an X proven by contradiction")
                                : tech == SolveTechnique.None ? "clear a wrong X"
                                : e.b > 0 ? $"{tech} (k={e.b})" : tech.ToString();
                    return $"Hint shown: {what}" + (e.cells.Length > 0 ? $" at {Cell(e.cells[0])}" : "");
                }
                default: return e.kind.ToString();
            }
        }

        static string Away(int min) => min < 60 ? $"{min} min" : min < 48 * 60 ? $"{min / 60} h {min % 60} min" : $"{min / 1440} days";

        // ---- finding the level ----------------------------------------------------------------

        static Dictionary<int, (LevelPack.Level level, string source)> _byHash;

        /// <summary>By content hash in the shipped packs, then in every level asset; without a hash,
        /// the campaign pack's slot.</summary>
        static LevelPack.Level FindLevel(int hash, int lvlIdx, bool daily, out string source) {
            if (_byHash == null) {
                _byHash = new Dictionary<int, (LevelPack.Level, string)>();
                foreach (var path in Directory.GetFiles(Path.Combine(Application.dataPath, "Reskin", "Resources", "Levels"), "*.bytes")) {
                    try {
                        byte[] plain = LevelPack.Decrypt(File.ReadAllBytes(path));
                        string name = Path.GetFileNameWithoutExtension(path);
                        for (int i = 0, c = LevelPack.Count(plain); i < c; i++) {
                            var l = LevelPack.Decode(plain, i);
                            int h = l.ContentHash();
                            if (!_byHash.ContainsKey(h)) _byHash[h] = (l, $"{name} #{i + 1}");
                        }
                    } catch (System.Exception e) { Debug.LogWarning($"[TracePlayer] {path}: {e.Message}"); }
                }
            }
            if (hash != 0 && _byHash.TryGetValue(hash, out var hit)) { source = hit.source; return hit.level; }

            if (hash != 0) {   // an older board (replaced since): search the level assets
                var guids = AssetDatabase.FindAssets("t:LevelData", new[] { "Assets/Levels" });
                try {
                    for (int i = 0; i < guids.Length; i++) {
                        if (i % 200 == 0 && EditorUtility.DisplayCancelableProgressBar("Trace Player", "Looking for the level by hash…", (float)i / guids.Length)) break;
                        string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                        var ld = AssetDatabase.LoadAssetAtPath<LevelData>(p);
                        if (ld != null && ld.regions != null && ld.ContentHash() == hash) { source = p.Replace("Assets/Levels/Sets/", ""); return ld.ToPacked(); }
                    }
                } finally { EditorUtility.ClearProgressBar(); }
            }

            if (!daily && hash == 0) {   // a bare trace: the campaign slot as shipped now
                string pack = Path.Combine(Application.dataPath, "Reskin", "Resources", "Levels", "levels.bytes");
                byte[] plain = LevelPack.Decrypt(File.ReadAllBytes(pack));
                if (lvlIdx >= 0 && lvlIdx < LevelPack.Count(plain)) { source = $"levels #{lvlIdx + 1} (no hash given)"; return LevelPack.Decode(plain, lvlIdx); }
            }
            source = null;
            return null;
        }

        // ---- tiny JSON field readers (a BigQuery row is flat) ---------------------------------

        static string JsonString(string json, string key) {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static int? JsonInt(string json, string key) {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"?(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : (int?)null;
        }
    }

    /// <summary>Reads a packed level trace, every tr_v — the format is documented on LevelTrace.</summary>
    public static class TraceCodec {

        const string B36 = "0123456789abcdefghijklmnopqrstuvwxyz";

        /// <summary>One decoded token: its kind, its cells (empty for C, R, !), its second on the
        /// level's play clock, and its extras (H: a = technique, b = size; R: a = minutes away, -1 if
        /// the format has none).</summary>
        public class Ev {
            public char kind;
            public int[] cells = new int[0];
            public int sec, a = -1, b;
        }

        static bool IsMark(char k) => k == 'X' || k == 'E' || k == 'U' || k == 'P';

        /// <summary>Kinds that are an event, not a board change (their cell is where it pointed).</summary>
        public static bool IsEventOnly(char k) => k == 'H' || k == 'C' || k == 'R';

        public static List<Ev> Decode(string t, int version, int n) {
            var evs = new List<Ev>();
            int p = 0, prev = 0;
            int Rd(int w) {
                int v = 0;
                for (int i = 0; i < w; i++) {
                    if (p >= t.Length) throw new System.Exception($"trace ends mid-token at char {p}");
                    int d = B36.IndexOf(t[p++]);
                    if (d < 0) throw new System.Exception($"bad char '{t[p - 1]}' at {p - 1}");
                    v = v * 36 + d;
                }
                return v;
            }
            while (p < t.Length) {
                char k = t[p++];
                if (k == '!') { evs.Add(new Ev { kind = '!', sec = prev }); continue; }
                if ("QWBACRHXEUP".IndexOf(k) < 0) throw new System.Exception($"unknown kind '{k}' at {p - 1}");
                int cell = Rd(2);
                int sec;
                if (version <= 1) sec = Rd(3);
                else if (p < t.Length && t[p] == 'z') { p++; sec = Rd(3); }
                else sec = prev + Rd(1);
                prev = sec;
                var e = new Ev { kind = k, sec = sec };
                if (k == 'H') { e.a = Rd(1); e.b = Rd(1); e.cells = new[] { cell }; }
                else if (k == 'R') { if (version >= 3) e.a = Rd(3); }
                else if (k == 'C') { }
                else if (IsMark(k)) {
                    int count = Rd(1);
                    var cells = new List<int> { cell };
                    for (int i = 1; i < count; i++) {
                        if (p < t.Length && t[p] == 'z') { p++; cells.Add(Rd(2)); }
                        else { int v = Rd(1); int last = cells[cells.Count - 1]; cells.Add(last + (v / 5 - 2) * n + v % 5 - 2); }
                    }
                    e.cells = cells.ToArray();
                }
                else e.cells = new[] { cell };
                evs.Add(e);
            }
            return evs;
        }
    }
}
