using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class CombatSystem(
    RoomState state,
    ProjectileSystem projectileSystem,
    DamageSystem damageSystem)
{
    public void ResolveAttacks(IReadOnlyList<PlayerState> attackers)
    {
        var pendingHits = new Dictionary<PlayerState, PendingHit>();
        foreach (var attacker in attackers)
        {
            if (attacker.PlayerId == GameProtocol.RangerPlayerId)
            {
                projectileSystem.SpawnArrow(attacker, isSkillArrow: false);
                continue;
            }

            foreach (var target in state.PlayersByConnection.Values)
            {
                if (ReferenceEquals(attacker, target)
                    || target.IsDead
                    || !CombatGeometry.IsWithinAttackRange(attacker, target))
                {
                    continue;
                }

                pendingHits.TryGetValue(target, out var hit);
                var directionMultiplier =
                    attacker.Facing == FacingDirection.Right ? 1f : -1f;
                pendingHits[target] = new PendingHit(
                    hit.Damage + GameProtocol.AttackDamage,
                    hit.KnockbackVelocityX
                        + (directionMultiplier * GameProtocol.MeleeKnockbackSpeed));
            }
        }

        foreach (var pair in pendingHits)
        {
            damageSystem.ApplyDamage(
                pair.Key,
                pair.Value.Damage,
                Math.Clamp(
                    pair.Value.KnockbackVelocityX,
                    -GameProtocol.MeleeKnockbackSpeed,
                    GameProtocol.MeleeKnockbackSpeed),
                GameProtocol.MeleeHitStunDuration);
        }
    }
}
