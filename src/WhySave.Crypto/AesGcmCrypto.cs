using System.Security.Cryptography;

namespace WhySave.Crypto;

public static class AesGcmCrypto
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;

    public static byte[] Encrypt(byte[] plain, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes (256-bit).", nameof(key));

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceSize + cipher.Length, TagSize);
        return blob;
    }

    public static byte[] Decrypt(byte[] blob, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes (256-bit).", nameof(key));
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext blob is too short.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[blob.Length - NonceSize - TagSize];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize + cipher.Length, tag, 0, TagSize);
        Buffer.BlockCopy(blob, NonceSize, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
