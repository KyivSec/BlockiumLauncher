using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Common;

public sealed record AccountSummary(
    AccountId AccountId,
    string DisplayName,
    AccountProvider Provider,
    AccountState State,
    bool IsDefault,
    DateTimeOffset? LastValidatedAtUtc);
