namespace ActionGame.Contracts.Protocol;

public enum CommandInput
{
    Down,
    Forward,
    Skill,
}

public sealed class ActionCommandQueue
{
    private const int MaximumInputCount = 8;
    private readonly List<TimedInput> inputs = [];

    public int Count => inputs.Count;

    public void Enqueue(CommandInput input, double timeSeconds)
    {
        if (!Enum.IsDefined(input))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!double.IsFinite(timeSeconds)
            || timeSeconds < 0d
            || (inputs.Count > 0 && timeSeconds < inputs[^1].TimeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        inputs.Add(new TimedInput(input, timeSeconds));
        if (inputs.Count > MaximumInputCount)
        {
            inputs.RemoveAt(0);
        }
    }

    public bool TryConsume(
        IReadOnlyList<CommandInput> command,
        double timeSeconds,
        double maximumWindowSeconds)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
        {
            throw new ArgumentException("A command must contain at least one input.", nameof(command));
        }

        if (!double.IsFinite(timeSeconds)
            || timeSeconds < 0d
            || !double.IsFinite(maximumWindowSeconds)
            || maximumWindowSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        inputs.RemoveAll(input => timeSeconds - input.TimeSeconds > maximumWindowSeconds);
        if (inputs.Count < command.Count)
        {
            return false;
        }

        var startIndex = inputs.Count - command.Count;
        for (var index = 0; index < command.Count; index++)
        {
            if (inputs[startIndex + index].Input != command[index])
            {
                return false;
            }
        }

        if (inputs[^1].TimeSeconds - inputs[startIndex].TimeSeconds
            > maximumWindowSeconds)
        {
            return false;
        }

        inputs.Clear();
        return true;
    }

    public void Clear()
    {
        inputs.Clear();
    }

    private readonly record struct TimedInput(CommandInput Input, double TimeSeconds);
}
