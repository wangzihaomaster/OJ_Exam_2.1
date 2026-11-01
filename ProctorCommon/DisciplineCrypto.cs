using System.Security.Cryptography;
using System.Text;

namespace ProctorCommon;

/// <summary>
/// 比赛纪律信息加解密工具。
///
/// 安全模型（防篡改 + 保密）：
///   1. 每个考场/比赛拥有一个口令（由教师下发、学生在客户端输入）。
///   2. 用 PBKDF2-SHA256(口令, 随机盐, 200000 次迭代) 派生出两把 32 字节密钥：
///        keyEnc = Derive(password, salt, "ENC")  用于 AES-256-CBC 加密明文 JSON
///        keyMac = Derive(password, salt, "MAC")  用于 HMAC-SHA256 完整性签名
///   3. 文件魔数 MAGIC + 版本号 + 盐 + IV + 密文 + HMAC，便于教师端识别与校验。
///
/// 只有知道口令的教师才能解密；HMAC 覆盖头部+密文，任何改动都会被检出。
/// </summary>
public static class DisciplineCrypto
{
    /// <summary>自定义文件扩展名建议值。</summary>
    public const string FileExtension = ".disc";

    private static readonly byte[] Magic = "NUOZJDISC1"u8.ToArray(); // 10 字节魔数+版本字符
    private const int SaltLen = 16;
    private const int IvLen = 16;
    private const int Pbkdf2Iterations = 200_000;
    private const int KeyLen = 32; // AES-256

    /// <summary>派生密钥：PBKDF2-SHA256(password, salt||purposeInfo)。</summary>
    private static byte[] DeriveKey(string password, byte[] salt, string purpose)
    {
        var info = Encoding.UTF8.GetBytes(purpose);
        var combinedSalt = new byte[salt.Length + info.Length];
        Buffer.BlockCopy(salt, 0, combinedSalt, 0, salt.Length);
        Buffer.BlockCopy(info, 0, combinedSalt, salt.Length, info.Length);
        using var kdf = new Rfc2898DeriveBytes(password, combinedSalt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeyLen);
    }

    /// <summary>
    /// 将纪律信息 JSON 加密并签名为二进制封包。
    /// 结构: Magic(10) | salt(16) | iv(16) | cipherLen(4, LE) | cipher(n) | hmac(32)
    /// </summary>
    public static byte[] Encrypt(string jsonPlain, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("加密口令不能为空", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var iv = RandomNumberGenerator.GetBytes(IvLen);
        var keyEnc = DeriveKey(password, salt, "ENC");
        var keyMac = DeriveKey(password, salt, "MAC");

        byte[] cipher;
        using (var aes = Aes.Create())
        {
            aes.Key = keyEnc;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var enc = aes.CreateEncryptor();
            cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(jsonPlain), 0, Encoding.UTF8.GetByteCount(jsonPlain));
        }

        // 头部 + 密文长度 + 密文 一起参与 HMAC
        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(salt);
        ms.Write(iv);
        ms.Write(BitConverter.GetBytes(cipher.Length));
        ms.Write(cipher);

        using var hmac = new HMACSHA256(keyMac);
        var mac = hmac.ComputeHash(ms.ToArray());
        ms.Write(mac);
        return ms.ToArray();
    }

    /// <summary>
    /// 解密并验签。口令错误 / 文件被篡改都会抛出 <see cref="DisciplineDecryptException"/>。
    /// </summary>
    public static string Decrypt(byte[] packet, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new DisciplineDecryptException("解密口令不能为空");

        int off = 0;
        var magic = ReadFixed(packet, ref off, Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new DisciplineDecryptException("文件不是有效的比赛纪律信息（魔数不匹配）");

        var salt = ReadFixed(packet, ref off, SaltLen);
        var iv = ReadFixed(packet, ref off, IvLen);
        var cipherLen = BitConverter.ToInt32(packet, off); off += 4;
        if (cipherLen <= 0 || off + cipherLen + 32 > packet.Length)
            throw new DisciplineDecryptException("文件已损坏或不是有效的纪律信息封包");
        var cipher = ReadFixed(packet, ref off, cipherLen);
        var mac = ReadFixed(packet, ref off, 32);

        var keyEnc = DeriveKey(password, salt, "ENC");
        var keyMac = DeriveKey(password, salt, "MAC");

        // 重新计算 HMAC（覆盖除最后 32 字节以外的全部内容）
        using (var hmac = new HMACSHA256(keyMac))
        {
            var expected = hmac.ComputeHash(packet, 0, packet.Length - 32);
            if (!CryptographicOperations.FixedTimeEquals(expected, mac))
                throw new DisciplineDecryptException("完整性校验失败：口令错误或文件被篡改");
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = keyEnc;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            throw new DisciplineDecryptException("解密失败：" + ex.Message);
        }
    }

    private static byte[] ReadFixed(byte[] data, ref int off, int len)
    {
        if (off + len > data.Length) throw new DisciplineDecryptException("数据不完整");
        var r = new byte[len];
        Buffer.BlockCopy(data, off, r, 0, len);
        off += len;
        return r;
    }

    /// <summary>把一个封包写入磁盘。</summary>
    public static void SavePacket(string path, byte[] packet) => File.WriteAllBytes(path, packet);

    /// <summary>从磁盘读取封包。</summary>
    public static byte[] LoadPacket(string path) => File.ReadAllBytes(path);
}

/// <summary>解密/验签失败时抛出的异常。</summary>
public class DisciplineDecryptException : Exception
{
    public DisciplineDecryptException(string message) : base(message) { }
}
