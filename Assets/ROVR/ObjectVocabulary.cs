// Assets/ROVR/ObjectVocabulary.cs
// Turns the word a user said for an object ("couch", "refrigerator", "chair") into the names the scan
// uses ("sofa", "fridge", "chair" / "armchair" / "stool"). Done in code rather than by the LLM: given
// a long list of visible objects, a small model was seen to swap one kind for another ("tree" became a
// listed plant), and the engine owns grounding anyway (Semantic-Geometric Division of Labor, 6.2.3).
// Plain C#, no Unity types, so it can be unit-tested.
using System;
using System.Collections.Generic;
using System.Text;

namespace ROVR
{
    public static class ObjectVocabulary
    {
        // Other words for a name, and general words that cover several (best match first).
        static readonly Dictionary<string, string[]> Words = new Dictionary<string, string[]>
        {
            { "couch", new[] { "sofa" } }, { "settee", new[] { "sofa" } },
            { "refrigerator", new[] { "fridge" } }, { "refridgerator", new[] { "fridge" } },
            { "television", new[] { "tv" } }, { "telly", new[] { "tv" } },
            { "tv cabinet", new[] { "tv stand" } }, { "television stand", new[] { "tv stand" } },
            { "bookcase", new[] { "bookshelf" } }, { "shelves", new[] { "shelf", "bookshelf" } },
            { "night stand", new[] { "nightstand" } }, { "bedside table", new[] { "nightstand" } },
            { "closet", new[] { "wardrobe" } }, { "chest of drawers", new[] { "dresser" } },
            { "cooker", new[] { "stove" } }, { "oven", new[] { "stove" } }, { "range", new[] { "stove" } },
            { "hood", new[] { "range hood" } }, { "basin", new[] { "sink" } }, { "washbasin", new[] { "sink" } },
            { "island", new[] { "kitchen island" } }, { "lamp post", new[] { "lamp" } },
            { "seat", new[] { "chair", "armchair", "stool", "sofa" } },
            { "chair", new[] { "chair", "armchair", "stool" } },
            { "table", new[] { "dining table", "coffee table", "side table", "console table", "desk" } },
            { "cupboard", new[] { "cabinet", "counter" } },
            { "bush", new[] { "plant" } }, { "shrub", new[] { "plant" } }, { "houseplant", new[] { "plant" } },
            { "potted plant", new[] { "plant" } }, { "plant", new[] { "plant", "cactus" } },
            { "boulder", new[] { "rock" } }, { "stone", new[] { "rock" } },
            { "lake", new[] { "pond" } }, { "water", new[] { "pond" } },
            { "doorway", new[] { "door" } }, { "entrance", new[] { "door" } },
            { "hedge", new[] { "wall" } }, { "way out", new[] { "exit" } }, { "goal", new[] { "exit" } },
        };

        // The scan names a word could mean, best first, out of the names that exist in this world.
        public static List<string> NamesFor(string word, ICollection<string> knownNames)
        {
            var result = new List<string>();
            string w = Normalize(word);
            if (w.Length == 0) return result;

            foreach (string form in SingularForms(w))
            {
                if (Words.TryGetValue(form, out string[] names))
                    foreach (var n in names)
                        if (knownNames.Contains(n) && !result.Contains(n)) result.Add(n);
                if (knownNames.Contains(form) && !result.Contains(form)) result.Insert(0, form);
            }
            return result;
        }

        // Object words in what the user said, in the order said ("coffee table" counts once, not also as "table").
        public static List<string> WordsIn(string utterance, ICollection<string> knownNames)
        {
            var found = new List<KeyValuePair<int, string>>();
            string text = " " + Normalize(utterance) + " ";

            var candidates = new List<string>(knownNames);
            candidates.AddRange(Words.Keys);
            candidates.Sort((a, b) => b.Length.CompareTo(a.Length));

            foreach (var c in candidates)
            {
                foreach (string form in new[] { c, c + "s", c + "es" })
                {
                    int at = text.IndexOf(" " + form + " ", StringComparison.Ordinal);
                    if (at < 0) continue;
                    if (NamesFor(c, knownNames).Count > 0 && !found.Exists(f => f.Value == c)) found.Add(new KeyValuePair<int, string>(at, c));
                    text = text.Remove(at + 1, form.Length).Insert(at + 1, new string('_', form.Length)); // don't reuse "table" of "coffee table"
                    break;
                }
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            return found.ConvertAll(f => f.Value);
        }

        static readonly string[] NearPhrases = { "next to", "beside", "close to", "nearest to", "closest to", "near", "by" };

        // "The tree by the pond", "the one next to the stool": the object after the "by" / "next to".
        public static string NearObjectIn(string utterance, ICollection<string> knownNames)
        {
            string text = " " + Normalize(utterance) + " ";
            foreach (var phrase in NearPhrases)
            {
                int at = text.IndexOf(" " + phrase + " ", StringComparison.Ordinal);
                if (at < 0) continue;
                var after = WordsIn(text.Substring(at + phrase.Length + 1), knownNames);
                if (after.Count > 0) return after[0];
            }
            return null;
        }

        // Whether two words can mean the same kind of object ("couch" and "sofa").
        public static bool SameThing(string a, string b, ICollection<string> knownNames)
        {
            var names = NamesFor(a, knownNames);
            return NamesFor(b, knownNames).Exists(names.Contains);
        }

        static IEnumerable<string> SingularForms(string w)
        {
            yield return w;
            if (w.EndsWith("ies")) yield return w.Substring(0, w.Length - 3) + "y";
            if (w.EndsWith("es")) yield return w.Substring(0, w.Length - 2);
            if (w.EndsWith("s")) yield return w.Substring(0, w.Length - 1);
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder();
            bool space = true;
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-') { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            string t = sb.ToString().Trim();
            foreach (var article in new[] { "the ", "a ", "an ", "that ", "this " })
                if (t.StartsWith(article)) t = t.Substring(article.Length);
            return t;
        }
    }
}
