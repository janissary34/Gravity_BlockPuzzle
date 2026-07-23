using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using ThreadFever.Events;
using ThreadFever.Controllers;

namespace ThreadFever.UI
{
    public class RaceHUDUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RaceStateManager _stateManager;
        [SerializeField] private Slider _playerSlider;
        [SerializeField] private Slider[] _aiSliders;

        private void Start()
        {
            UpdateRacerPositions(animated: false);
        }

        private void OnEnable()
        {
            RaceEvents.OnTurnProcessed += OnTurnProcessed;
            RaceEvents.OnRaceContinued += OnRaceContinued;
        }

        private void OnDisable()
        {
            RaceEvents.OnTurnProcessed -= OnTurnProcessed;
            RaceEvents.OnRaceContinued -= OnRaceContinued;
        }

        private void OnTurnProcessed()
        {
            UpdateRacerPositions(animated: true);
        }

        private void OnRaceContinued()
        {
            UpdateRacerPositions(animated: false);
        }

        private void UpdateRacerPositions(bool animated)
        {
            if (_stateManager == null || _stateManager.Data == null) return;

            // Player Update
            int playerCurrentStep = _stateManager.Data.PlayerStep;
            Debug.Log($"<color=orange>[RaceHUDUI] Moving Player Slider to {playerCurrentStep}. Animated: {animated}</color>");
            
            if (animated)
            {
                _playerSlider.DOKill();
                _playerSlider.DOValue((float)playerCurrentStep, 0.5f).SetEase(Ease.OutBack).SetUpdate(true);
            }
            else
            {
                _playerSlider.DOKill();
                _playerSlider.value = playerCurrentStep;
            }

            // AI Update
            if (_aiSliders != null && _stateManager.Data.AISteps != null)
            {
                for (int i = 0; i < _aiSliders.Length; i++)
                {
                    if (i < _stateManager.Data.AISteps.Length)
                    {
                        int aiCurrentStep = _stateManager.Data.AISteps[i];
                        
                        if (animated)
                        {
                            _aiSliders[i].DOKill();
                            _aiSliders[i].DOValue((float)aiCurrentStep, 0.5f).SetEase(Ease.OutBack).SetUpdate(true);
                        }
                        else
                        {
                            _aiSliders[i].DOKill();
                            _aiSliders[i].value = aiCurrentStep;
                        }
                    }
                }
            }
        }
    }
}
