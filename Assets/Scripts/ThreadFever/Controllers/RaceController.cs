using UnityEngine;
using ThreadFever.Config;
using ThreadFever.Events;
using ThreadFever.Models;

namespace ThreadFever.Controllers
{
    public class RaceController : MonoBehaviour
    {
        [SerializeField] private RaceStateManager _stateManager;
        [SerializeField] private AIController _aiController;

        private void Awake()
        {
            // Auto setup if missing
            if (_stateManager == null) _stateManager = GetComponent<RaceStateManager>();
            if (_aiController == null) _aiController = GetComponent<AIController>();
        }

        private void Start()
        {
            if (_aiController != null && _stateManager != null)
            {
                _aiController.Initialize(_stateManager);
            }
        }

        public void ProcessPlayerTurn(bool isSuccess, int additionalSteps = 1)
        {
            if (_stateManager == null || _stateManager.Data == null) return;
            
            if (!_stateManager.Data.IsRaceActive || _stateManager.Data.IsPlayerFinished || _stateManager.Data.IsPlayerEliminated)
                return;

            if (isSuccess)
            {
                _stateManager.Data.PlayerStep += additionalSteps;
            }

            // AIs take their turn
            _aiController.ProcessAITurns();

            _stateManager.SaveData();

            // Fire event for UI update
            RaceEvents.OnTurnProcessed?.Invoke();
            RaceEvents.OnParticipantsUpdated?.Invoke();

            Debug.Log($"<color=white>[RaceController] Turn Processed. Player Step: {_stateManager.Data.PlayerStep}. AI Steps: {_stateManager.Data.AISteps[0]}, {_stateManager.Data.AISteps[1]}, {_stateManager.Data.AISteps[2]}, {_stateManager.Data.AISteps[3]}</color>");

            CheckRaceConditions();
        }

        private void CheckRaceConditions()
        {
            var data = _stateManager.Data;

            // Check if any AI finished
            for (int i = 0; i < 4; i++)
            {
                if (data.AISteps[i] >= RaceConfig.TotalStepsToWin)
                {
                    if (!data.PodiumList.Contains(i))
                    {
                        data.PodiumList.Add(i);
                        // Condition B: AI finishes, added to podium. 
                        // AI stops rolling because AIController checks if AISteps[i] >= TotalStepsToWin
                    }
                }
            }

            // Condition A: Player Finishes (Mutlu Son)
            if (data.PlayerStep >= RaceConfig.TotalStepsToWin)
            {
                data.IsPlayerFinished = true;
                data.IsRaceActive = false;
                
                int rank = data.PodiumList.Count; // e.g. if 2 AIs finished, player is rank 2 (3rd place)
                data.PodiumList.Add(4); // 4 represents Player

                int reward = 0;
                if (rank < RaceConfig.TieredRewards.Length)
                {
                    reward = RaceConfig.TieredRewards[rank];
                }

                _stateManager.SaveData();

                RaceEvents.OnRaceEnded?.Invoke(new RaceResult(rank + 1, reward, true));
                Time.timeScale = 0f; // Yarış bittiğinde oyunu dondur
                return;
            }

            // Condition C: First 3 spots filled by AI (Elendi)
            // Player hasn't finished, but podium is full
            if (data.PodiumList.Count >= 3)
            {
                data.IsPlayerEliminated = true;
                data.IsRaceActive = false;
                
                _stateManager.SaveData();

                RaceEvents.OnRaceEnded?.Invoke(new RaceResult(4, 0, false));
                Time.timeScale = 0f; // Yarış bittiğinde oyunu dondur
                return;
            }

            // Check Comeback State
            CheckComebackState();
        }

        private void CheckComebackState()
        {
            var data = _stateManager.Data;
            int leaderStep = 0;
            
            for (int i = 0; i < 4; i++)
            {
                if (data.AISteps[i] > leaderStep)
                    leaderStep = data.AISteps[i];
            }

            if (leaderStep - data.PlayerStep >= 3)
            {
                RaceEvents.OnComebackStateTriggered?.Invoke();
            }
        }
    }
}
