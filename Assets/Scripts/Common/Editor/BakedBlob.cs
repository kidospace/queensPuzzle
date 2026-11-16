using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// One binary the server packs and the build ships: where it lives, and how to go and get it.
    ///
    /// The game bakes more than one of these now (winstats, rivals), and they are identical in
    /// everything except the fetch — same Resources folder, same compare-with-baked, same "commit
    /// it or the build ships what git hasn't got". So the exporters describe their blob with this
    /// and <see cref="BakedDataSection"/> handles the rest, once, for all of them.
    ///
    /// Fetch is a delegate rather than an interface because the differences are entirely inside
    /// it: a URL, a request shape, and a validator that is the game's own reader.
    /// </summary>
    public sealed class BakedBlob
    {
        /// <summary>What to call it in a dialog — "WinStats", "Rivals".</summary>
        public readonly string Name;

        /// <summary>Where the build reads it from.</summary>
        public readonly string Path;

        readonly Func<byte[]> _fetch;

        public BakedBlob(string name, string path, Func<byte[]> fetch)
        {
            Name = name;
            Path = path;
            _fetch = fetch;
        }

        /// <summary>Download and validate. Throws with something a human can act on.</summary>
        public byte[] Fetch() => _fetch();

        /// <summary>What is baked today, or null if nothing is.</summary>
        public byte[] Baked() => File.Exists(Path) ? File.ReadAllBytes(Path) : null;

        public void Save(byte[] bytes)
        {
            File.WriteAllBytes(Path, bytes);
            AssetDatabase.ImportAsset(Path);
        }

        /// <summary>
        /// Stage and commit just these blobs (and their .meta), in one commit. A build must never
        /// ship bytes the repo lacks; pushing stays manual. Returns the new commit's short hash.
        /// Throws with git's own message on failure.
        /// </summary>
        public static string CommitAll(IReadOnlyCollection<BakedBlob> blobs)
        {
            string root = System.IO.Path.GetFullPath(Application.dataPath + "/..");
            string paths = string.Join(" ", blobs.SelectMany(b => new[] { $"\"{b.Path}\"", $"\"{b.Path}.meta\"" }));
            string names = string.Join(", ", blobs.Select(b => b.Name.ToLowerInvariant()));

            Git(root, $"add -- {paths}");
            Git(root, $"commit -m \"baked data: refresh {names}\" -- {paths}");
            return Git(root, "rev-parse --short HEAD").Trim();
        }

        static string Git(string root, string args)
        {
            var result = Common.BuildCenter.GitStatusCheck.Git(root, args);
            if (result.Error != null) throw new Exception(result.Error);
            return result.Output;
        }
    }
}
