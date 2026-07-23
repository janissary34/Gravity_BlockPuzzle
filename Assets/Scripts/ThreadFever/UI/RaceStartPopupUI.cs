using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using ThreadFever.Controllers;
using ThreadFever.Events;

namespace ThreadFever.UI
{
    /// <summary>
    /// Manages the Race Start Popup with spring-in animation and button press feedback.
    /// Handles popup lifecycle: open, close, and scene transition.
    /// </summary>
    public class RaceStartPopupUI : MonoBehaviour
    {
        [Header("References — Overlay (Arka Plan Kararması)")]
        [Tooltip("Full-screen CanvasGroup used to fade the background overlay.")]
        [SerializeField] private CanvasGroup _popupCanvasGroup;

        [Header("References — Popup Window (Pencere)")]
        [Tooltip("The popup window RectTransform that springs into view.")]
        [SerializeField] private RectTransform _popupWindow;

        [Header("References — Buttons")]
        [Tooltip("The button on the main screen that opens the popup.")]
        [SerializeField] private Button _openPopupButton;

        [Tooltip("The 'Start Race' button inside the popup.")]
        [SerializeField] private Button _startRaceButton;

        [Header("Dependencies")]
        [SerializeField] private RaceStateManager _stateManager;

        [Header("Settings")]
        [SerializeField] private string _raceSceneName = "Race_Scene";

        // DOTween tween referansları — bellek sızıntısı olmaması için Kill() çağrılarında kullanılır.
        private Tween _scaleTween;
        private Tween _fadeTween;
        private Tween _feedbackTween;

        // ─────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            // Oyun başladığında popup gizli olsun
            if (_popupCanvasGroup != null)
            {
                _popupCanvasGroup.alpha = 0f;
                _popupCanvasGroup.interactable = false;
                _popupCanvasGroup.blocksRaycasts = false;
                _popupCanvasGroup.gameObject.SetActive(false);
            }

            if (_popupWindow != null)
                _popupWindow.localScale = Vector3.zero;
        }

        private void OnEnable()
        {
            if (_openPopupButton != null)
                _openPopupButton.onClick.AddListener(OnOpenPopupClicked);

            if (_startRaceButton != null)
                _startRaceButton.onClick.AddListener(OnStartRaceClicked);
        }

        private void OnDisable()
        {
            if (_openPopupButton != null)
                _openPopupButton.onClick.RemoveListener(OnOpenPopupClicked);

            if (_startRaceButton != null)
                _startRaceButton.onClick.RemoveListener(OnStartRaceClicked);
        }

        private void OnDestroy()
        {
            // Sahneden kaldırılırken tüm aktif tween'leri temizle (bellek sızıntısı koruması)
            _scaleTween?.Kill();
            _fadeTween?.Kill();
            _feedbackTween?.Kill();
        }

        // ─────────────────────────────────────────────────────────────
        // Button Callbacks
        // ─────────────────────────────────────────────────────────────

        private void OnOpenPopupClicked()
        {
            // Açma butonuna küçük bir punch feedback ver
            _openPopupButton.transform
                .DOPunchScale(Vector3.one * -0.05f, 0.15f, 1, 0.5f)
                .SetUpdate(true)
                .OnComplete(OpenPopup);
        }

        private void OnStartRaceClicked()
        {
            // Çift tıklamayı engelle
            if (_startRaceButton != null)
                _startRaceButton.interactable = false;

            // ── Button Press Feedback: Küçül → Büyü → Yarışı Başlat ──
            _feedbackTween?.Kill();
            _feedbackTween = _startRaceButton.transform
                .DOScale(0.9f, 0.1f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _feedbackTween = _startRaceButton.transform
                        .DOScale(1f, 0.1f)
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true)
                        .OnComplete(LaunchRace);
                });
        }

        // ─────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Popup'ı yaylanarak açar ve arka planı karartur.
        /// </summary>
        public void OpenPopup()
        {
            if (_popupCanvasGroup == null || _popupWindow == null)
            {
                Debug.LogError("RaceStartPopupUI: CanvasGroup veya PopupWindow referansı eksik!", this);
                return;
            }

            // Eski tween'leri durdur
            _scaleTween?.Kill();
            _fadeTween?.Kill();

            // Popup'ı aktif et ve başlangıç durumunu sıfırla
            _popupCanvasGroup.gameObject.SetActive(true);
            _popupCanvasGroup.interactable = true;
            _popupCanvasGroup.blocksRaycasts = true;
            _popupWindow.localScale = Vector3.zero;

            // Spring-in animasyonu (pencere yaylanarak açılır)
            _scaleTween = _popupWindow
                .DOScale(Vector3.one, 0.4f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);

            // Aynı anda arka plan perdesi kararır
            _fadeTween = _popupCanvasGroup
                .DOFade(1f, 0.35f)
                .SetUpdate(true);
        }

        /// <summary>
        /// Popup'ı pürüzsüzce kapatır ve tamamlanınca deaktif eder.
        /// </summary>
        public void ClosePopup(Action onComplete = null)
        {
            if (_popupCanvasGroup == null || _popupWindow == null) return;

            _scaleTween?.Kill();
            _fadeTween?.Kill();

            // Popup kutusunu küçülterek yok et
            _scaleTween = _popupWindow
                .DOScale(Vector3.zero, 0.3f)
                .SetEase(Ease.InBack)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _popupCanvasGroup.interactable = false;
                    _popupCanvasGroup.blocksRaycasts = false;
                    _popupCanvasGroup.gameObject.SetActive(false);
                    onComplete?.Invoke();
                });

            // Aynı anda arka plan perdesi açılır
            _fadeTween = _popupCanvasGroup
                .DOFade(0f, 0.25f)
                .SetUpdate(true);
        }

        // ─────────────────────────────────────────────────────────────
        // Private Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Popup kapandıktan sonra yarışı başlatır ve sahneye geçiş yapar.
        /// </summary>
        private void LaunchRace()
        {
            // Popup'ı kapat, kapandıktan sonra iplik animasyonunu başlat
            ClosePopup(onComplete: () =>
            {
                // Race state'i başlat
                if (_stateManager != null)
                {
                    _stateManager.StartNewRace();
                    RaceEvents.OnRaceContinued?.Invoke();
                }

                if (!string.IsNullOrEmpty(_raceSceneName))
                {
                    if (ThreadTransitionManager.Instance != null)
                    {
                        // İplik animasyonu oynar, bittikten sonra sahneyi yükler
                        ThreadTransitionManager.Instance.TransitionToScene(_raceSceneName);
                    }
                    else
                    {
                        SceneManager.LoadScene(_raceSceneName);
                    }
                }
            });
        }
    }
}
