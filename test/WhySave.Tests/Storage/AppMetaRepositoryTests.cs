using WhySave.Storage.Repositories;

namespace WhySave.Tests.Storage;

public class AppMetaRepositoryTests : StorageTestBase
{
    [Fact]
    public void Get_Returns_Null_When_Key_Not_Found()
    {
        var repo = new AppMetaRepository(Connection);
        Assert.Null(repo.Get("nonexistent"));
    }

    [Fact]
    public void Set_And_Get_RoundTrip()
    {
        var repo = new AppMetaRepository(Connection);
        repo.Set("first_run_at", "1700000000000");

        Assert.Equal("1700000000000", repo.Get("first_run_at"));
    }

    [Fact]
    public void Set_Overwrites_Existing_Value()
    {
        var repo = new AppMetaRepository(Connection);
        repo.Set("schema_version", "1");
        repo.Set("schema_version", "3");

        Assert.Equal("3", repo.Get("schema_version"));
    }

    [Fact]
    public void Set_Null_Value_Persists_As_Null()
    {
        var repo = new AppMetaRepository(Connection);
        repo.Set("last_rescan_at", "123");
        repo.Set("last_rescan_at", null);

        Assert.Null(repo.Get("last_rescan_at"));
    }

    [Fact]
    public void Multiple_Keys_Coexist()
    {
        var repo = new AppMetaRepository(Connection);
        repo.Set("first_run_at", "100");
        repo.Set("schema_version", "3");
        repo.Set("last_rescan_at", "200");

        Assert.Equal("100", repo.Get("first_run_at"));
        Assert.Equal("3", repo.Get("schema_version"));
        Assert.Equal("200", repo.Get("last_rescan_at"));
    }
}
