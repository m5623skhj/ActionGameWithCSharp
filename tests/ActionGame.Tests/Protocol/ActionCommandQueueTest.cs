using ActionGame.Contracts.Protocol;

namespace ActionGame.Tests.Protocol;

public sealed class ActionCommandQueueTest
{
    private static readonly CommandInput[] SkillCommand =
        [CommandInput.Down, CommandInput.Forward, CommandInput.BasicAttack];

    [Fact]
    public void MatchingCommandIsConsumedWithinWindow()
    {
        var queue = new ActionCommandQueue();
        queue.Enqueue(CommandInput.Down, 1.0d);
        queue.Enqueue(CommandInput.Forward, 1.2d);
        queue.Enqueue(CommandInput.BasicAttack, 1.4d);

        Assert.True(queue.TryConsume(SkillCommand, 1.4d, 0.6d));
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryConsume(SkillCommand, 1.4d, 0.6d));
    }

    [Fact]
    public void ExpiredOrIncorrectCommandDoesNotMatch()
    {
        var expired = new ActionCommandQueue();
        expired.Enqueue(CommandInput.Down, 1.0d);
        expired.Enqueue(CommandInput.Forward, 1.2d);
        expired.Enqueue(CommandInput.BasicAttack, 1.8d);
        Assert.False(expired.TryConsume(SkillCommand, 1.8d, 0.6d));

        var incorrect = new ActionCommandQueue();
        incorrect.Enqueue(CommandInput.Forward, 2.0d);
        incorrect.Enqueue(CommandInput.Down, 2.1d);
        incorrect.Enqueue(CommandInput.BasicAttack, 2.2d);
        Assert.False(incorrect.TryConsume(SkillCommand, 2.2d, 0.6d));
    }

    [Fact]
    public void QueueKeepsOnlyTheMostRecentInputs()
    {
        var queue = new ActionCommandQueue();
        for (var index = 0; index < 12; index++)
        {
            queue.Enqueue(CommandInput.Down, index);
        }

        Assert.Equal(8, queue.Count);
    }
}
