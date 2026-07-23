namespace ThreadFever.Config
{
    public static class RaceConfig
    {
        public const int TotalStepsToWin = 10;
        public const int RaceDurationSeconds = 172800; // 48 hours

        // AI Base Chances (Kazanma oranları düşürüldü)
        public const float ChanceFast = 0.35f;
        public const float ChanceBalanced = 0.25f;
        public const float ChanceSlow = 0.15f;
        public const float ChanceVerySlow = 0.05f;

        // Rewards based on rank
        // 0-indexed: 0 = 1st Place, 1 = 2nd Place, 2 = 3rd Place
        public static readonly int[] TieredRewards = new int[]
        {
            500, // 1st Place
            200, // 2nd Place
            50,  // 3rd Place
            0    // 4th+ Place (Eliminated)
        };
    }
    
    public enum AIType
    {
        Fast = 0,
        Balanced = 1,
        Slow = 2,
        VerySlow = 3
    }
}
