namespace ActionGame.Server.Game;

internal sealed class RoomState
{
    private int nextArrowId = 1;

    public Dictionary<long, PlayerState> PlayersByConnection { get; } = [];

    public List<ArrowState> Arrows { get; } = [];

    public double ServerTimeSeconds { get; set; }

    public long ServerTick { get; set; }

    public int TakeNextArrowId()
    {
        return nextArrowId++;
    }
}
