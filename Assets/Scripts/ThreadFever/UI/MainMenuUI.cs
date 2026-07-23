using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using ThreadFever.Controllers;
using ThreadFever.Events;

namespace ThreadFever.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Button _startRaceBtn;
        [SerializeField] private RaceStateManager _stateManager;
        
        [Header("Settings")]
        [SerializeField] private string _raceSceneName = "Race_Scene";

        private void OnEnable()
        {
            if (_startRaceBtn != null)
            {
                var audioBtn = _startRaceBtn.GetComponent<UIButtonAudio>();
                if (audioBtn != null)
                    audioBtn.OnDelayedClick.AddListener(OnStartRaceClicked);
                else
                    _startRaceBtn.onClick.AddListener(OnStartRaceClicked);
            }
        }

        private void OnDisable()
        {
            if (_startRaceBtn != null)
            {
                var audioBtn = _startRaceBtn.GetComponent<UIButtonAudio>();
                if (audioBtn != null)
                    audioBtn.OnDelayedClick.RemoveListener(OnStartRaceClicked);
                else
                    _startRaceBtn.onClick.RemoveListener(OnStartRaceClicked);
            }
        }

        private void OnStartRaceClicked()
        {
            if (_startRaceBtn != null) _startRaceBtn.interactable = false;

            // DOTween ile daha pürüzsüz "tıklanma" animasyonu (Punch Scale)
            _startRaceBtn.transform.DOPunchScale(Vector3.one * -0.05f, 0.15f, 1, 1)
                .SetUpdate(true)
                .SetUpdate(true)
                .OnComplete(() => 
                {
                    if (_startRaceBtn != null) _startRaceBtn.interactable = true;

                    // Yarışı başlat
                    if (_stateManager != null)
                    {
                        _stateManager.StartNewRace();
                        RaceEvents.OnRaceContinued?.Invoke();
                    }

                    // Sahne Geçişi
                    if (!string.IsNullOrEmpty(_raceSceneName))
                    {
                        SceneManager.LoadScene(_raceSceneName);
                    }
                });
        }
    }
}
