using UnityEngine;
using ThreadFever.Config;

namespace ThreadFever.Controllers
{
    public class AIController : MonoBehaviour
    {
        private RaceStateManager _stateManager;

        public void Initialize(RaceStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        public void ProcessAITurns()
        {
            if (_stateManager == null || _stateManager.Data == null) return;
            if (!_stateManager.Data.IsRaceActive || _stateManager.Data.IsPlayerFinished || _stateManager.Data.IsPlayerEliminated)
                return;

            int playerStep = _stateManager.Data.PlayerStep;

            for (int i = 0; i < 4; i++)
            {
                // If AI already finished, skip their turn
                if (_stateManager.Data.AISteps[i] >= RaceConfig.TotalStepsToWin)
                    continue;

                AIType aiType = (AIType)i;
                float baseChance = GetBaseChance(aiType);
                float rubberBandModifier = GetRubberBandModifier(aiType, _stateManager.Data.AISteps[i], playerStep);
                float prdModifier = _stateManager.Data.AIFailures[i] * 0.25f;

                float finalChance = baseChance + rubberBandModifier + prdModifier;
                finalChance = Mathf.Clamp01(finalChance);

                float roll = UnityEngine.Random.Range(0f, 1f);

                if (roll <= finalChance)
                {
                    // Success
                    _stateManager.Data.AISteps[i]++;
                    _stateManager.Data.AIFailures[i] = 0; // Reset failures on success
                }
                else
                {
                    // Fail
                    _stateManager.Data.AIFailures[i]++;
                }
            }

            _stateManager.SaveData();
        }

        private float GetBaseChance(AIType aiType)
        {
            switch (aiType)
            {
                case AIType.Fast: return RaceConfig.ChanceFast;
                case AIType.Balanced: return RaceConfig.ChanceBalanced;
                case AIType.Slow: return RaceConfig.ChanceSlow;
                case AIType.VerySlow: return RaceConfig.ChanceVerySlow;
                default: return 0f;
            }
        }

        private float GetRubberBandModifier(AIType aiType, int aiStep, int playerStep)
        {
            // Only apply rubber banding to Fast and Balanced AI
            if (aiType == AIType.Slow || aiType == AIType.VerySlow)
                return 0f;

            if (aiStep - playerStep >= 3)
            {
                // AI is >= 3 steps ahead
                return -0.30f;
            }
            else if (playerStep - aiStep >= 4)
            {
                // AI is >= 4 steps behind
                return 0.20f;
            }

            return 0f;
        }
    }
}
