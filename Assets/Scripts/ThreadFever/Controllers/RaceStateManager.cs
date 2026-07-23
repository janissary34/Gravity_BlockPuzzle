using UnityEngine;
using System;
using System.Collections.Generic;

namespace ThreadFever.Controllers
{
    [Serializable]
    public class RaceSaveData
    {
        public bool IsRaceActive;
        public long EndTimeBinary;
        public int PlayerStep;
        public int[] AISteps = new int[4]; // Indices: 0=Fast, 1=Balanced, 2=Slow, 3=VerySlow
        public int[] AIFailures = new int[4];
        
        // Represents who is on the podium in order. 
        // 0-3 for AIs, 4 for Player.
        public List<int> PodiumList = new List<int>(); 
        
        public bool IsPlayerEliminated;
        public bool IsPlayerFinished;
    }

    public class RaceStateManager : MonoBehaviour
    {
        private const string SAVE_KEY = "ThreadFever_RaceData";
        
        public RaceSaveData Data { get; private set; }

        private void Awake()
        {
            LoadData();
        }

        public void LoadData()
        {
            if (PlayerPrefs.HasKey(SAVE_KEY))
            {
                string json = PlayerPrefs.GetString(SAVE_KEY);
                Data = JsonUtility.FromJson<RaceSaveData>(json);
            }
            else
            {
                Data = new RaceSaveData();
            }
        }

        public void SaveData()
        {
            string json = JsonUtility.ToJson(Data);
            PlayerPrefs.SetString(SAVE_KEY, json);
            PlayerPrefs.Save();
        }

        public void StartNewRace()
        {
            Data = new RaceSaveData();
            Data.IsRaceActive = true;
            Data.EndTimeBinary = DateTime.UtcNow.AddSeconds(Config.RaceConfig.RaceDurationSeconds).ToBinary();
            SaveData();
            
            Time.timeScale = 1f; // Yeni yarış başladığında zamanı normal akışına döndür
        }

        public TimeSpan GetRemainingTime()
        {
            if (!Data.IsRaceActive) return TimeSpan.Zero;
            
            DateTime endTime = DateTime.FromBinary(Data.EndTimeBinary);
            TimeSpan diff = endTime - DateTime.UtcNow;
            if (diff.TotalSeconds <= 0)
            {
                return TimeSpan.Zero;
            }
            return diff;
        }

        public bool IsRaceTimeEnded()
        {
            if (!Data.IsRaceActive) return true;
            return GetRemainingTime().TotalSeconds <= 0;
        }
    }
}
