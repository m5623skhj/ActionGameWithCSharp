using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class SkillSystem(
    RoomState state,
    ProjectileSystem projectileSystem,
    DamageSystem damageSystem)
{
    /// <summary>
    /// Resolves every skill accepted for this tick from an immutable position snapshot.
    /// Skill effects are committed before damage so container iteration order cannot
    /// cancel another action that was valid at the start of the resolution phase.
    /// </summary>
    public void Resolve(IReadOnlyList<PendingSkill> pendingSkills)
    {
        if (pendingSkills.Count == 0)
        {
            return;
        }

        var playerSnapshots = state.PlayersByConnection.Values.ToDictionary(
            player => player,
            player => new SkillPlayerSnapshot(
                player.X,
                player.Y,
                player.Z,
                player.Facing,
                player.IsDead));
        var skillIntents = pendingSkills
            .Select(pendingSkill =>
            {
                var snapshot = playerSnapshots[pendingSkill.Player];
                return new SkillIntent(
                    pendingSkill.Player,
                    pendingSkill.ActionId,
                    snapshot.X,
                    snapshot.Y,
                    snapshot.Z,
                    snapshot.Facing);
            })
            .OrderBy(intent => intent.Player.PlayerId)
            .ToArray();
        var pendingHits = new Dictionary<PlayerState, PendingSkillHit>();

        foreach (var intent in skillIntents)
        {
            switch (intent.ActionId)
            {
                case ActionId.WarriorDashSlash:
                    ResolveMeleeSkill(intent, playerSnapshots, pendingHits);
                    break;
                case ActionId.RangerPowerArrow:
                    projectileSystem.SpawnArrow(intent);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported skill action: {intent.ActionId}.");
            }
        }

        foreach (var pair in pendingHits)
        {
            damageSystem.ApplyDamage(
                pair.Key,
                pair.Value.Damage,
                Math.Clamp(
                    pair.Value.KnockbackVelocityX,
                    -GameProtocol.MeleeSkillKnockbackSpeed,
                    GameProtocol.MeleeSkillKnockbackSpeed),
                pair.Value.HitStunDuration);
        }
    }

    private static void ResolveMeleeSkill(
        SkillIntent intent,
        IReadOnlyDictionary<PlayerState, SkillPlayerSnapshot> playerSnapshots,
        IDictionary<PlayerState, PendingSkillHit> pendingHits)
    {
        var directionMultiplier = intent.Facing == FacingDirection.Right ? 1f : -1f;
        var startX = intent.X;
        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        var endX = Math.Clamp(
            startX + (directionMultiplier * GameProtocol.MeleeSkillDashDistance),
            halfPlayerSize,
            GameProtocol.WorldWidth - halfPlayerSize);
        intent.Player.X = endX;
        intent.Player.KnockbackVelocityX = 0f;

        foreach (var pair in playerSnapshots)
        {
            var target = pair.Key;
            var targetSnapshot = pair.Value;
            if (ReferenceEquals(intent.Player, target)
                || targetSnapshot.IsDead
                || MathF.Abs(targetSnapshot.Y - intent.Y)
                    > GameProtocol.AttackDepthTolerance
                || MathF.Abs(targetSnapshot.Z - intent.Z)
                    > GameProtocol.AttackHeightTolerance
                || !CombatGeometry.TryGetHorizontalSegmentHitDistance(
                    startX,
                    endX,
                    targetSnapshot.X,
                    out _))
            {
                continue;
            }

            pendingHits.TryGetValue(target, out var hit);
            pendingHits[target] = new PendingSkillHit(
                hit.Damage + GameProtocol.MeleeSkillDamage,
                hit.KnockbackVelocityX
                    + (directionMultiplier * GameProtocol.MeleeSkillKnockbackSpeed),
                Math.Max(
                    hit.HitStunDuration,
                    GameProtocol.MeleeSkillHitStunDuration));
        }
    }
}
