using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

/// <summary>
/// Owns one room simulation and coordinates its systems on a single server thread.
/// </summary>
public sealed class GameRoom
{
    private readonly RoomState state = new();
    private readonly PlayerRoster playerRoster;
    private readonly PlayerActionSystem playerActionSystem;
    private readonly RespawnSystem respawnSystem;
    private readonly SkillSystem skillSystem;
    private readonly CombatSystem combatSystem;
    private readonly ProjectileSystem projectileSystem;
    private readonly RoomSnapshotFactory snapshotFactory;

    public GameRoom()
    {
        playerRoster = new PlayerRoster(state);
        playerActionSystem = new PlayerActionSystem(state);
        respawnSystem = new RespawnSystem(state);
        var damageSystem = new DamageSystem(state);
        projectileSystem = new ProjectileSystem(state, damageSystem);
        skillSystem = new SkillSystem(state, projectileSystem, damageSystem);
        combatSystem = new CombatSystem(
            state,
            projectileSystem,
            damageSystem);
        snapshotFactory = new RoomSnapshotFactory(state, respawnSystem);
    }

    public int PlayerCount => playerRoster.PlayerCount;

    public long ServerTick => state.ServerTick;

    public bool TryJoin(
        long connectionId,
        out int playerId,
        out JoinRejectReason rejectionReason)
    {
        return playerRoster.TryJoin(
            connectionId,
            out playerId,
            out rejectionReason);
    }

    public bool Leave(long connectionId)
    {
        if (!playerRoster.TryLeave(connectionId, out var player))
        {
            return false;
        }

        projectileSystem.RemoveOwnedBy(player.PlayerId);
        return true;
    }

    public bool ApplyMovementInput(long connectionId, MovementInputPacket input)
    {
        return playerActionSystem.ApplyMovementInput(connectionId, input);
    }

    public bool ApplyActionCommand(long connectionId, ActionCommandPacket command)
    {
        return playerActionSystem.ApplyActionCommand(connectionId, command);
    }

    public void Update(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        state.ServerTimeSeconds += deltaSeconds;
        var attackers = new List<PlayerState>();
        var pendingSkills = new List<PendingSkill>();
        foreach (var player in state.PlayersByConnection.Values)
        {
            PlayerStatusSystem.TickTimers(player, deltaSeconds);
            if (player.IsDead)
            {
                respawnSystem.UpdateDeadPlayer(
                    player,
                    playerActionSystem.DrainReviveRequest(player));
                continue;
            }

            if (player.HitStunTimeRemaining > 0f)
            {
                player.ClearControllableState();
            }
            else
            {
                playerActionSystem.CollectActions(
                    player,
                    attackers,
                    pendingSkills);
            }

            MovementSystem.Update(player, deltaSeconds);
        }

        skillSystem.Resolve(pendingSkills);
        combatSystem.ResolveAttacks(attackers);
        projectileSystem.Update(deltaSeconds);
        state.ServerTick++;
    }

    public WorldSnapshotPacket CreateSnapshot()
    {
        return snapshotFactory.CreateSnapshot();
    }

    public bool TryGetPlayer(long connectionId, out PlayerSnapshot player)
    {
        return snapshotFactory.TryGetPlayer(connectionId, out player);
    }
}
