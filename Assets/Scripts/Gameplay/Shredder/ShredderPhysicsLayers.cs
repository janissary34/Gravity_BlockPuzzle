using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Owns the collision isolation used only by the temporary shredder
    /// physics island. This keeps dynamic handoff bodies away from normal
    /// grid-owned pieces and non-grid scene colliders.
    /// </summary>
    internal static class ShredderPhysicsLayers
    {
        private const string GuideLayerName = "ShredderPhysicsGuide";
        private const string PieceLayerName = "ShredderPhysicsPiece";
        internal const string BlockerLayerName = "ShredderPhysicsBlocker";

        private static int guideLayer = -1;
        private static int pieceLayer = -1;
        private static int blockerLayer = -1;
        private static bool configured;

        public static int GuideLayer => guideLayer;
        public static int PieceLayer => pieceLayer;
        public static int BlockerLayer => blockerLayer;
        public static bool IsAvailable => configured &&
                                          guideLayer >= 0 &&
                                          pieceLayer >= 0 &&
                                          blockerLayer >= 0;

        public static bool Configure()
        {
            guideLayer = LayerMask.NameToLayer(GuideLayerName);
            pieceLayer = LayerMask.NameToLayer(PieceLayerName);
            blockerLayer = LayerMask.NameToLayer(BlockerLayerName);
            configured = guideLayer >= 0 && pieceLayer >= 0 && blockerLayer >= 0;

            if (!configured)
            {
                Debug.LogError(
                    $"[ShredderPhysicsIsland] Missing required layers '{GuideLayerName}', " +
                    $"'{PieceLayerName}', and/or '{BlockerLayerName}'. " +
                    "The temporary physics island is disabled until all required layers are added in Project Settings.");
                return false;
            }

            // Explicitly isolate this small island. It is intentional that
            // normal Default-layer colliders cannot become accidental supports
            // for a temporary Dynamic piece.
            for (int layer = 0; layer < 32; layer++)
            {
                Physics2D.IgnoreLayerCollision(guideLayer, layer, true);
                Physics2D.IgnoreLayerCollision(pieceLayer, layer, true);
            }

            Physics2D.IgnoreLayerCollision(guideLayer, pieceLayer, false);
            Physics2D.IgnoreLayerCollision(pieceLayer, pieceLayer, false);
            // Grid-authored static obstacles and map boundaries are the only
            // normal-board collision surfaces allowed inside the island. They
            // share the same authoritative blocked cells as the grid; all
            // other Default-layer scene objects remain isolated.
            Physics2D.IgnoreLayerCollision(pieceLayer, blockerLayer, false);
            // This project keeps normally managed pieces on Default. Make the
            // normal board's relationship with the migrated blocker geometry
            // explicit too, rather than relying on the project matrix's
            // current all-enabled default.
            Physics2D.IgnoreLayerCollision(0, blockerLayer, false);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            guideLayer = -1;
            pieceLayer = -1;
            blockerLayer = -1;
            configured = false;
        }
    }
}
