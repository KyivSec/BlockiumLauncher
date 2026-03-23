namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

public interface ILauncherDataMigrationService
{
    void MigrateIfNeeded();
}