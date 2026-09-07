using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal static class MovementSystem
{
    public static void Update(PlayerState player, float deltaSeconds)
    {
        var horizontal = (float)player.Horizontal;
        var depth = (float)player.Depth;
        var lengthSquared = (horizontal * horizontal) + (depth * depth);
        if (lengthSquared > 1f)
        {
            var inverseLength = 1f / MathF.Sqrt(lengthSquared);
            horizontal *= inverseLength;
            depth *= inverseLength;
        }

        if (horizontal < 0f)
        {
            player.Facing = FacingDirection.Left;
        }
        else if (horizontal > 0f)
        {
            player.Facing = FacingDirection.Right;
        }

        var inputVelocityX = player.KnockbackVelocityX == 0f
            ? horizontal * GameProtocol.HorizontalSpeed
            : 0f;
        var nextX = player.X
            + ((inputVelocityX + player.KnockbackVelocityX) * deltaSeconds);
        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        player.X = Math.Clamp(
            nextX,
            halfPlayerSize,
            GameProtocol.WorldWidth - halfPlayerSize);
        if ((player.X <= halfPlayerSize && player.KnockbackVelocityX < 0f)
            || (player.X >= GameProtocol.WorldWidth - halfPlayerSize
                && player.KnockbackVelocityX > 0f))
        {
            player.KnockbackVelocityX = 0f;
        }
        else
        {
            player.KnockbackVelocityX = MoveTowardsZero(
                player.KnockbackVelocityX,
                GameProtocol.KnockbackDeceleration * deltaSeconds);
        }

        player.Y = Math.Clamp(
            player.Y + (depth * GameProtocol.DepthSpeed * deltaSeconds),
            GameProtocol.FloorTop,
            GameProtocol.FloorBottom);

        if (player.Z > 0f || player.VerticalVelocity > 0f)
        {
            player.Z += player.VerticalVelocity * deltaSeconds;
            player.VerticalVelocity -= GameProtocol.Gravity * deltaSeconds;
            if (player.Z <= 0f)
            {
                player.Z = 0f;
                player.VerticalVelocity = 0f;
            }
        }
    }

    private static float MoveTowardsZero(float value, float maximumDelta)
    {
        if (MathF.Abs(value) <= maximumDelta)
        {
            return 0f;
        }

        return value - (MathF.Sign(value) * maximumDelta);
    }
}
