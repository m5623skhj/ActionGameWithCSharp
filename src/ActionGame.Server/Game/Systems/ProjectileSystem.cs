using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class ProjectileSystem(
    RoomState state,
    DamageSystem damageSystem)
{
    public void SpawnArrow(PlayerState attacker, bool isSkillArrow)
    {
        SpawnArrow(
            attacker.PlayerId,
            attacker.X,
            attacker.Y,
            attacker.Z,
            attacker.Facing,
            isSkillArrow);
    }

    public void SpawnArrow(SkillIntent intent)
    {
        SpawnArrow(
            intent.Player.PlayerId,
            intent.X,
            intent.Y,
            intent.Z,
            intent.Facing,
            isSkillArrow: true);
    }

    public void RemoveOwnedBy(int playerId)
    {
        state.Arrows.RemoveAll(arrow => arrow.OwnerPlayerId == playerId);
    }

    public void Update(float deltaSeconds)
    {
        for (var index = state.Arrows.Count - 1; index >= 0; index--)
        {
            var arrow = state.Arrows[index];
            var remainingDistance = arrow.MaximumDistance - arrow.DistanceTraveled;
            if (remainingDistance <= 0f)
            {
                state.Arrows.RemoveAt(index);
                continue;
            }

            var travelDistance = MathF.Min(
                arrow.Speed * deltaSeconds,
                remainingDistance);
            var directionMultiplier = arrow.Direction == FacingDirection.Right
                ? 1f
                : -1f;
            var previousX = arrow.X;
            var nextX = previousX + (directionMultiplier * travelDistance);
            var target = FindTarget(arrow, previousX, nextX);
            if (target is not null)
            {
                var knockbackVelocityX = arrow.Direction == FacingDirection.Right
                    ? arrow.KnockbackSpeed
                    : -arrow.KnockbackSpeed;
                damageSystem.ApplyDamage(
                    target,
                    arrow.Damage,
                    knockbackVelocityX,
                    arrow.HitStunDuration);
                state.Arrows.RemoveAt(index);
                continue;
            }

            arrow.X = nextX;
            arrow.DistanceTraveled += travelDistance;
            if (arrow.DistanceTraveled >= arrow.MaximumDistance
                || arrow.X < 0f
                || arrow.X > GameProtocol.WorldWidth)
            {
                state.Arrows.RemoveAt(index);
            }
        }
    }

    private void SpawnArrow(
        int ownerPlayerId,
        float x,
        float y,
        float z,
        FacingDirection facing,
        bool isSkillArrow)
    {
        if (state.Arrows.Count >= GameProtocol.MaxArrows)
        {
            return;
        }

        var directionMultiplier = facing == FacingDirection.Right ? 1f : -1f;
        state.Arrows.Add(new ArrowState(
            state.TakeNextArrowId(),
            ownerPlayerId,
            x + (directionMultiplier * GameProtocol.PlayerSize / 2f),
            y,
            z + GameProtocol.ArrowSpawnHeight,
            facing,
            isSkillArrow));
    }

    private PlayerState? FindTarget(
        ArrowState arrow,
        float previousX,
        float nextX)
    {
        PlayerState? closestTarget = null;
        var closestDistance = float.MaxValue;
        foreach (var target in state.PlayersByConnection.Values)
        {
            if (target.PlayerId == arrow.OwnerPlayerId
                || target.IsDead
                || MathF.Abs(target.Y - arrow.Y)
                    > GameProtocol.ArrowDepthTolerance
                || MathF.Abs(
                    target.Z + GameProtocol.ArrowSpawnHeight - arrow.Z)
                    > GameProtocol.ArrowHeightTolerance
                || !CombatGeometry.TryGetHorizontalSegmentHitDistance(
                    previousX,
                    nextX,
                    target.X,
                    out var hitDistance)
                || hitDistance >= closestDistance)
            {
                continue;
            }

            closestTarget = target;
            closestDistance = hitDistance;
        }

        return closestTarget;
    }
}
