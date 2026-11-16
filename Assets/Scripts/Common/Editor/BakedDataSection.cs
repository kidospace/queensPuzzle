using System;
using System.Collections.Generic;
using System.Linq;
using Common.BuildCenter;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// Pawdoku only: the blobs the build bakes from the winstats server (WinStats, Rivals), as a
    /// section of Common > Build Center and as the pre-build step (replaces BakedBlobsBuildCheck).
    ///
    /// The section's on/off switch (its tick in the Build Center heading) is the whole setting:
    ///   on  -> before every build: fetch; the server has newer -> save it and commit it, no
    ///          questions; the server not answering STOPS the build (never ships stale data
    ///          without anyone knowing).
    ///   off -> builds use what is baked, nothing fetched (a warning in the Console).
    ///
    /// Runs before the git-clean check (BuildCenterOrder), so a refresh it commits is in the
    /// commit the build records.
    /// </summary>
    public sealed class BakedDataSection : BuildCenterSection
    {
        /// <summary>Every blob the build ships. A new one is a new entry here.</summary>
        static IReadOnlyList<BakedBlob> All => new[]
        {
            WinStatsExporter.Blob,
            RivalsExporter.Blob,
        };

        public override int callbackOrder => BuildCenterOrder.BakedData;

        public override string Title => "Baked server data (WinStats, Rivals) — update before build";

        // ---- window ------------------------------------------------------------------

        class Checked
        {
            public BakedBlob Blob;
            public byte[] Fresh;       // null when the fetch failed
            public string Error;
            public bool Same => Fresh != null && BytesEqual(Blob.Baked(), Fresh);
        }

        List<Checked> _lastCheck;
        DateTime _checkedAt;
        string _lastAction;

        public override SectionStatus Status
        {
            get
            {
                if (_lastCheck == null)
                    return SectionStatus.Of(SectionState.Ok, "Updated from the server before every build");

                var failed = _lastCheck.Where(c => c.Error != null).ToList();
                if (failed.Count > 0)
                    return SectionStatus.Of(SectionState.Blocking, "Server did not answer: " + string.Join(", ", failed.Select(c => c.Blob.Name)));

                var newer = _lastCheck.Where(c => !c.Same).ToList();
                if (newer.Count > 0)
                    return SectionStatus.Of(SectionState.Warning, "Server has newer: " + string.Join(", ", newer.Select(c => c.Blob.Name)) + " — the build will update and commit it");

                return SectionStatus.Of(SectionState.Ok, "Same as the server");
            }
        }

        // No network on focus: the server is checked only on "Check server now" and in a build.
        public override void Refresh() { }

        public override void DrawDetails()
        {
            foreach (var blob in All)
            {
                var baked = blob.Baked();
                EditorGUILayout.LabelField(blob.Name, baked == null ? "nothing baked" : $"{baked.Length / 1024f:0.0} KB");
            }

            if (_lastAction != null)
                EditorGUILayout.LabelField($"{_checkedAt:HH:mm}  ·  {_lastAction}", EditorStyles.centeredGreyMiniLabel);
        }

        // Fetch shows progress bars and reads level assets: main thread, on request only.
        void CheckNow()
        {
            _lastCheck = new List<Checked>();
            foreach (var blob in All)
            {
                var c = new Checked { Blob = blob };
                try { c.Fresh = blob.Fetch(); }
                catch (Exception e) { c.Error = Innermost(e).Message; }
                finally { EditorUtility.ClearProgressBar(); }
                _lastCheck.Add(c);
            }
            _checkedAt = DateTime.Now;
            _lastAction = null;
            BuildCenterWindow.RepaintAll();
        }

        bool UpdateAndCommit(List<Checked> newer)
        {
            try
            {
                _lastAction = Apply(newer.ToDictionary(c => c.Blob, c => c.Fresh));
                return true;
            }
            catch (Exception e)
            {
                _lastAction = "FAILED: " + Innermost(e).Message;
                return false;
            }
            finally
            {
                BuildCenterWindow.RepaintAll();
            }
        }

        // ---- Build button in Build Center: everything here, before the build ----------

        public override bool PrepareBuildFromWindow(out string problem)
        {
            CheckNow();

            var failed = _lastCheck.Where(c => c.Error != null).ToList();
            if (failed.Count > 0)
            {
                problem = "the server did not answer (" + string.Join("; ", failed.Select(c => $"{c.Blob.Name}: {c.Error}")) + ")";
                return false;
            }

            var newer = _lastCheck.Where(c => !c.Same).ToList();
            if (newer.Count > 0 && !UpdateAndCommit(newer))
            {
                problem = _lastAction;
                return false;
            }

            problem = null;
            return true;
        }

        // ---- build -------------------------------------------------------------------

        protected override void RunBeforeBuild(BuildReport report)
        {
            // The window's Build button already fetched, updated and committed moments ago
            if (FromBuildCenter)
            {
                Debug.Log("[BakedData] updated in Build Center just before this build");
                return;
            }

            var blobs = All;

            // CI / -batchmode: try each, log loudly, never block (as before).
            if (Application.isBatchMode)
            {
                foreach (var blob in blobs)
                {
                    try { blob.Save(blob.Fetch()); Debug.Log($"[{blob.Name}] baked fresh blob (batch mode)"); }
                    catch (Exception e) { Debug.LogError($"[{blob.Name}] batch fetch FAILED — building with the existing blob. " + Innermost(e).Message); }
                }
                return;
            }

            var fresh = new Dictionary<BakedBlob, byte[]>();
            var failures = new List<string>();

            foreach (var blob in blobs)
            {
                try { fresh[blob] = blob.Fetch(); }
                catch (Exception e) { failures.Add($"{blob.Name}: {Innermost(e).Message}"); }
                finally { EditorUtility.ClearProgressBar(); }
            }

            if (failures.Count > 0)
                throw new BuildFailedException("[BakedData] Could not fetch from the server:\n\n" + string.Join("\n\n", failures) +
                    "\n\nBuild stopped (it never builds on data it could not check). " +
                    "Fix the server or network, or untick this section in Build Center to build with what is baked.");

            var changed = fresh.Where(kv => !BytesEqual(kv.Key.Baked(), kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (changed.Count == 0)
            {
                Debug.Log("[BakedData] same as the server: " + string.Join(", ", blobs.Select(b => b.Name)));
                return;
            }

            try
            {
                Debug.Log("[BakedData] " + Apply(changed));
            }
            catch (Exception e)
            {
                throw new BuildFailedException("[BakedData] updating the baked data FAILED — build stopped: " + Innermost(e).Message);
            }
        }

        /// <summary>Save the fresh bytes and commit them in one commit. Returns what was done.</summary>
        static string Apply(Dictionary<BakedBlob, byte[]> changed)
        {
            foreach (var kv in changed) kv.Key.Save(kv.Value);
            string sha = BakedBlob.CommitAll(changed.Keys.ToList());
            return $"{string.Join(", ", changed.Keys.Select(b => b.Name))} updated + committed ({sha})";
        }

        static bool BytesEqual(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

        static Exception Innermost(Exception e) { while (e.InnerException != null) e = e.InnerException; return e; }
    }
}
