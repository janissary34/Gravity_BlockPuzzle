using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle
{
    public static class PieceFuser
    {
        public static List<PieceDefinition> Fuse(List<PieceDefinition> originalPieces)
        {
            List<PieceDefinition> workingPieces = new List<PieceDefinition>();
            foreach (PieceDefinition pieceDef in originalPieces)
            {
                PieceDefinition copy = new PieceDefinition();
                copy.name = pieceDef.name;
                copy.color = pieceDef.color;
                copy.origin = pieceDef.origin;
                copy.quarterTurns = pieceDef.quarterTurns;
                foreach (var cell in pieceDef.cells)
                {
                    copy.cells.Add(new PieceCellDefinition(cell.localCell, cell.type));
                }
                workingPieces.Add(copy);
            }

            bool fusedAny = true;
            while (fusedAny)
            {
                fusedAny = false;
                for (int i = 0; i < workingPieces.Count; i++)
                {
                    for (int j = i + 1; j < workingPieces.Count; j++)
                    {
                        if (ArePiecesAdjacentAndSameColor(workingPieces[i], workingPieces[j]))
                        {
                            workingPieces[i] = FusePieces(workingPieces[i], workingPieces[j]);
                            workingPieces.RemoveAt(j);
                            fusedAny = true;
                            break;
                        }
                    }
                    if (fusedAny) break;
                }
            }

            return workingPieces;
        }

        private static bool ArePiecesAdjacentAndSameColor(PieceDefinition a, PieceDefinition b)
        {
            if (a.color != b.color) return false;

            List<Vector2Int> absoluteA = GetAbsoluteCells(a);
            List<Vector2Int> absoluteB = GetAbsoluteCells(b);

            foreach (Vector2Int cellA in absoluteA)
            {
                foreach (Vector2Int cellB in absoluteB)
                {
                    if (IsAdjacent(cellA, cellB))
                        return true;
                }
            }

            return false;
        }

        private static bool IsAdjacent(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        }

        private static PieceDefinition FusePieces(PieceDefinition a, PieceDefinition b)
        {
            PieceDefinition fused = new PieceDefinition();
            fused.name = a.name + " (Fused)";
            fused.color = a.color;
            fused.origin = a.origin; 
            fused.quarterTurns = 0; // We resolve rotation to 0 for simplicity

            List<PieceCellDefinition> combined = new List<PieceCellDefinition>();

            // Add A's cells
            foreach (PieceCellDefinition cell in a.cells)
            {
                Vector2Int absolute = GetAbsoluteCell(a, cell.localCell);
                Vector2Int local = absolute - fused.origin;
                combined.Add(new PieceCellDefinition(local, cell.type));
            }

            // Add B's cells
            foreach (PieceCellDefinition cell in b.cells)
            {
                Vector2Int absolute = GetAbsoluteCell(b, cell.localCell);
                Vector2Int local = absolute - fused.origin;
                combined.Add(new PieceCellDefinition(local, cell.type));
            }

            fused.cells = combined;
            return fused;
        }

        private static List<Vector2Int> GetAbsoluteCells(PieceDefinition piece)
        {
            List<Vector2Int> cells = new List<Vector2Int>();
            foreach (var cell in piece.cells)
            {
                cells.Add(GetAbsoluteCell(piece, cell.localCell));
            }
            return cells;
        }

        private static Vector2Int GetAbsoluteCell(PieceDefinition piece, Vector2Int localCell)
        {
            Vector2Int rotated = QuarterTurnUtility.Rotate(localCell, piece.quarterTurns);
            return piece.origin + rotated;
        }
    }
}
