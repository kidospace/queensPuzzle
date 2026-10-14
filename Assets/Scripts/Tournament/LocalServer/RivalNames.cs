using System.Collections.Generic;
using Common;
using UnityEngine;

namespace qp {

    /// <summary>
    /// What the tournament's rivals are called.
    ///
    /// Half the job is a text file — Resources/RivalNames.txt, a pool per name culture and a line
    /// per country saying which pools it draws from and in what proportion. Two hundred countries
    /// share about thirty cultures, so a list per country would be the same names copied over and
    /// over with every copy to keep. Adding a country there is one line, and a country nobody
    /// mapped falls to [global] rather than breaking.
    ///
    /// The other half is that not every player renames themselves. A table where everyone has a
    /// real name is a table where everyone went into the settings, which is not what happens — so
    /// some rivals keep the adjective-and-noun name the game hands out, exactly as a real player
    /// who never touched it would. How many is <see cref="TournamentConfig"/>'s business, and it
    /// rises with the player's level: deeper in the campaign they are among people who have been
    /// here longer, and the longer you have been here the likelier you have named yourself.
    ///
    /// Parsed on the first name asked for and dropped again by <see cref="Unload"/> when the group
    /// is built — about 70 KB of small strings that nothing wants for another 48 hours. The same
    /// deal RivalsBlob gets, and for the same reason.
    /// </summary>
    public static class RivalNames {

        const string Resource = "RivalNames";

        /// <summary>A country's recipe: which pools, and how much of each. Weights are whatever
        /// the file says — they are summed, never assumed to reach any particular total.</summary>
        sealed class Recipe {
            public readonly List<string> cultures = new List<string>();
            public readonly List<int> weights = new List<int>();
            public int total;
        }

        static Dictionary<string, List<string>> _pools;     // culture -> names
        static Dictionary<string, Recipe> _recipes;         // country -> recipe
        static readonly List<string> Empty = new List<string>();

        /// <summary>
        /// One rival's name, for a player in <paramref name="country"/> with
        /// <paramref name="realShare"/> of the table expected to have named themselves.
        ///
        /// May collide with a name already drawn — the caller draws again, which it has to do
        /// anyway. Keeping that here would mean this knows about groups, and it does not.
        /// </summary>
        public static string Pick(string country, float realShare) {
            _load();
            if (Random.value >= realShare) return ProfileManager.GenerateName();

            var pool = _poolFor(country);
            if (pool.Count == 0) return ProfileManager.GenerateName();

            return _shape(pool[Random.Range(0, pool.Count)]);
        }

        /// <summary>A plain first name most of the time. The initial and the digits are what a
        /// player adds when the name they wanted was taken, so they are rare — a table full of
        /// Daniel22 is the one thing that reads as generated.</summary>
        static string _shape(string name) {
            var cfg = TournamentConfig.Instance;
            float initial = cfg != null ? cfg.nameWithInitial : 0.16f;
            float digits = cfg != null ? cfg.nameWithDigits : 0.14f;

            float roll = Random.value;
            if (roll < initial) return name + " " + (char)('A' + Random.Range(0, 26));
            if (roll < initial + digits) return name + Random.Range(2, 100);
            return name;
        }

        /// <summary>The names a country actually draws from, picked culture-first so the
        /// proportions in the file are the proportions on the table — drawing from all of them
        /// pooled together would let a big culture's long list outvote the recipe.</summary>
        static List<string> _poolFor(string country) {
            var recipe = _recipe(country);
            if (recipe == null || recipe.total == 0) return Empty;

            int roll = Random.Range(0, recipe.total);
            for (int i = 0; i < recipe.weights.Count; i++) {
                roll -= recipe.weights[i];
                if (roll < 0) return _pools.TryGetValue(recipe.cultures[i], out var pool) ? pool : Empty;
            }
            return Empty;
        }

        static Recipe _recipe(string country) {
            if (!string.IsNullOrEmpty(country) && _recipes.TryGetValue(country, out var found)) return found;
            _recipes.TryGetValue("", out var fallback);   // the [global] pool on its own
            return fallback;
        }

        // ---- the file ----------------------------------------------------------------------

        /// <summary>Read the file, if it is not already read. Public for the same reason
        /// <see cref="RivalsBlob.Load"/> is: a caller that will release it should be able to take
        /// it in the open. <see cref="Pick"/> calls it anyway, so nobody has to.</summary>
        public static void Load() => _load();

        /// <summary>Let the names go. Called once a group is named — the next one is two days
        /// away, and re-reading 13 KB then is cheaper than carrying it the whole time.</summary>
        public static void Unload() {
            _pools = null;
            _recipes = null;
        }

        static void _load() {
            if (_pools != null) return;
            _pools = new Dictionary<string, List<string>>();
            _recipes = new Dictionary<string, Recipe>();

            var asset = Resources.Load<TextAsset>(Resource);
            if (asset == null) {
                CDebug.LogError($"[RivalNames] Resources/{Resource}.txt not found — rivals keep the generated names.");
                return;
            }

            // "" is the recipe for a country nobody mapped: all global, which is what that pool
            // is for. Built here rather than special-cased at every read.
            var fallback = new Recipe();
            fallback.cultures.Add("global");
            fallback.weights.Add(1);
            fallback.total = 1;
            _recipes[""] = fallback;

            List<string> pool = null;   // null while reading [map]
            foreach (var raw in asset.text.Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[') {
                    pool = line.StartsWith("[culture:") ? _pool(line.Substring(9).TrimEnd(']')) : null;
                    continue;
                }

                if (pool != null) pool.Add(line);
                else _mapLine(line);
            }

            // Every name above is our own string by now, so the asset itself is finished with.
            Resources.UnloadAsset(asset);
        }

        static List<string> _pool(string culture) {
            if (!_pools.TryGetValue(culture, out var pool)) _pools[culture] = pool = new List<string>();
            return pool;
        }

        /// <summary>One line of [map]: <c>IL = hebrew*7 arabic*2 russian*1</c>. A malformed line is
        /// skipped with a word about it rather than quietly costing a country its names.</summary>
        static void _mapLine(string line) {
            int eq = line.IndexOf('=');
            if (eq <= 0) return;

            string country = line.Substring(0, eq).Trim().ToUpperInvariant();
            var recipe = new Recipe();

            foreach (var part in line.Substring(eq + 1).Split(' ')) {
                string term = part.Trim();
                if (term.Length == 0) continue;

                int star = term.IndexOf('*');
                string culture = star > 0 ? term.Substring(0, star) : term;
                int weight = 1;
                if (star > 0 && !int.TryParse(term.Substring(star + 1), out weight)) {
                    Debug.LogWarning($"[RivalNames] {country}: cannot read '{term}'");
                    continue;
                }
                if (weight <= 0) continue;

                recipe.cultures.Add(culture);
                recipe.weights.Add(weight);
                recipe.total += weight;
            }

            if (recipe.total > 0) _recipes[country] = recipe;
        }
    }
}
