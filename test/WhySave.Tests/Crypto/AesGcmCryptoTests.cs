using System.Security.Cryptography;
using WhySave.Crypto;

namespace WhySave.Tests.Crypto;

public class AesGcmCryptoTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_Returns_Plaintext()
    {
        var key = NewKey();
        var plain = "For the transformers lecture"u8.ToArray();

        var blob = AesGcmCrypto.Encrypt(plain, key);

        Assert.Equal(AesGcmCrypto.NonceSize + plain.Length + AesGcmCrypto.TagSize, blob.Length);
        var decrypted = AesGcmCrypto.Decrypt(blob, key);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_Empty_Plaintext()
    {
        var key = NewKey();
        var plain = Array.Empty<byte>();

        var blob = AesGcmCrypto.Encrypt(plain, key);
        Assert.Equal(AesGcmCrypto.NonceSize + AesGcmCrypto.TagSize, blob.Length);

        var decrypted = AesGcmCrypto.Decrypt(blob, key);
        Assert.Empty(decrypted);
    }

    [Fact]
    public void Each_Encrypt_Produces_Different_Nonce()
    {
        var key = NewKey();
        var plain = "hello"u8.ToArray();

        var blob1 = AesGcmCrypto.Encrypt(plain, key);
        var blob2 = AesGcmCrypto.Encrypt(plain, key);

        Assert.NotEqual(blob1.AsSpan(0, AesGcmCrypto.NonceSize).ToArray(),
                        blob2.AsSpan(0, AesGcmCrypto.NonceSize).ToArray());
    }

    [Fact]
    public void Tampered_Ciphertext_Is_Rejected()
    {
        var key = NewKey();
        var plain = "secret reason"u8.ToArray();
        var blob = AesGcmCrypto.Encrypt(plain, key);

        blob[^AesGcmCrypto.TagSize] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(blob, key));
    }

    [Fact]
    public void Tampered_Nonce_Is_Rejected()
    {
        var key = NewKey();
        var plain = "secret reason"u8.ToArray();
        var blob = AesGcmCrypto.Encrypt(plain, key);

        blob[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(blob, key));
    }

    [Fact]
    public void Wrong_Key_Is_Rejected()
    {
        var key1 = NewKey();
        var key2 = NewKey();
        var plain = "secret"u8.ToArray();
        var blob = AesGcmCrypto.Encrypt(plain, key1);

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(blob, key2));
    }

    [Fact]
    public void Blob_Too_Short_Throws()
    {
        var key = NewKey();
        Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(new byte[10], key));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void Wrong_Key_Size_Throws(int badLen)
    {
        var badKey = RandomNumberGenerator.GetBytes(badLen);
        Assert.Throws<ArgumentException>(() => AesGcmCrypto.Encrypt("x"u8.ToArray(), badKey));
    }
}
