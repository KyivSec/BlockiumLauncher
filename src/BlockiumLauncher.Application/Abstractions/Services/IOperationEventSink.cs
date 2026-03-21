using BlockiumLauncher.Contracts.Operations;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IOperationEventSink
{
    Task PublishAsync(OperationEventDto Event, CancellationToken CancellationToken);
}
