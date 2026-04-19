using SpacedOut.State;

namespace SpacedOut.Agents;

/// <summary>
/// Deterministic per-contact variation so agents do not share identical orbit phase, direction, and ring distance.
/// </summary>
public static class AgentSpawnPersonality
{
    /// <summary>
    /// Sets <see cref="AgentState.OrbitDirection"/>, patrol phase, <see cref="AgentState.PhaseOffset"/>,
    /// <see cref="AgentState.AttackIdealDistFactor"/>, and jittered <see cref="AgentState.BaseSpeed"/>.
    /// </summary>
    public static void Apply(AgentState agent, string contactId, float positionX, float positionY, float definitionBaseSpeed)
    {
        uint h = HashFnv1a(contactId);
        agent.OrbitDirection = (h & 1) == 0 ? 1 : -1;
        agent.OrbitAngle = positionX * 0.1f + (h % 628) * 0.01f;
        agent.PhaseOffset = positionY * 0.07f + ((h >> 16) % 628) * 0.01f;
        agent.AttackIdealDistFactor = 0.88f + (h % 25) * 0.01f;
        agent.BaseSpeed = definitionBaseSpeed * (0.92f + (h % 17) * 0.01f);
    }

    private static uint HashFnv1a(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619u;
        }
        return h;
    }
}
