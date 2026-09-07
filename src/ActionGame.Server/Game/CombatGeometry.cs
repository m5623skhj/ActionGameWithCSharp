using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal static class CombatGeometry
{
    public static bool IsWithinAttackRange(PlayerState attacker, PlayerState target)
    {
        var facingMultiplier = attacker.Facing == FacingDirection.Right ? 1f : -1f;
        var forwardDistance = (target.X - attacker.X) * facingMultiplier;
        var horizontalReach = GameProtocol.PlayerSize + GameProtocol.AttackReach;
        return forwardDistance >= 0f
            && forwardDistance <= horizontalReach
            && MathF.Abs(target.Y - attacker.Y)
                <= GameProtocol.AttackDepthTolerance
            && MathF.Abs(target.Z - attacker.Z)
                <= GameProtocol.AttackHeightTolerance;
    }

    public static bool TryGetHorizontalSegmentHitDistance(
        float previousX,
        float nextX,
        float targetX,
        out float hitDistance)
    {
        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        var targetMinX = targetX - halfPlayerSize;
        var targetMaxX = targetX + halfPlayerSize;
        var segmentMinX = MathF.Min(previousX, nextX);
        var segmentMaxX = MathF.Max(previousX, nextX);
        if (segmentMaxX < targetMinX || segmentMinX > targetMaxX)
        {
            hitDistance = 0f;
            return false;
        }

        var hitX = nextX >= previousX
            ? MathF.Max(previousX, targetMinX)
            : MathF.Min(previousX, targetMaxX);
        hitDistance = MathF.Abs(hitX - previousX);
        return true;
    }
}
