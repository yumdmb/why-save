using Dapper;

namespace WhySave.Storage;

[ModuleInitializer]
internal static class StorageModuleInit
{
    public static void Init()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
