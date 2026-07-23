using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;
using ThreadFever.Controllers;
using ThreadFever.Events;
using ThreadFever.Models;

namespace ThreadFever.UI
{
    /// <summary>
    /// Yarış bittiğinde tetiklenen kutlama/sonuç popup'ı.
    ///
    /// WIN PANELİ: 1 Image + 2 Text (başlık + sıra yazısı) sırayla animasyonlu açılır.
    /// LOSE PANELİ: 1 Image + 2 Text aynı şekilde.
    /// Oyuncunun sırası PodiumList'ten hesaplanarak Race_over_txt alanına yazılır.
    /// </summary>
    public class RaceEndCelebrationUI : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────
        // Inspector Referansları
        // ─────────────────────────────────────────────────────────────

        [Header("── Overlay (Arka Plan Karartma) ──────────────────────")]
        [Tooltip("Tüm sahneyi kaplayan karartma CanvasGroup'u.")]
        [SerializeField] private CanvasGroup _overlayCanvasGroup;

        // ── WIN PANELİ ────────────────────────────────────────────────

        [Header("── Win Panel ─────────────────────────────────────────")]
        [Tooltip("Kazanma popup'ının kök GameObject'i.")]
        [SerializeField] private GameObject _winPanel;

        [Tooltip("Win panelindeki görsel (örn: kupa/madalya). Scale 0→1 ile açılır.")]
        [SerializeField] private RectTransform _winImage;

        [Tooltip("Win panelindeki başlık metni (örn: 'Tebrikler!'). Image'dan sonra açılır.")]
        [SerializeField] private TextMeshProUGUI _winTitleText;

        [Tooltip("Oyuncunun sırasını gösteren metin (Race_over_txt). En son açılır. " +
                 "İçerik otomatik yazılır: '1. Sıra', '2. Sıra' vb.")]
        [SerializeField] private TextMeshProUGUI _winRankText;

        [Tooltip("Win panelindeki 'Devam Et' butonu.")]
        [SerializeField] private Button _winContinueButton;

        // ── LOSE PANELİ ───────────────────────────────────────────────

        [Header("── Lose Panel ──────────────────────────────────────────")]
        [Tooltip("Kaybetme popup'ının kök GameObject'i.")]
        [SerializeField] private GameObject _losePanel;

        [Tooltip("Lose panelindeki görsel. Scale 0→1 ile açılır.")]
        [SerializeField] private RectTransform _loseImage;

        [Tooltip("Lose panelindeki başlık metni (örn: 'Elendi!'). Image'dan sonra açılır.")]
        [SerializeField] private TextMeshProUGUI _loseTitleText;

        [Tooltip("Oyuncunun sırasını gösteren metin (Race_over_txt). En son açılır. " +
                 "İçerik otomatik yazılır: '3. Sıra', '4. Sıra' vb.")]
        [SerializeField] private TextMeshProUGUI _loseRankText;

        [Tooltip("Lose panelindeki 'Devam Et' butonu.")]
        [SerializeField] private Button _loseContinueButton;

        // ── KUTLAMA ───────────────────────────────────────────────────

        [Header("── Particles (Kutlama) ─────────────────────────────────")]
        [Tooltip("Havai fişek ve konfeti ParticleSystem'ları — kazanıldığında hepsi Play() edilir.")]
        [SerializeField] private ParticleSystem[] _celebrationParticles;

        [Header("── Audio ──────────────────────────────────────────────")]
        [Tooltip("Kazanma jingle'ı AudioSource (Loop=false, PlayOnAwake=false).")]
        [SerializeField] private AudioSource _victoryAudioSource;

        // ── TRANSITION + STATE ────────────────────────────────────────

        [Header("── Transition ───────────────────────────────────────────")]
        [Tooltip("Geri dönülecek sahne adı.")]
        [SerializeField] private string _raceSceneName = "Race_Scene";

        [Header("── Race State (Önemli!) ────────────────────────────────")]
        [Tooltip("Race_Scene'deki RaceStateManager. Yarış sonu durumunu ve sıralamayı buradan okuruz.")]
        [SerializeField] private RaceStateManager _stateManager;

        // ── TIMING ────────────────────────────────────────────────────

        [Header("── Timing ──────────────────────────────────────────────")]
        [Tooltip("Popup açılmadan önceki gecikme (saniye).")]
        [SerializeField] private float _openDelay = 0.5f;

        [Tooltip("Overlay karartma süresi (saniye).")]
        [SerializeField] private float _overlayFadeDuration = 0.4f;

        [Tooltip("Her elemanın (image/text) yaylanarak açılma süresi (saniye).")]
        [SerializeField] private float _popDuration = 0.45f;

        [Tooltip("Elemanlar arası bekleme süresi (saniye).")]
        [SerializeField] private float _stagingDelay = 0.12f;

        // ─────────────────────────────────────────────────────────────
        // Private State
        // ─────────────────────────────────────────────────────────────

        private Sequence _mainSequence;
        private bool     _isShowing = false;
        private Dictionary<Transform, Vector3> _initialScales = new Dictionary<Transform, Vector3>();

        // ─────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            SetInitialState();
        }

        private void Start()
        {
            // ANA FIX: Level_Scene'de yakılan OnRaceEnded event'ini Race_Scene kaçırır.
            // Start()'ta RaceStateManager.Data'yı okuyarak popup'ı kendimiz tetikliyoruz.
            if (_isShowing) return;
            if (_stateManager == null || _stateManager.Data == null) return;

            var data = _stateManager.Data;

            if (data.IsPlayerFinished)
            {
                _isShowing = true;
                PlayWinSequence();
            }
            else if (data.IsPlayerEliminated)
            {
                _isShowing = true;
                PlayLoseSequence();
            }
        }

        private void OnEnable()
        {
            RaceEvents.OnRaceEnded += OnRaceEnded;
            if (_winContinueButton  != null) _winContinueButton.onClick.AddListener(OnContinueClicked);
            if (_loseContinueButton != null) _loseContinueButton.onClick.AddListener(OnContinueClicked);
        }

        private void OnDisable()
        {
            RaceEvents.OnRaceEnded -= OnRaceEnded;
            if (_winContinueButton  != null) _winContinueButton.onClick.RemoveListener(OnContinueClicked);
            if (_loseContinueButton != null) _loseContinueButton.onClick.RemoveListener(OnContinueClicked);
        }

        private void OnDestroy()
        {
            _mainSequence?.Kill();
        }

        // ─────────────────────────────────────────────────────────────
        // Event Callbacks
        // ─────────────────────────────────────────────────────────────

        private void OnRaceEnded(RaceResult result)
        {
            if (_isShowing) return;
            _isShowing = true;

            if (result.IsWin)
                PlayWinSequence();
            else
                PlayLoseSequence();
        }

        private void OnContinueClicked()
        {
            if (_winContinueButton  != null) _winContinueButton.interactable  = false;
            if (_loseContinueButton != null) _loseContinueButton.interactable = false;

            Button clicked = (_winContinueButton != null && _winContinueButton.gameObject.activeSelf)
                ? _winContinueButton : _loseContinueButton;

            if (clicked != null)
                clicked.transform.DOPunchScale(Vector3.one * -0.08f, 0.12f, 1, 0.5f)
                                 .SetUpdate(true)
                                 .OnComplete(TransitionToRaceScene);
            else
                TransitionToRaceScene();
        }

        // ─────────────────────────────────────────────────────────────
        // Win Akışı
        // ─────────────────────────────────────────────────────────────

        private void PlayWinSequence()
        {
            _mainSequence?.Kill();
            _mainSequence = DOTween.Sequence().SetUpdate(true);

            if (_winContinueButton  != null) _winContinueButton.interactable  = false;
            if (_loseContinueButton != null) _loseContinueButton.interactable = false;

            // Sıra metnini doldur
            int rank = GetPlayerRank();
            if (_winRankText != null)
                _winRankText.text = GetFormattedRank(rank);

            // ADIM 0: Bekleme
            _mainSequence.AppendInterval(_openDelay);

            // ADIM 1: Overlay + Win Panel aktif
            _mainSequence.AppendCallback(() =>
            {
                if (_winPanel != null) _winPanel.SetActive(true);

                if (_overlayCanvasGroup != null)
                {
                    _overlayCanvasGroup.gameObject.SetActive(true);
                    _overlayCanvasGroup.alpha = 0f;
                    _overlayCanvasGroup.DOFade(0.75f, _overlayFadeDuration).SetUpdate(true);
                }
            });

            _mainSequence.AppendInterval(_overlayFadeDuration * 0.6f);

            // ADIM 2a: Image açılır
            AppendPopIn(_mainSequence, _winImage);

            // ADIM 2b: Başlık metni açılır
            AppendTextPopIn(_mainSequence, _winTitleText);

            // ADIM 2c: Sıra metni açılır
            AppendTextPopIn(_mainSequence, _winRankText);

            // ADIM 3: Partikül + müzik + buton
            _mainSequence.AppendInterval(0.1f);
            _mainSequence.AppendCallback(() =>
            {
                PlayCelebrationParticles();
                if (_victoryAudioSource != null && !_victoryAudioSource.isPlaying)
                    _victoryAudioSource.Play();
                if (_winContinueButton != null)
                    _winContinueButton.interactable = true;
            });
        }

        // ─────────────────────────────────────────────────────────────
        // Lose Akışı
        // ─────────────────────────────────────────────────────────────

        private void PlayLoseSequence()
        {
            _mainSequence?.Kill();
            _mainSequence = DOTween.Sequence().SetUpdate(true);

            if (_loseContinueButton != null) _loseContinueButton.interactable = false;

            // Sıra metnini doldur
            int rank = GetPlayerRank();
            if (_loseRankText != null)
                _loseRankText.text = GetFormattedRank(rank);

            // ADIM 0: Bekleme
            _mainSequence.AppendInterval(_openDelay);

            // ADIM 1: Overlay + Lose Panel aktif
            _mainSequence.AppendCallback(() =>
            {
                if (_losePanel != null) _losePanel.SetActive(true);

                if (_overlayCanvasGroup != null)
                {
                    _overlayCanvasGroup.gameObject.SetActive(true);
                    _overlayCanvasGroup.alpha = 0f;
                    _overlayCanvasGroup.DOFade(0.7f, _overlayFadeDuration).SetUpdate(true);
                }
            });

            _mainSequence.AppendInterval(_overlayFadeDuration * 0.6f);

            // ADIM 2a: Image açılır
            AppendPopIn(_mainSequence, _loseImage);

            // ADIM 2b: Başlık metni açılır
            AppendTextPopIn(_mainSequence, _loseTitleText);

            // ADIM 2c: Sıra metni açılır
            AppendTextPopIn(_mainSequence, _loseRankText);

            // ADIM 3: Buton
            _mainSequence.AppendInterval(0.1f);
            _mainSequence.AppendCallback(() =>
            {
                if (_loseContinueButton != null)
                    _loseContinueButton.interactable = true;
            });
        }

        // ─────────────────────────────────────────────────────────────
        // Animasyon Yardımcıları
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Sequence'a: delay → SetActive(true) → scale 0→1 (OutBack) ekler.
        /// </summary>
        private void AppendPopIn(Sequence seq, RectTransform target)
        {
            if (target == null) return;

            target.gameObject.SetActive(false);
            target.localScale = Vector3.zero;
            
            Vector3 targetScale = _initialScales.ContainsKey(target) ? _initialScales[target] : Vector3.one;

            seq.AppendInterval(_stagingDelay);
            seq.AppendCallback(() =>
            {
                target.localScale = Vector3.zero;
                target.gameObject.SetActive(true);
            });
            seq.Append(
                target.DOScale(targetScale, _popDuration)
                      .SetEase(Ease.OutBack)
                      .SetUpdate(true)
            );
        }

        /// <summary>
        /// TMP text için: delay → SetActive(true) → scale 0→1 (OutBack).
        /// </summary>
        private void AppendTextPopIn(Sequence seq, TextMeshProUGUI target)
        {
            if (target == null) return;

            target.gameObject.SetActive(false);
            target.transform.localScale = Vector3.zero;

            Vector3 targetScale = _initialScales.ContainsKey(target.transform) ? _initialScales[target.transform] : Vector3.one;

            seq.AppendInterval(_stagingDelay);
            seq.AppendCallback(() =>
            {
                target.transform.localScale = Vector3.zero;
                target.gameObject.SetActive(true);
            });
            seq.Append(
                target.transform.DOScale(targetScale, _popDuration)
                      .SetEase(Ease.OutBack)
                      .SetUpdate(true)
            );
        }

        // ─────────────────────────────────────────────────────────────
        // Rank Hesaplama
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// PodiumList'te oyuncunun (index=4) kaçıncı olduğunu döner.
        /// PodiumList dolmamışsa 1 döner.
        /// </summary>
        private int GetPlayerRank()
        {
            if (_stateManager == null || _stateManager.Data == null) return 1;

            List<int> podium = _stateManager.Data.PodiumList;
            if (podium == null || podium.Count == 0) return 1;

            int playerIndex = 4; // 0-3: AI, 4: Player
            int idx = podium.IndexOf(playerIndex);
            return idx >= 0 ? idx + 1 : podium.Count + 1;
        }

        private string GetFormattedRank(int rank)
        {
            switch (rank)
            {
                case 1: return "1st Place";
                case 2: return "2nd Place";
                case 3: return "3rd Place";
                default: return rank + "th Place";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Private Helpers
        // ─────────────────────────────────────────────────────────────

        private void SetInitialState()
        {
            if (_overlayCanvasGroup != null)
            {
                _overlayCanvasGroup.alpha = 0f;
                _overlayCanvasGroup.gameObject.SetActive(false);
            }

            if (_winPanel  != null) _winPanel.SetActive(false);
            if (_losePanel != null) _losePanel.SetActive(false);

            // Win elemanları
            HideElement(_winImage);
            HideTextElement(_winTitleText);
            HideTextElement(_winRankText);

            // Lose elemanları
            HideElement(_loseImage);
            HideTextElement(_loseTitleText);
            HideTextElement(_loseRankText);

            if (_winContinueButton  != null) _winContinueButton.interactable  = false;
            if (_loseContinueButton != null) _loseContinueButton.interactable = false;
        }

        private void HideElement(RectTransform rt)
        {
            if (rt == null) return;
            if (!_initialScales.ContainsKey(rt)) _initialScales[rt] = rt.localScale;
            
            rt.localScale = Vector3.zero;
            rt.gameObject.SetActive(false);
        }

        private void HideTextElement(TextMeshProUGUI txt)
        {
            if (txt == null) return;
            if (!_initialScales.ContainsKey(txt.transform)) _initialScales[txt.transform] = txt.transform.localScale;
            
            txt.transform.localScale = Vector3.zero;
            txt.gameObject.SetActive(false);
        }

        private void PlayCelebrationParticles()
        {
            if (_celebrationParticles == null) return;
            foreach (var ps in _celebrationParticles)
            {
                if (ps == null) continue;
                
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play();

                // Eğer bu partikül objesinin üzerinde bir AudioSource varsa, onu da çal
                var audio = ps.GetComponent<AudioSource>();
                if (audio != null && !audio.isPlaying)
                {
                    audio.Play();
                }
            }
        }

        private void TransitionToRaceScene()
        {
            // Popup bir daha gösterilmesin diye flagleri temizle
            if (_stateManager != null && _stateManager.Data != null)
            {
                _stateManager.Data.IsPlayerFinished   = false;
                _stateManager.Data.IsPlayerEliminated = false;
                _stateManager.SaveData();
            }

            Time.timeScale = 1f;

            if (ThreadTransitionManager.Instance != null)
                ThreadTransitionManager.Instance.TransitionToScene(_raceSceneName);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(_raceSceneName);
        }
    }
}
