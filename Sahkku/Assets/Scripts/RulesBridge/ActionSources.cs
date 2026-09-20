using UnityEngine;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

/// <summary>Die randomness backed by UnityEngine.Random, matching the original behaviour.</summary>
public class UnityRandomSource : IRandomSource
{
    public int NextDieFaceIndex()
    {
        return Random.Range(0, 4);
    }
}

/// <summary>
/// Draw range used by <see cref="RandomPlayerAgent"/>; the original CPU drew its piece and its move
/// index from <c>UnityEngine.Random</c> too, so this keeps that behaviour without leaking the engine
/// into Unity's random state.
/// </summary>
public class UnityBotRandomSource : IBotRandomSource
{
    public int NextInt(int minimumInclusive, int maximumExclusive)
    {
        return Random.Range(minimumInclusive, maximumExclusive);
    }
}
