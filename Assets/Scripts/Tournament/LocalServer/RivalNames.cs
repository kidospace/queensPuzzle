using System.Collections.Generic;
using System.Text;
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
        /// One rival's name. <paramref name="renamed"/> says whether this one is a player who
        /// typed something or one who left the name the game gave them — the caller decides that,
        /// because how many of each a table has is a fact about the table.
        ///
        /// May collide with a name already drawn — the caller draws again, which it has to do
        /// anyway. Keeping that here would mean this knows about groups, and it does not.
        /// </summary>
        public static string Pick(string country, bool renamed) {
            string name = _pick(country, renamed);

            // The last word, over every route out of _pick — the pools are a file, and a file
            // grows a two-letter entry sooner or later. A rival wearing a name the game would
            // refuse to let the player wear is the kind of thing nobody reports and everybody
            // notices. The generated name is always legal, so it is what is left.
            return _allowed(name) ? name : ProfileManager.GenerateName();
        }

        static string _pick(string country, bool renamed) {
            _load();

            // The name the game handed out, untouched. The only name here nobody typed.
            if (!renamed) return ProfileManager.GenerateName();

            // Everything below is somebody who typed something, and they split three ways.
            var cfg = TournamentConfig.Instance;
            float playful = cfg != null ? cfg.namePlayful : 0.20f;
            float gamer = cfg != null ? cfg.nameGamer : 0.30f;
            float roll = Random.value;

            // Typed something silly: a syllable, twice. Took three seconds over it, but those
            // three seconds are the difference.
            if (roll < playful) {
                var sounds = _named(PlayfulPool);
                if (sounds.Count > 0) return sounds[Random.Range(0, sounds.Count)];
            }

            // Came to play. A handle belongs to no country.
            else if (roll < playful + gamer) {
                string handle = _gamer();
                if (handle != null) return handle;
            }

            // From around here, and never bare.
            var pool = _poolFor(country);
            if (pool.Count == 0) return ProfileManager.GenerateName();
            return _dress(pool[Random.Range(0, pool.Count)]);
        }

        const string PlayfulPool = "playful", GamerPool = "gamer", TagPool = "tag";
        const string DressPool = "dress", HandlePool = "handle", ScribblePool = "scribble";
        const int MaxLength = 14;   // the leaderboard row, same limit a player gets

        /// <summary>A local name, wearing something. Never handed back bare: "Doron" is what a
        /// list of names looks like, and "Doron96" is what somebody typed.</summary>
        static string _dress(string name) => _fit(_apply(_word(DressPool), name), name);

        /// <summary>A handle, for someone who came to play. The word plays the part the name
        /// plays above, so the same patterns and the same tokens work on it.</summary>
        static string _gamer() {
            string word = _word(GamerPool);
            return word.Length == 0 ? null : _fit(_apply(_word(HandlePool), word), word);
        }

        /// <summary>
        /// Fill in a pattern from the file. Everything outside {braces} is literal — which is the
        /// point: xX, The and the underscore are text somebody can change without a build, and a
        /// form nobody thought of is a line, not a case label.
        /// </summary>
        static string _apply(string pattern, string name) {
            if (pattern.Length == 0) return name;

            var built = new StringBuilder(pattern.Length + 8);
            for (int i = 0; i < pattern.Length; i++) {
                int end = pattern[i] == '{' ? pattern.IndexOf('}', i + 1) : -1;
                if (end < 0) { built.Append(pattern[i]); continue; }

                built.Append(_token(pattern.Substring(i + 1, end - i - 1), name));
                i = end;
            }
            return built.ToString();
        }

        /// <summary>An unknown token writes nothing rather than its own name — a typo in the file
        /// should cost a form, not put "{yaer}" on the leaderboard.</summary>
        static string _token(string token, string name) {
            switch (token) {
                case "n":         return name;
                case "lower":     return name.ToLowerInvariant();
                case "upper":     return name.ToUpperInvariant();
                case "tag":       return _word(TagPool);
                case "tag_lower": return _word(TagPool).ToLowerInvariant();
                // Never the word it is standing next to: ShadowShadow is not a name anyone chose.
                case "w":         return _other(GamerPool, name);
                case "pre":       return _scribble(name, atFront: true);
                case "post":      return _scribble(name, atFront: false);
                case "year":      return _year();
                // Two digits, because nobody types 578. The ones who do are already covered by
                // {year}, which is what those digits almost always are.
                case "num":       return Random.Range(1, 100).ToString();
                case "initial":   return ((char)('A' + Random.Range(0, 26))).ToString();
                default:
                    Debug.LogWarning($"[RivalNames] unknown token {{{token}}}");
                    return "";
            }
        }

        /// <summary>
        /// Dressed up, unless the result is not a name the player could have picked themselves —
        /// then plain, which is still better than a rival wearing something the game refuses to
        /// let anyone else wear.
        ///
        /// A pattern in the file can produce anything, and anything is what it eventually will:
        /// a two-letter name left bare comes out under the minimum, a long one wrapped in xX runs
        /// over the row. The rule is <see cref="ProfileManager.Validate"/>'s, kept here rather
        /// than called, because that one also runs a profanity filter over a word list and these
        /// names come from a file we wrote.
        /// </summary>
        static string _fit(string dressed, string plain) => _allowed(dressed) ? dressed : plain;

        static bool _allowed(string name) {
            var cfg = ProfileConfig.Instance;
            int min = cfg != null ? cfg.minNameLength : 4;
            int max = cfg != null ? cfg.maxNameLength : MaxLength;
            if (name.Length < min || name.Length > max) return false;

            // Letters and digits, single spaces between them — the same shape the field accepts.
            bool wasSpace = true;   // a leading space is as wrong as a double one
            foreach (char c in name) {
                bool letterOrDigit = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (letterOrDigit) { wasSpace = false; continue; }
                if (c != ' ' || wasSpace) return false;
                wasSpace = true;
            }
            return !wasSpace;       // and no trailing one
        }

        /// <summary>Digits that mean something to whoever typed them: a birth year far more often
        /// than a random number, because that is what people actually put there.</summary>
        static string _year() {
            int roll = Random.Range(0, 10);
            if (roll < 6) return Random.Range(60, 100).ToString();     // 60-99
            if (roll < 8) return Random.Range(0, 25).ToString("00");   // 00-24
            return Random.Range(1, 100).ToString();
        }

        /// <summary>
        /// A few letters of nothing to put either side of a name.
        ///
        /// It must not touch the name with the same letter the name offers back: "iTz" in front
        /// of "Ziv" gives iTzZiv, and a doubled letter at the seam reads as a typo rather than as
        /// a flourish. Tried a few times and then given up on, because no scribble at all is a
        /// perfectly good outcome and a loop that insists is not.
        /// </summary>
        static string _scribble(string name, bool atFront) {
            var pool = _named(ScribblePool);
            if (pool.Count == 0 || name.Length == 0) return "";

            char touching = char.ToLowerInvariant(atFront ? name[0] : name[name.Length - 1]);
            for (int attempt = 0; attempt < 8; attempt++) {
                string scribble = pool[Random.Range(0, pool.Count)];
                if (scribble.Length == 0) continue;

                char edge = atFront ? scribble[scribble.Length - 1] : scribble[0];
                if (char.ToLowerInvariant(edge) != touching) return scribble;
            }
            return "";
        }

        /// <summary>A word from the pool that is not <paramref name="avoid"/>. Given up on after a
        /// few tries — a repeat is better than a loop, and with fifty words it will not happen.</summary>
        static string _other(string pool, string avoid) {
            for (int attempt = 0; attempt < 8; attempt++) {
                string word = _word(pool);
                if (word != avoid) return word;
            }
            return _word(pool);
        }

        static string _word(string pool) {
            var list = _named(pool);
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : "";
        }

        static List<string> _named(string pool) =>
            _pools != null && _pools.TryGetValue(pool, out var found) ? found : Empty;

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
                    // [culture:x] and [pool:x] are the same thing to the reader — a list of lines
                    // under a name. What differs is who asks for it: a country's recipe, or the
                    // code, by one of the constants above.
                    if (line.StartsWith("[culture:")) pool = _pool(line.Substring(9).TrimEnd(']'));
                    else if (line.StartsWith("[pool:")) pool = _pool(line.Substring(6).TrimEnd(']'));
                    else pool = null;
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
