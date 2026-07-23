using System;
using ThreadFever.Models;

namespace ThreadFever.Events
{
    public static class RaceEvents
    {
        /// <summary>
        /// Triggered when the player falls >= 3 steps behind the leader.
        /// </summary>
        public static Action OnComebackStateTriggered;

        /// <summary>
        /// Triggered when the race completely ends for the player (Win or Eliminated).
        /// </summary>
        public static Action<RaceResult> OnRaceEnded;

        /// <summary>
        /// Triggered when AIs have finished taking their turns.
        /// Useful for updating the UI.
        /// </summary>
        public static Action OnTurnProcessed;
        
        /// <summary>
        /// Triggered when any state of the participants changes.
        /// </summary>
        public static Action OnParticipantsUpdated;

        /// <summary>
        /// Triggered when the game is first opened and race is continued.
        /// </summary>
        public static Action OnRaceContinued;
    }
}
