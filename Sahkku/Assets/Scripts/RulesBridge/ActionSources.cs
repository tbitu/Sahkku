using System.Collections.Generic;
using UnityEngine;
using Sahkku.Rules;

/// <summary>Die randomness backed by UnityEngine.Random, matching the original behaviour.</summary>
public class UnityRandomSource : IRandomSource
{
    public int NextDieFaceIndex()
    {
        return Random.Range(0, 4);
    }
}

/// <summary>
/// The current (random) CPU policy. It reproduces the original AI selection exactly: pick a random
/// movable piece, then an index in [0,4) clamped to that piece's options. A future LLM-backed NPC will
/// implement <see cref="IActionSelector"/> against the same ruleset.
/// </summary>
public class RandomActionSelector : IActionSelector
{
    public bool TryChooseAction(GameState state, IReadOnlyList<Move> legalMoves, out Move move)
    {
        move = default(Move);

        var movable = new List<Piece>();
        foreach (Place place in state.places)
        {
            foreach (Piece piece in place.pieces)
            {
                if (piece.IsSelectable()) movable.Add(piece);
            }
        }
        if (movable.Count == 0) return false;

        Piece chosen = movable[Random.Range(0, movable.Count)];
        int moveIndex = Mathf.Clamp(Random.Range(0, 4), 0, chosen.allowedPlaces.Count - 1);
        move = new Move(chosen.id, chosen.allowedPlaces[moveIndex]);
        return true;
    }
}
