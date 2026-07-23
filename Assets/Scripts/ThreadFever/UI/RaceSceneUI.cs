using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;

namespace ThreadFever.UI
{
    public class RaceSceneUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Button _playLevelBtn;
        [SerializeField] private Controllers.RaceStateManager _stateManager;
        
        [Header("Settings")]
        [SerializeField] private string _levelSceneName = "Level_Scene";

        private void OnEnable()
        {
            if (_playLevelBtn != null)
                _playLevelBtn.onClick.AddListener(OnPlayLevelClicked);
        }

        private void OnDisable()
        {
            if (_playLevelBtn != null)
                _playLevelBtn.onClick.RemoveListener(OnPlayLevelClicked);
        }

        private void OnPlayLevelClicked()
        {
            if (_playLevelBtn == null) return;
            
            // YARIŞ BİTTİYSE TIKLAMAYI ENGELLE!
            if (_stateManager != null && _stateManager.Data != null && !_stateManager.Data.IsRaceActive)
            {
                Debug.Log("Yarış Bitti! Level'a geçilemez.");
                return;
            }

            _playLevelBtn.interactable = false;

            // Tıklanma animasyonu ve ardından sahne geçişi (daha zarif)
            _playLevelBtn.transform.DOPunchScale(Vector3.one * -0.05f, 0.15f, 1, 1)
                .SetUpdate(true)
                .OnComplete(() => 
                {
                    _playLevelBtn.interactable = true;
                    
                    if (!string.IsNullOrEmpty(_levelSceneName))
                    {
                        SceneManager.LoadScene(_levelSceneName);
                    }
                });
        }
    }
}
