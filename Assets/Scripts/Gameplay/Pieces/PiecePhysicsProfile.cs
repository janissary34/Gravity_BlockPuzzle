namespace GravityPuzzle.Gameplay.Pieces
{
    /// <summary>
    /// Engine-only profiles for a piece root. PieceModel remains the
    /// authoritative gameplay lifecycle; this enum makes each Unity physics
    /// setup explicit and prevents one stage from inheriting another's state.
    /// </summary>
    public enum PiecePhysicsProfile
    {
        Grid,
        OpenFreefall,
        Shredding,
        ShredderSupport,
        ShredderApproach
    }
}
