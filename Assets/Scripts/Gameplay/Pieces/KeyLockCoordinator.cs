using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    /// <summary>
    /// Owns the per-level Key/Lock pairing state. When a Key piece is
    /// destroyed, its Key flies to the Lock that references it and the Lock
    /// opens when the Key arrives. Each Lock piece owns its own locked flag;
    /// this coordinator only records which Keys are consumed and decides
    /// when a Lock may open. <see cref="PrototypeBoard"/> owns the instance.
    /// </summary>
    public sealed class KeyLockCoordinator
    {
        private readonly HashSet<string> consumedKeyIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Action wakeGravity;
        private readonly Action<PuzzlePiece> keyArrivedHandler;

        public KeyLockCoordinator(Action wakeGravity)
        {
            this.wakeGravity = wakeGravity;
            keyArrivedHandler = HandleKeyArrived;
        }

        public void Reset()
        {
            consumedKeyIds.Clear();
        }

        public bool IsKeyConsumed(string keyId)
        {
            return !string.IsNullOrEmpty(keyId) && consumedKeyIds.Contains(keyId);
        }

        /// <summary>
        /// Consumes a destroyed Key piece's Key and sends it to every active
        /// Lock linked to it. Locks not yet on the board (inside a Box) open
        /// when they spawn, through <see cref="IsKeyConsumed"/>.
        /// </summary>
        public void NotifyPieceDestroyed(PuzzlePiece destroyedPiece, IReadOnlyList<PuzzlePiece> activePieces)
        {
            if (destroyedPiece == null || !destroyedPiece.IsKey ||
                !consumedKeyIds.Add(destroyedPiece.KeyLinkId))
                return;

            Vector3 flightStart = destroyedPiece.DetachKeyBadgeForFlight();
            bool anyOpenedImmediately = false;
            for (int index = 0; index < activePieces.Count; index++)
            {
                PuzzlePiece lockPiece = activePieces[index];
                if (lockPiece == null || lockPiece == destroyedPiece || !lockPiece.IsKeyLocked ||
                    !string.Equals(lockPiece.KeyLinkId, destroyedPiece.KeyLinkId, StringComparison.Ordinal))
                    continue;

                // Without a presentable flight the Lock must still open.
                if (!lockPiece.TryBeginKeyArrival(flightStart, keyArrivedHandler))
                    anyOpenedImmediately |= lockPiece.TryReleaseKeyLock();
            }

            if (anyOpenedImmediately)
                wakeGravity?.Invoke();
        }

        /// <summary>
        /// The Rocket is carrying a still-locked Lock away. Its Key is
        /// consumed, leaves its Key piece (which becomes a normal piece), and
        /// jumps into the Lock to leave the board with it.
        /// </summary>
        public void NotifyLockCarriedAway(PuzzlePiece lockPiece, IReadOnlyList<PuzzlePiece> activePieces)
        {
            if (lockPiece == null || !lockPiece.IsKeyLocked ||
                !consumedKeyIds.Add(lockPiece.KeyLinkId))
                return;

            for (int index = 0; index < activePieces.Count; index++)
            {
                PuzzlePiece keyPiece = activePieces[index];
                if (keyPiece == null || keyPiece == lockPiece || !keyPiece.IsKey ||
                    !string.Equals(keyPiece.KeyLinkId, lockPiece.KeyLinkId, StringComparison.Ordinal))
                    continue;

                lockPiece.TryCatchKeyWhileCarried(keyPiece.DetachKeyBadgeForFlight());
                return;
            }
        }

        private void HandleKeyArrived(PuzzlePiece lockPiece)
        {
            if (lockPiece != null && lockPiece.TryReleaseKeyLock())
                wakeGravity?.Invoke();
        }
    }
}
