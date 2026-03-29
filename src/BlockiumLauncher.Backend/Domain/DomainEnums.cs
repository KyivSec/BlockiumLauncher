namespace BlockiumLauncher.Domain.Enums
{
    public enum AccountProvider
    {
        Microsoft = 1,
        Offline = 2
    }

    public enum AccountState
    {
        Active = 0,
        Expired = 1,
        Invalid = 2,
        Removed = 3
    }

    public enum InstanceState
    {
        Created = 0,
        Installed = 1,
        NeedsRepair = 2,
        Updating = 3,
        Broken = 4,
        Deleted = 5
    }

    public enum JavaArchitecture
    {
        Unknown = 0,
        X64 = 1,
        Arm64 = 2
    }

    public enum LoaderType
    {
        Vanilla = 0,
        Forge = 1,
        NeoForge = 2,
        Fabric = 3,
        Quilt = 4
    }
}
