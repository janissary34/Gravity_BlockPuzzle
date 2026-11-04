using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    /// <summary>
    /// The board's TimerStarted contract and Creative Mode's Frozen timer mode. Normal play must keep
    /// counting down; a Frozen creative take must hold the timer from the moment it starts.
    /// </summary>
    public sealed class CreativeTimerFreezePlayModeTests
    {
        private const float TimeLimit = 60f;
        private const int FramesToRun = 10;

        private static readonly Type BoardType = Type.GetType("GravityPuzzle.PrototypeBoard, Assembly-CSharp");
        private static readonly Type GameStateType = Type.GetType("GravityPuzzle.Core.StateMachine.GameState, Assembly-CSharp");
        private static readonly Type ConfigType = Type.GetType("GravityPuzzle.Creative.Config.CreativeTakeConfig, Assembly-CSharp");
        private static readonly Type TimerModeType = Type.GetType("GravityPuzzle.Creative.Config.CreativeTimerMode, Assembly-CSharp");
        private static readonly Type RequestType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeTakeRequest, Assembly-CSharp");
        private static readonly Type SessionType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeSession, Assembly-CSharp");

        private GameObject boardObject;
        private Component board;
        private ScriptableObject config;
        private string outputFolder;

        [SetUp]
        public void SetUp()
        {
            Assert.That(BoardType, Is.Not.Null);
            Assert.That(GameStateType, Is.Not.Null);
            boardObject = new GameObject("Creative Timer Test Board");
            board = boardObject.AddComponent(BoardType);
            Invoke("SetTimeLimit", TimeLimit);
            TransitionTo("Ready");
        }

        [TearDown]
        public void TearDown()
        {
            if (boardObject != null)
                UnityEngine.Object.Destroy(boardObject);
            if (config != null)
                UnityEngine.Object.Destroy(config);
            if (outputFolder != null && Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, true);
        }

        [UnityTest]
        public IEnumerator TimerStarted_IsRaisedOnceAfterTheTimerIsMarkedStarted()
        {
            int raised = 0;
            bool startedWhenRaised = false;
            Action handler = () =>
            {
                raised++;
                startedWhenRaised = TimerStarted;
            };
            BoardType.GetEvent("TimerStarted").AddEventHandler(board, handler);

            Invoke("StartTimer");
            Invoke("StartTimer");
            yield return null;

            Assert.That(raised, Is.EqualTo(1), "StartTimer must raise TimerStarted only when the timer really starts.");
            Assert.That(startedWhenRaised, Is.True, "TimerStarted must be raised after IsTimerStarted is set.");
            Assert.That(State, Is.EqualTo("Playing"));
        }

        [UnityTest]
        public IEnumerator NormalPlay_TimerCountsDown()
        {
            Invoke("StartTimer");
            for (int frame = 0; frame < FramesToRun; frame++)
                yield return null;

            Assert.That(TimeRemaining, Is.LessThan(TimeLimit));
        }

        [UnityTest]
        public IEnumerator FrozenCreativeTake_HoldsTheTimerFromItsFirstFrame_AndReleasesItOnEnd()
        {
            object session = CreateSession(frozen: true);
            SessionType.GetMethod("Begin").Invoke(session, null);

            Invoke("StartTimer");
            Assert.That(IsTimerPaused, Is.True, "The freeze must apply synchronously, inside StartTimer.");
            for (int frame = 0; frame < FramesToRun; frame++)
                yield return null;

            Assert.That(TimeRemaining, Is.EqualTo(TimeLimit), "A Frozen take must not lose any time.");

            SessionType.GetMethod("End").Invoke(session, null);
            Assert.That(IsTimerPaused, Is.False);
            for (int frame = 0; frame < FramesToRun; frame++)
                yield return null;

            Assert.That(TimeRemaining, Is.LessThan(TimeLimit), "Ending the take releases the creative pause.");
        }

        [UnityTest]
        public IEnumerator NormalTimerModeCreativeTake_DoesNotPauseTheTimer()
        {
            object session = CreateSession(frozen: false);
            SessionType.GetMethod("Begin").Invoke(session, null);

            Invoke("StartTimer");
            for (int frame = 0; frame < FramesToRun; frame++)
                yield return null;

            Assert.That(IsTimerPaused, Is.False);
            Assert.That(TimeRemaining, Is.LessThan(TimeLimit));
            SessionType.GetMethod("End").Invoke(session, null);
        }

        private object CreateSession(bool frozen)
        {
            Assert.That(ConfigType, Is.Not.Null);
            Assert.That(RequestType, Is.Not.Null);
            Assert.That(SessionType, Is.Not.Null);

            config = ScriptableObject.CreateInstance(ConfigType);
            ConfigType.GetField("timerMode", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(config, Enum.Parse(TimerModeType, frozen ? "Frozen" : "Normal"));

            outputFolder = Path.Combine(Path.GetTempPath(), "gbp-creative-timer-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputFolder);
            object request = Activator.CreateInstance(
                RequestType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { config, "timer-test", outputFolder }, null);

            ConstructorInfo constructor = SessionType.GetConstructors()[0];
            object[] arguments = new object[constructor.GetParameters().Length];
            arguments[0] = config;
            arguments[1] = request;
            arguments[2] = board;
            arguments[arguments.Length - 1] = board; // coroutine host
            return constructor.Invoke(arguments);
        }

        private void Invoke(string method, params object[] arguments)
        {
            BoardType.GetMethod(method, BindingFlags.Instance | BindingFlags.Public).Invoke(board, arguments);
        }

        private void TransitionTo(string state)
        {
            object next = Enum.Parse(GameStateType, state);
            bool ok = (bool)BoardType.GetMethod("TryTransitionGameState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(board, new[] { next });
            Assert.That(ok, Is.True, $"Board must accept the transition to {state}.");
        }

        private float TimeRemaining => (float)BoardType.GetProperty("TimeRemaining").GetValue(board);
        private bool TimerStarted => (bool)BoardType.GetProperty("IsTimerStarted").GetValue(board);
        private bool IsTimerPaused => (bool)BoardType.GetProperty("IsTimerPaused").GetValue(board);
        private string State => BoardType.GetProperty("GameState").GetValue(board).ToString();
    }
}
