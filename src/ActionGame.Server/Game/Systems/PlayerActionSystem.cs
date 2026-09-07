using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class PlayerActionSystem(RoomState state)
{
    private const int MaximumPendingActions = 8;

    public bool ApplyMovementInput(long connectionId, MovementInputPacket input)
    {
        if (input.Horizontal is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Depth is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!state.PlayersByConnection.TryGetValue(connectionId, out var player))
        {
            return false;
        }

        if (player.HasMovementInput
            && input.Sequence <= player.LastMovementInputSequence)
        {
            return false;
        }

        player.HasMovementInput = true;
        player.LastMovementInputSequence = input.Sequence;
        if (player.IsDead || player.HitStunTimeRemaining > 0f)
        {
            player.Horizontal = 0;
            player.Depth = 0;
            return true;
        }

        player.Horizontal = input.Horizontal;
        player.Depth = input.Depth;
        return true;
    }

    public bool ApplyActionCommand(long connectionId, ActionCommandPacket command)
    {
        if (!Enum.IsDefined(command.ActionId))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (!state.PlayersByConnection.TryGetValue(connectionId, out var player))
        {
            return false;
        }

        if (player.HasActionCommand
            && command.Sequence <= player.LastActionCommandSequence)
        {
            return false;
        }

        player.HasActionCommand = true;
        player.LastActionCommandSequence = command.Sequence;
        if (!IsActionAllowedForPlayer(player, command.ActionId))
        {
            return false;
        }

        if (player.IsDead)
        {
            if (command.ActionId == ActionId.Revive)
            {
                EnqueueAction(player, command.ActionId);
            }

            return true;
        }

        if (player.HitStunTimeRemaining > 0f
            || command.ActionId == ActionId.Revive)
        {
            return true;
        }

        EnqueueAction(player, command.ActionId);
        return true;
    }

    public bool DrainReviveRequest(PlayerState player)
    {
        var reviveRequested = false;
        while (player.PendingActions.TryDequeue(out var actionId))
        {
            reviveRequested |= actionId == ActionId.Revive;
        }

        return reviveRequested;
    }

    public void CollectActions(
        PlayerState player,
        ICollection<PlayerState> attackers,
        ICollection<PendingSkill> pendingSkills)
    {
        var requestedAction = DrainRequestedActions(player, out var jumpRequested);
        if (jumpRequested && player.Z <= 0f)
        {
            player.VerticalVelocity = GameProtocol.JumpInitialVelocity;
        }

        if ((requestedAction is ActionId.WarriorDashSlash
                or ActionId.RangerPowerArrow)
            && player.SkillCooldownRemaining <= 0f)
        {
            player.AttackTimeRemaining = GameProtocol.MeleeSkillDuration;
            player.SkillCooldownRemaining = GameProtocol.SkillCooldown;
            player.AttackCooldownRemaining = GameProtocol.AttackCooldown;
            pendingSkills.Add(new PendingSkill(player, requestedAction.Value));
        }
        else if (requestedAction == ActionId.BasicAttack
            && player.AttackCooldownRemaining <= 0f)
        {
            player.AttackTimeRemaining = GameProtocol.AttackDuration;
            player.AttackCooldownRemaining = GameProtocol.AttackCooldown;
            attackers.Add(player);
        }
    }

    private static bool IsActionAllowedForPlayer(
        PlayerState player,
        ActionId actionId)
    {
        return actionId switch
        {
            ActionId.BasicAttack or ActionId.Jump or ActionId.Revive => true,
            ActionId.WarriorDashSlash =>
                player.PlayerId != GameProtocol.RangerPlayerId,
            ActionId.RangerPowerArrow =>
                player.PlayerId == GameProtocol.RangerPlayerId,
            _ => false,
        };
    }

    private static void EnqueueAction(PlayerState player, ActionId actionId)
    {
        if (player.PendingActions.Count < MaximumPendingActions)
        {
            player.PendingActions.Enqueue(actionId);
        }
    }

    private static ActionId? DrainRequestedActions(
        PlayerState player,
        out bool jumpRequested)
    {
        ActionId? offensiveAction = null;
        jumpRequested = false;
        while (player.PendingActions.TryDequeue(out var actionId))
        {
            if (actionId == ActionId.Jump)
            {
                jumpRequested = true;
            }
            else if (offensiveAction is null && actionId != ActionId.Revive)
            {
                offensiveAction = actionId;
            }
        }

        return offensiveAction;
    }
}
