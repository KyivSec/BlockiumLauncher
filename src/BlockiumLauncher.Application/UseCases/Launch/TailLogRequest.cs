using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class TailLogRequest
{
    public InstanceId InstanceId { get; }
    public int MaxLines { get; }

    public TailLogRequest(InstanceId InstanceId, int MaxLines = 200)
    {
        if (MaxLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLines), "MaxLines must be greater than zero.");
        }

        this.InstanceId = InstanceId;
        this.MaxLines = MaxLines;
    }
}
