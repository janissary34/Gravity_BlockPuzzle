using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using ThreadFever.Controllers;

namespace ThreadFever.UI
{
    public class LevelUI : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button _successBtn;
        [SerializeField] private Button _failBtn;

        [Header("Managers")]
        [SerializeField] private RaceController _raceController;
        
        [Header("Settings")]
        [SerializeField] private string _raceSceneName = "Race_Scene";

        private void OnEnable()
        {
            if (_successBtn != null) _successBtn.onClick.AddListener(OnSuccessClicked);
            if (_failBtn != null) _failBtn.onClick.AddListener(OnFailClicked);
        }

        private void OnDisable()
        {
            if (_successBtn != null) _successBtn.onClick.RemoveListener(OnSuccessClicked);
            if (_failBtn != null) _failBtn.onClick.RemoveListener(OnFailClicked);
        }

        private void OnSuccessClicked()
        {
            AnimateButtonAndDo(_successBtn, () => 
            {
                if (_raceController != null)
                {
                    _raceController.ProcessPlayerTurn(isSuccess: true, additionalSteps: 1);
                }
                else
                {
                    Debug.LogWarning("[LevelUI] RaceController atanmamış!");
                }
                
                if (!string.IsNullOrEmpty(_raceSceneName))
                {
                    SceneManager.LoadScene(_raceSceneName);
                }
            });
        }

        private void OnFailClicked()
        {
            AnimateButtonAndDo(_failBtn, () => 
            {
                if (_raceController != null)
                {
                    _raceController.ProcessPlayerTurn(isSuccess: false, additionalSteps: 0);
                }
                else
                {
                    Debug.LogWarning("[LevelUI] RaceController atanmamış!");
                }
                
                if (!string.IsNullOrEmpty(_raceSceneName))
                {
                    SceneManager.LoadScene(_raceSceneName);
                }
            });
        }

        private void AnimateButtonAndDo(Button btn, System.Action onComplete)
        {
            if (btn == null) return;

            btn.interactable = false; // Animasyon sırasında çift tıklanmasını önlemek için kapat
            
            // Tıklanma hissi veren daha zarif DOTween animasyonu
            btn.transform.DOPunchScale(Vector3.one * -0.05f, 0.15f, 1, 1)
                .SetUpdate(true)
                .SetUpdate(true)
                .OnComplete(() => 
                {
                    btn.interactable = true;
                    onComplete?.Invoke();
                });
        }
    }
}
