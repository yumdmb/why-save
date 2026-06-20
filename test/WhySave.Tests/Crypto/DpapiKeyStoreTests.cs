using System.IO;
using System.Security.Cryptography;
using WhySave.Crypto;

namespace WhySave.Tests.Crypto;

public class DpapiKeyStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keyPath;

    public DpapiKeyStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _keyPath = Path.Combine(_tempDir, "key.bin");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void First_Run_Creates_Key_File()
    {
        Assert.False(File.Exists(_keyPath));

        using var store = new DpapiKeyStore(_keyPath);
        var key = store.GetOrCreateKey();

        Assert.Equal(AesGcmCrypto.KeySize, key.Length);
        Assert.True(File.Exists(_keyPath));
    }

    [Fact]
    public void Second_Run_Reuses_Same_Key()
    {
        byte[] firstKey;
        using (var store1 = new DpapiKeyStore(_keyPath))
            firstKey = store1.GetOrCreateKey();

        byte[] secondKey;
        using (var store2 = new DpapiKeyStore(_keyPath))
            secondKey = store2.GetOrCreateKey();

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void Key_File_Is_DPAPI_Encrypted_Not_Plaintext()
    {
        using var store = new DpapiKeyStore(_keyPath);
        var key = store.GetOrCreateKey();

        var fileBytes = File.ReadAllBytes(_keyPath);

        Assert.NotEqual(key, fileBytes);
        Assert.True(fileBytes.Length > AesGcmCrypto.KeySize);
    }

    [Fact]
    public void Key_File_Can_Be_Unprotected_With_CurrentUser_Scope()
    {
        using var store = new DpapiKeyStore(_keyPath);
        var key = store.GetOrCreateKey();

        var fileBytes = File.ReadAllBytes(_keyPath);
        var unprotected = ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.CurrentUser);

        Assert.Equal(key, unprotected);
    }

    [Fact]
    public void Key_File_Scoped_To_CurrentUser()
    {
        using var store = new DpapiKeyStore(_keyPath);
        var key = store.GetOrCreateKey();
        var fileBytes = File.ReadAllBytes(_keyPath);

        Assert.NotEqual(key, fileBytes);

        var unprotected = ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.CurrentUser);
        Assert.Equal(key, unprotected);

        try
        {
            ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException)
        {
        }
    }

    [Fact]
    public void Dispose_Zeros_In_Memory_Key()
    {
        byte[] key;
        using (var store = new DpapiKeyStore(_keyPath))
            key = store.GetOrCreateKey();

        Assert.All(key, b => Assert.Equal(0, b));
    }

    [Fact]
    public void GetOrCreateKey_After_Dispose_Throws()
    {
        var store = new DpapiKeyStore(_keyPath);
        store.GetOrCreateKey();
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.GetOrCreateKey());
    }

    [Fact]
    public void Key_Survives_AesGcm_RoundTrip_Across_Instances()
    {
        byte[] cipher;
        using (var store1 = new DpapiKeyStore(_keyPath))
        {
            var key = store1.GetOrCreateKey();
            cipher = AesGcmCrypto.Encrypt("reason text"u8.ToArray(), key);
        }

        using var store2 = new DpapiKeyStore(_keyPath);
        var key2 = store2.GetOrCreateKey();
        var plain = AesGcmCrypto.Decrypt(cipher, key2);

        Assert.Equal("reason text"u8.ToArray(), plain);
    }
}
