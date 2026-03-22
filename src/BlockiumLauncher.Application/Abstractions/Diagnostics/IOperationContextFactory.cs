namespace BlockiumLauncher.Application.Abstractions.Diagnostics;

public interface IOperationContextFactory
{
    OperationContext Create(string OperationName);
}