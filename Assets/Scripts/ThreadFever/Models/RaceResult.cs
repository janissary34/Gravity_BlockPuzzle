using UnityEngine;

namespace ThreadFever.Models
{
    public struct RaceResult
    {
        public int Rank;
        public int Reward;
        public bool IsWin;

        public RaceResult(int rank, int reward, bool isWin)
        {
            Rank = rank;
            Reward = reward;
            IsWin = isWin;
        }
    }
}
