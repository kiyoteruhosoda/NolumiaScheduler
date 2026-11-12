using NolumiaScheduler.Infrastructure;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class StorageConfigTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
        => _dir = Path.Combine(Path.GetTempPath(), $"nolumia-cfg-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [TestMethod]
    public void GetBackend_NoSetting_DefaultsToJson()
    {
        Assert.AreEqual(StorageBackend.Json, new StorageConfig(_dir).GetBackend());
    }

    [TestMethod]
    public void SetBackend_SavedValueCanBeReadBack()
    {
        new StorageConfig(_dir).SetBackend(StorageBackend.Sqlite);

        // A fresh instance reads the persisted storage.json.
        Assert.AreEqual(StorageBackend.Sqlite, new StorageConfig(_dir).GetBackend());
    }

    [TestMethod]
    public void GetBackend_CorruptSetting_FallsBackToJson()
    {
        var config = new StorageConfig(_dir);
        File.WriteAllText(config.FilePath, "{ this is not json");

        Assert.AreEqual(StorageBackend.Json, config.GetBackend());
    }
}
