using System.Collections.Generic;

namespace WuWa
{
    /// One-shot story/content flags ("talked_merchant", "arena_intro"…), saved
    /// with the game.
    public static class GameFlags
    {
        static readonly HashSet<string> _set = new HashSet<string>();

        public static bool Has(string key) { return _set.Contains(key); }
        public static void Set(string key) { _set.Add(key); }
        public static void Clear() { _set.Clear(); }

        public static string[] Export()
        {
            var arr = new string[_set.Count];
            _set.CopyTo(arr);
            return arr;
        }

        public static void Import(string[] keys)
        {
            _set.Clear();
            if (keys == null) return;
            foreach (var k in keys) if (!string.IsNullOrEmpty(k)) _set.Add(k);
        }
    }
}
