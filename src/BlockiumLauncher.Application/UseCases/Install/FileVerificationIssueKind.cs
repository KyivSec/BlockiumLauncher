namespace BlockiumLauncher.Application.UseCases.Install;

public enum FileVerificationIssueKind
{
    RootDirectoryMissing = 1,
    MinecraftDirectoryMissing = 2,
    BlockiumDirectoryMissing = 3
}