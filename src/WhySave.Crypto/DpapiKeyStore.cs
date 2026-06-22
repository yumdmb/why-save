using System.Security.Cryptography;

namespace WhySave.Crypto;

public sealed class DpapiKeyStore : IDisposable
{
    private readonly string _keyFilePath;
    private byte[]? _key;
    private bool _disposed;

    public DpapiKeyStore(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    public byte[] GetOrCreateKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_key is not null)
            return _key;

        var dir = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(_keyFilePath))
        {
            var sealedBytes = File.ReadAllBytes(_keyFilePath);
            _key = ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.CurrentUser);
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);
            var sealedBytes = ProtectedData.Protect(_key, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_keyFilePath, sealedBytes);
        }

        return _key;
    }

    public void ReplaceKey(byte[] newKey)
    {
        ArgumentNullException.ThrowIfNull(newKey);
        if (newKey.Length != AesGcmCrypto.KeySize)
            throw new ArgumentException($"Key must be {AesGcmCrypto.KeySize} bytes.", nameof(newKey));

        ObjectDisposedException.ThrowIf(_disposed, this);

        var oldKey = _key;
        _key = newKey;

        var sealedBytes = ProtectedData.Protect(_key, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_keyFilePath, sealedBytes);

        if (oldKey is not null)
            CryptographicOperations.ZeroMemory(oldKey);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }
    }
}
