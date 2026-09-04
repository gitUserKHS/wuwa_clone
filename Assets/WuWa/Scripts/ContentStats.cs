namespace WuWa
{
    /// Saved counters for repeatable content (arena runs, rift closures) and the
    /// results-screen tallies (kills, parries, perfect dodges, S ranks, chests).
    public static class ContentStats
    {
        public static int ArenaClears;
        public static int ArenaBestWave;
        public static int RiftsClosed;
        public static int Kills, Parries, PerfectDodges, RankS, ChestsOpened;
        public static int ArenaTierBest;               // highest trial tier cleared (0 = none)

        public static void Reset()
        {
            ArenaClears = 0;
            ArenaBestWave = 0;
            RiftsClosed = 0;
            Kills = 0; Parries = 0; PerfectDodges = 0; RankS = 0; ChestsOpened = 0;
            ArenaTierBest = 0;
        }
    }
}
