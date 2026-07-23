using UnityEngine;
using TMPro;
using System;
using System.Collections;
using ThreadFever.Controllers;
using ThreadFever.Events;
using ThreadFever.Models;

namespace ThreadFever.UI
{
    public class RaceCountdownUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RaceStateManager _stateManager;
        [SerializeField] private TMP_Text _countdownText;

        private Coroutine _countdownCoroutine;

        private void OnEnable()
        {
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
            }
            // Start the countdown routine when the UI element becomes active
            _countdownCoroutine = StartCoroutine(CountdownRoutine());

            // Listen to race continue/start events to revive the timer if tested in-scene
            RaceEvents.OnRaceContinued += OnRaceContinued;
        }

        private void OnDisable()
        {
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
            }
            
            RaceEvents.OnRaceContinued -= OnRaceContinued;
        }

        private void OnRaceContinued()
        {
            // Restart the countdown when a new race starts (e.g. pressing 'S' in RaceTester)
            if (gameObject.activeInHierarchy)
            {
                if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = StartCoroutine(CountdownRoutine());
            }
        }

        private IEnumerator CountdownRoutine()
        {
            // Cache the wait instruction to avoid garbage generation every second
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(1f);

            while (true)
            {
                // Wait if state manager is not assigned yet
                if (_stateManager == null)
                {
                    yield return null;
                    continue;
                }

                // Unity Execution Order Fix: Wait if RaceStateManager hasn't finished Awake() and LoadData() yet
                if (_stateManager.Data == null)
                {
                    yield return null;
                    continue;
                }

                // If data is loaded but race is officially inactive, then the event has ended.
                if (!_stateManager.Data.IsRaceActive)
                {
                    _countdownText.text = "Event Ended";
                    yield break;
                }

                // 1. Calculate remaining time
                DateTime targetDateTime = DateTime.FromBinary(_stateManager.Data.EndTimeBinary);
                TimeSpan remainingTime = targetDateTime - DateTime.UtcNow;

                // 2. Check if event has ended
                if (remainingTime.TotalSeconds <= 0)
                {
                    _countdownText.text = "Event Ended";

                    // Emniyet Kontrolü: Süre bittiğinde yarışı resmi olarak sonlandır ve event'i tetikle
                    _stateManager.Data.IsRaceActive = false;
                    _stateManager.SaveData();
                    
                    // Fallback to a 4th place (eliminated) finish since time ran out
                    RaceEvents.OnRaceEnded?.Invoke(new RaceResult(4, 0, false));
                    
                    yield break; // Stop coroutine
                }

                // 3. Smart Formatting
                if (remainingTime.TotalHours >= 24)
                {
                    // More than 24 hours: show Days, Hours, and Minutes
                    _countdownText.text = string.Format("{0}d {1:D2}h {2:D2}m", remainingTime.Days, remainingTime.Hours, remainingTime.Minutes);
                }
                else
                {
                    // Less than 24 hours: show Hours, Minutes, and Seconds to increase excitement
                    _countdownText.text = string.Format("{0:D2}h {1:D2}m {2:D2}s", remainingTime.Hours, remainingTime.Minutes, remainingTime.Seconds);
                }

                // Wait for exactly 1 real-time second before calculating and updating again
                yield return wait;
            }
        }
    }
}
