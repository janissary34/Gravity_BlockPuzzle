namespace GravityPuzzle.Gameplay.Pieces
{
    public interface IRuntimePieceRootProvider
    {
        RuntimePieceRoot Create(string pieceName);
    }
}
