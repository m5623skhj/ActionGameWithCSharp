using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class PlayerState(int playerId, float x, float y)
{
    public int PlayerId { get; } = playerId;

    public float X { get; set; } = x;

    public float Y { get; set; } = y;

    public float Z { get; set; }

    public sbyte Horizontal { get; set; }

    public sbyte Depth { get; set; }

    public float VerticalVelocity { get; set; }

    public float KnockbackVelocityX { get; set; }

    public float HitStunTimeRemaining { get; set; }

    public float InvulnerabilityTimeRemaining { get; set; }

    public float AttackTimeRemaining { get; set; }

    public float AttackCooldownRemaining { get; set; }

    public float SkillCooldownRemaining { get; set; }

    public int Health { get; set; } = GameProtocol.MaxHealth;

    public bool IsDead => Health == 0;

    public double? DeathTimeSeconds { get; set; }

    public FacingDirection Facing { get; set; } = FacingDirection.Right;

    public Queue<ActionId> PendingActions { get; } = [];

    public uint LastMovementInputSequence { get; set; }

    public bool HasMovementInput { get; set; }

    public uint LastActionCommandSequence { get; set; }

    public bool HasActionCommand { get; set; }

    public void ClearControllableState()
    {
        Horizontal = 0;
        Depth = 0;
        PendingActions.Clear();
    }
}
