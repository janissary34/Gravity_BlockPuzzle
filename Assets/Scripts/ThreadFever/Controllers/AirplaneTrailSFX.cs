using UnityEngine;
using DG.Tweening;

namespace ThreadFever.Controllers
{
    /// <summary>
    /// Uçak GameObject'lerine eklenir; hareket ederken:
    ///   • TrailRenderer aracılığıyla görsel iz bırakır.
    ///   • AudioSource aracılığıyla döngüsel motor/rüzgar sesi çalar.
    ///
    /// NOT — UI Uçakları (Slider içindeki Image'lar):
    ///   TrailRenderer dünya-uzayında çalışır; Canvas UI elemanlarında
    ///   doğrudan görünmeyebilir. Bunun için iki seçenek:
    ///     A) Uçağın tam konumunda duran, Canvas dışında bir
    ///        world-space boş GameObject'e (Trail Emitter) TrailRenderer ekle
    ///        ve o GameObject'i bu scriptin _trailRenderer'ına ata.
    ///     B) Alternatif olarak _trailParticle alanına Particle System
    ///        (Trails modülü aktif) atayarak saf UI-uyumlu iz elde edebilirsin.
    ///
    /// KURULUM:
    ///   Inspector'dan _engineAudioSource, _trailRenderer veya _trailParticle
    ///   referanslarını ver. En az biri olsa yeterli.
    /// </summary>
    public class AirplaneTrailSFX : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────
        // Inspector Referansları
        // ─────────────────────────────────────────────────────────────

        [Header("── Trail ────────────────────────────────────────────")]
        [Tooltip("Uçağın iz bırakan TrailRenderer'ı (World-Space objede olmalı).")]
        [SerializeField] private TrailRenderer _trailRenderer;

        [Tooltip("Alternatif: Trail modülü aktif ParticleSystem ile görsel iz (UI-uyumlu).")]
        [SerializeField] private ParticleSystem _trailParticle;

        [Header("── Engine Audio ───────────────────────────────────────")]
        [Tooltip("Motor/rüzgar sesi AudioSource (Loop=true, PlayOnAwake=false). Clip: planesound")]
        [SerializeField] private AudioSource _engineAudioSource;

        [Tooltip("Hareket ederken motorun hedef ses seviyesi (0-1).")]
        [Range(0f, 1f)]
        [SerializeField] private float _engineMaxVolume = 0.6f;

        [Tooltip("Ses yükselme/alçalma geçiş süresi (saniye).")]
        [SerializeField] private float _volumeFadeDuration = 0.3f;

        [Header("── Movement Detection ──────────────────────────────────")]
        [Tooltip("Bu eşik-değerden hızlı hareket 'uçuş var' olarak sayılır (Unity birim/saniye).")]
        [SerializeField] private float _movementThreshold = 0.5f;

        // ─────────────────────────────────────────────────────────────
        // Private State
        // ─────────────────────────────────────────────────────────────

        private Vector3 _lastPosition;
        private bool    _wasMoving = false;
        private Tween   _volumeFadeTween;

        // ─────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Start()
        {
            _lastPosition = transform.position;

            // Başlangıçta iz ve ses kapalı
            SetTrailEmitting(false);

            if (_engineAudioSource != null)
            {
                _engineAudioSource.loop   = true;
                _engineAudioSource.volume = 0f;
                _engineAudioSource.Play(); // Ses başlatılıyor ama sessiz — fade ile açılacak
            }
        }

        private void Update()
        {
            DetectMovementAndUpdate();
        }

        private void OnDestroy()
        {
            _volumeFadeTween?.Kill();

            if (_engineAudioSource != null)
                _engineAudioSource.DOKill();
        }

        // ─────────────────────────────────────────────────────────────
        // Movement Detection
        // ─────────────────────────────────────────────────────────────

        private void DetectMovementAndUpdate()
        {
            float speed = (transform.position - _lastPosition).magnitude / Time.deltaTime;
            _lastPosition = transform.position;

            bool isMovingNow = speed > _movementThreshold;

            if (isMovingNow && !_wasMoving)
            {
                // Uçuş başladı
                OnFlightStarted();
                _wasMoving = true;
            }
            else if (!isMovingNow && _wasMoving)
            {
                // Uçuş durdu
                OnFlightStopped();
                _wasMoving = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Flight State Handlers
        // ─────────────────────────────────────────────────────────────

        private void OnFlightStarted()
        {
            // Trail aktif
            SetTrailEmitting(true);

            // Motor sesini yavaşça aç
            if (_engineAudioSource != null)
            {
                _volumeFadeTween?.Kill();
                _volumeFadeTween = _engineAudioSource
                    .DOFade(_engineMaxVolume, _volumeFadeDuration)
                    .SetUpdate(false); // Oyun zamanıyla
            }
        }

        private void OnFlightStopped()
        {
            // Trail kapat
            SetTrailEmitting(false);

            // Motor sesini yavaşça kapat
            if (_engineAudioSource != null)
            {
                _volumeFadeTween?.Kill();
                _volumeFadeTween = _engineAudioSource
                    .DOFade(0f, _volumeFadeDuration)
                    .SetUpdate(false);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private void SetTrailEmitting(bool emit)
        {
            if (_trailRenderer != null)
                _trailRenderer.emitting = emit;

            if (_trailParticle != null)
            {
                if (emit && !_trailParticle.isPlaying)
                    _trailParticle.Play();
                else if (!emit && _trailParticle.isPlaying)
                    _trailParticle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Public API (Gerekirse dışarıdan zorla aç/kapat)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Hareket tespitini devre dışı bırakıp manuel olarak iz ve sesi zorla açar.
        /// Örneğin animasyonlu bir tween uçuşu sırasında kullanılabilir.
        /// </summary>
        public void ForceFlightOn()
        {
            _wasMoving = true;
            SetTrailEmitting(true);

            if (_engineAudioSource != null)
            {
                _volumeFadeTween?.Kill();
                _volumeFadeTween = _engineAudioSource
                    .DOFade(_engineMaxVolume, _volumeFadeDuration)
                    .SetUpdate(false);
            }
        }

        /// <summary>
        /// Zorla kapatma — iz ve sesi durdurur.
        /// </summary>
        public void ForceFlightOff()
        {
            _wasMoving = false;
            SetTrailEmitting(false);

            if (_engineAudioSource != null)
            {
                _volumeFadeTween?.Kill();
                _volumeFadeTween = _engineAudioSource
                    .DOFade(0f, _volumeFadeDuration)
                    .SetUpdate(false);
            }
        }
    }
}
