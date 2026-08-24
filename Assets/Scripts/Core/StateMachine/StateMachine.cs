using System;
using System.Collections.Generic;

namespace GravityPuzzle.Core.StateMachine
{
    public enum GameState
    {
        Initialize,
        Ready,
        Playing,
        LevelComplete,
        Result
    }

    public static class GameStateTransitionRules
    {
        public static IEnumerable<StateTransition<GameState>> Create()
        {
            yield return new StateTransition<GameState>(GameState.Initialize, GameState.Ready);
            yield return new StateTransition<GameState>(GameState.Ready, GameState.Playing);
            yield return new StateTransition<GameState>(GameState.Ready, GameState.LevelComplete);
            yield return new StateTransition<GameState>(GameState.Ready, GameState.Result);
            yield return new StateTransition<GameState>(GameState.Playing, GameState.LevelComplete);
            yield return new StateTransition<GameState>(GameState.Playing, GameState.Result);
            yield return new StateTransition<GameState>(GameState.LevelComplete, GameState.Result);
        }
    }

    public sealed class StateMachine<TState> where TState : struct, Enum
    {
        private readonly HashSet<StateTransition<TState>> allowedTransitions;

        public TState Current { get; private set; }
        public event Action<TState, TState> Transitioned;
        public event Action<TState, TState> InvalidTransition;

        public StateMachine(TState initialState, IEnumerable<StateTransition<TState>> transitions)
        {
            Current = initialState;
            allowedTransitions = new HashSet<StateTransition<TState>>(transitions);
        }

        public bool TryTransition(TState next)
        {
            StateTransition<TState> transition = new StateTransition<TState>(Current, next);
            if (!allowedTransitions.Contains(transition))
            {
                InvalidTransition?.Invoke(Current, next);
                return false;
            }

            TState previous = Current;
            Current = next;
            Transitioned?.Invoke(previous, next);
            return true;
        }
    }

    public readonly struct StateTransition<TState> : IEquatable<StateTransition<TState>> where TState : struct, Enum
    {
        public readonly TState From;
        public readonly TState To;

        public StateTransition(TState from, TState to)
        {
            From = from;
            To = to;
        }

        public bool Equals(StateTransition<TState> other)
        {
            return EqualityComparer<TState>.Default.Equals(From, other.From) &&
                   EqualityComparer<TState>.Default.Equals(To, other.To);
        }

        public override bool Equals(object other)
        {
            return other is StateTransition<TState> transition && Equals(transition);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(From, To);
        }
    }
}
