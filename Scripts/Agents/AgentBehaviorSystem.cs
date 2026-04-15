using System;
using System.Collections.Generic;
using SpacedOut.State;

namespace SpacedOut.Agents;

/// <summary>
/// Stateless tick-based AI that updates velocity and mode transitions
/// for all agent-controlled contacts each frame.
/// </summary>
public static class AgentBehaviorSystem
{
    private const float MapBoundary = 1000f;
    private const float DespawnMargin = 30f;

    public static void Update(List<Contact> contacts, ShipState ship, float delta)
    {
        foreach (var contact in contacts)
        {
            if (contact.Agent == null || contact.IsDestroyed) continue;
            var agent = contact.Agent;
            agent.ModeTimer += delta;

            UpdateModeTransitions(contact, agent, ship);
            UpdateMovement(contact, agent, ship, delta);
        }
    }

    // ── Mode transitions ─────────────────────────────────────────────

    private static void UpdateModeTransitions(Contact contact, AgentState agent, ShipState ship)
    {
        if (agent.Mode == AgentBehaviorMode.Destroyed) return;

        if (contact.IsDestroyed)
        {
            TransitionTo(agent, AgentBehaviorMode.Destroyed);
            return;
        }

        if (!AgentDefinition.TryGet(agent.AgentType, out var def)) return;

        float distToPlayer = DistanceTo(contact, ship);

        switch (agent.Mode)
        {
            case AgentBehaviorMode.Idle:
                TransitionTo(agent, def.InitialMode);
                break;

            case AgentBehaviorMode.Patrol:
                if (def.DetectionRadius > 0 && distToPlayer < def.DetectionRadius)
                    TransitionTo(agent, AgentBehaviorMode.Intercept);
                break;

            case AgentBehaviorMode.Guard:
                if (def.DetectionRadius > 0 && distToPlayer < def.DetectionRadius)
                    TransitionTo(agent, AgentBehaviorMode.Intercept);
                break;

            case AgentBehaviorMode.Intercept:
                if (distToPlayer < def.AttackRange && def.AttackDamage > 0)
                    TransitionTo(agent, AgentBehaviorMode.Attack);
                else if (distToPlayer > def.DisengageRadius)
                    TransitionTo(agent, def.InitialMode);
                break;

            case AgentBehaviorMode.Attack:
                if (def.CanFlee && contact.HitPoints / contact.MaxHitPoints <= def.FleeThreshold)
                    TransitionTo(agent, AgentBehaviorMode.Flee);
                else if (distToPlayer > def.AttackRange * 1.3f)
                    TransitionTo(agent, AgentBehaviorMode.Intercept);
                break;

            case AgentBehaviorMode.Transit:
                float dx = agent.DestinationX - contact.PositionX;
                float dy = agent.DestinationY - contact.PositionY;
                if (dx * dx + dy * dy < 30f * 30f)
                    TransitionTo(agent, AgentBehaviorMode.Destroyed);
                if (IsOutOfBounds(contact))
                    TransitionTo(agent, AgentBehaviorMode.Destroyed);
                break;

            case AgentBehaviorMode.Flee:
                if (IsOutOfBounds(contact))
                    TransitionTo(agent, AgentBehaviorMode.Destroyed);
                break;
        }
    }

    private static void TransitionTo(AgentState agent, AgentBehaviorMode newMode)
    {
        if (agent.Mode == newMode) return;
        agent.Mode = newMode;
        agent.ModeTimer = 0f;
    }

    // ── Movement per mode ────────────────────────────────────────────

    private static void UpdateMovement(Contact contact, AgentState agent, ShipState ship, float delta)
    {
        switch (agent.Mode)
        {
            case AgentBehaviorMode.Idle:
                UpdateIdle(contact, agent, delta);
                break;
            case AgentBehaviorMode.Patrol:
                UpdatePatrol(contact, agent, delta);
                break;
            case AgentBehaviorMode.Guard:
                UpdateGuard(contact, agent, delta);
                break;
            case AgentBehaviorMode.Transit:
                UpdateTransit(contact, agent, delta);
                break;
            case AgentBehaviorMode.Intercept:
                UpdateIntercept(contact, agent, ship, delta);
                break;
            case AgentBehaviorMode.Attack:
                UpdateAttack(contact, agent, ship, delta);
                break;
            case AgentBehaviorMode.Flee:
                UpdateFlee(contact, agent, ship);
                break;
            case AgentBehaviorMode.Destroyed:
                contact.VelocityX = 0;
                contact.VelocityY = 0;
                break;
        }
    }

    private static void UpdateIdle(Contact contact, AgentState agent, float delta)
    {
        float drift = MathF.Sin(agent.ModeTimer * 0.5f + agent.PhaseOffset) * 0.3f;
        contact.VelocityX = drift;
        contact.VelocityY = MathF.Cos(agent.ModeTimer * 0.3f + agent.PhaseOffset) * 0.2f;
    }

    private static void UpdatePatrol(Contact contact, AgentState agent, float delta)
    {
        if (!AgentDefinition.TryGet(agent.AgentType, out var def)) return;

        agent.OrbitAngle += def.AngularSpeed * delta;
        float speed = def.BaseSpeed * 0.6f;
        float r = def.PatrolOrbitRadius;

        float targetX = agent.AnchorX + MathF.Cos(agent.OrbitAngle) * r;
        float targetY = agent.AnchorY + MathF.Sin(agent.OrbitAngle) * r;

        float dx = targetX - contact.PositionX;
        float dy = targetY - contact.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist > 0.1f)
        {
            contact.VelocityX = dx / dist * speed;
            contact.VelocityY = dy / dist * speed;
        }
    }

    private static void UpdateGuard(Contact contact, AgentState agent, float delta)
    {
        if (!AgentDefinition.TryGet(agent.AgentType, out var def)) return;

        agent.OrbitAngle += def.AngularSpeed * delta;
        float speed = def.BaseSpeed * 0.4f;
        float r = def.PatrolOrbitRadius;

        float targetX = agent.AnchorX + MathF.Cos(agent.OrbitAngle) * r;
        float targetY = agent.AnchorY + MathF.Sin(agent.OrbitAngle) * r;

        float dx = targetX - contact.PositionX;
        float dy = targetY - contact.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist > 0.1f)
        {
            contact.VelocityX = dx / dist * speed;
            contact.VelocityY = dy / dist * speed;
        }
    }

    private static void UpdateTransit(Contact contact, AgentState agent, float delta)
    {
        float dx = agent.DestinationX - contact.PositionX;
        float dy = agent.DestinationY - contact.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 1f)
        {
            contact.VelocityX = 0;
            contact.VelocityY = 0;
            return;
        }

        float baseVx = dx / dist * agent.BaseSpeed;
        float baseVy = dy / dist * agent.BaseSpeed;

        float nx = -dy / dist;
        float ny = dx / dist;
        float wobble = MathF.Sin(agent.ModeTimer * 0.3f + agent.PhaseOffset) * 0.4f;

        contact.VelocityX = baseVx + nx * wobble;
        contact.VelocityY = baseVy + ny * wobble;
    }

    private static void UpdateIntercept(Contact contact, AgentState agent, ShipState ship, float delta)
    {
        float dx = ship.PositionX - contact.PositionX;
        float dy = ship.PositionY - contact.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 1f) return;

        contact.VelocityX = dx / dist * agent.BaseSpeed;
        contact.VelocityY = dy / dist * agent.BaseSpeed;
    }

    private static void UpdateAttack(Contact contact, AgentState agent, ShipState ship, float delta)
    {
        if (!AgentDefinition.TryGet(agent.AgentType, out var def)) return;

        float toPlayerX = ship.PositionX - contact.PositionX;
        float toPlayerY = ship.PositionY - contact.PositionY;
        float dist = MathF.Sqrt(toPlayerX * toPlayerX + toPlayerY * toPlayerY);
        if (dist < 0.1f) return;

        float nx = toPlayerX / dist;
        float ny = toPlayerY / dist;

        float perpX = -ny * agent.OrbitDirection;
        float perpY = nx * agent.OrbitDirection;

        float idealDist = def.AttackRange * 0.7f;
        float approachStrength = (dist - idealDist) * 0.02f;
        approachStrength = Math.Clamp(approachStrength, -0.5f, 0.5f);

        float vx = perpX + nx * approachStrength;
        float vy = perpY + ny * approachStrength;
        float len = MathF.Sqrt(vx * vx + vy * vy);
        if (len > 0.01f)
        {
            contact.VelocityX = vx / len * agent.BaseSpeed;
            contact.VelocityY = vy / len * agent.BaseSpeed;
        }

        agent.OrbitAngle += delta;
    }

    private static void UpdateFlee(Contact contact, AgentState agent, ShipState ship)
    {
        float dx = contact.PositionX - ship.PositionX;
        float dy = contact.PositionY - ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 0.1f)
        {
            contact.VelocityX = agent.BaseSpeed * 1.5f;
            contact.VelocityY = 0;
            return;
        }

        float fleeSpeed = agent.BaseSpeed * 1.5f;
        contact.VelocityX = dx / dist * fleeSpeed;
        contact.VelocityY = dy / dist * fleeSpeed;
    }

    // ── Utilities ────────────────────────────────────────────────────

    private static float DistanceTo(Contact contact, ShipState ship)
    {
        float dx = contact.PositionX - ship.PositionX;
        float dy = contact.PositionY - ship.PositionY;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsOutOfBounds(Contact contact) =>
        contact.PositionX < -DespawnMargin || contact.PositionX > MapBoundary + DespawnMargin ||
        contact.PositionY < -DespawnMargin || contact.PositionY > MapBoundary + DespawnMargin;
}
