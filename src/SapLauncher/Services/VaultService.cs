using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 凭据库：PBKDF2-HMAC-SHA256 派生密钥 + AES-256-GCM 认证加密。
/// 文件格式：MAGIC(8) | iterations(4) | salt(16) | nonce(12) | tag(16) | ciphertext
/// </summary>
public sealed class VaultService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SAPVLT01");
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DefaultIterations = 200_000;

    private byte[]? _key;
    private byte[] _salt = Array.Empty<byte>();
    private int _iterations = DefaultIterations;
    private List<Credential> _items = new();

    public bool Exists => File.Exists(AppPaths.Vault);
    public bool IsUnlocked => _key is not null;
    public IReadOnlyList<Credential> Items => _items;

    public static byte[] DeriveKey(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>首次创建：写入空凭据库。</summary>
    public void Initialize(string password)
    {
        _salt = RandomNumberGenerator.GetBytes(SaltSize);
        _iterations = DefaultIterations;
        _key = DeriveKey(password, _salt, _iterations);
        _items = new List<Credential>();
        Save();
    }

    public bool Unlock(string password, out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(AppPaths.Vault))
            {
                error = "凭据库不存在";
                return false;
            }

            var data = File.ReadAllBytes(AppPaths.Vault);
            if (!TryParseHeader(data, out var iterations, out var salt, out var nonce, out var tag, out var cipher))
            {
                error = "凭据库文件格式无效";
                return false;
            }

            var key = DeriveKey(password, salt, iterations);
            var plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
            }
            catch (CryptographicException)
            {
                Array.Clear(key);
                error = "主密码错误，或凭据库已损坏";
                return false;
            }

            var payload = JsonSerializer.Deserialize<VaultPayload>(plain, ManifestStore.Options);
            Array.Clear(plain);

            _items = payload?.Items ?? new List<Credential>();
            _key = key;
            _salt = salt;
            _iterations = iterations;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("解锁凭据库失败", ex);
            error = "无法读取凭据库：" + ex.Message;
            return false;
        }
    }

    public void Lock()
    {
        if (_key is not null) Array.Clear(_key);
        _key = null;
        _items = new List<Credential>();
    }

    public void Save()
    {
        if (_key is null) throw new InvalidOperationException("凭据库未解锁");

        var payload = new VaultPayload { Items = _items };
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, ManifestStore.Options);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);
        Array.Clear(plain);

        AppPaths.EnsureAll();
        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(BitConverter.GetBytes(_iterations));
        ms.Write(_salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipher);

        var tmp = AppPaths.Vault + ".tmp";
        File.WriteAllBytes(tmp, ms.ToArray());
        File.Move(tmp, AppPaths.Vault, true);
    }

    /// <summary>取某客户端下用于登录的账号：默认账号优先，否则最近更新的。</summary>
    public Credential? Get(string uuid, string client)
        => _items.Where(c => c.SystemUuid == uuid &&
                             string.Equals(c.Client, client, StringComparison.Ordinal))
            .OrderByDescending(c => c.IsDefault)
            .ThenByDescending(c => c.UpdatedAt)
            .FirstOrDefault();

    /// <summary>按账号名精确取；账号名为空时等同于 Get。</summary>
    public Credential? GetByUser(string uuid, string client, string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return Get(uuid, client);
        return _items.FirstOrDefault(c => c.SystemUuid == uuid &&
                                          string.Equals(c.Client, client, StringComparison.Ordinal) &&
                                          string.Equals(c.User, user, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>某客户端的全部账号（默认账号在前）。</summary>
    public List<Credential> GetAll(string uuid, string client)
        => _items.Where(c => c.SystemUuid == uuid &&
                             string.Equals(c.Client, client, StringComparison.Ordinal))
            .OrderByDescending(c => c.IsDefault)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();

    /// <summary>某系统的默认账号（取最近使用的客户端）。</summary>
    public Credential? GetAny(string uuid)
    {
        var latestClient = _items.Where(c => c.SystemUuid == uuid)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.Client)
            .FirstOrDefault();
        return latestClient is null ? null : Get(uuid, latestClient);
    }

    public string GetDefaultUser(string uuid) => GetAny(uuid)?.User ?? "";

    public void Upsert(Credential credential)
    {
        credential.UpdatedAt = DateTime.Now;
        var idx = _items.FindIndex(c => c.SystemUuid == credential.SystemUuid &&
                                        string.Equals(c.Client, credential.Client, StringComparison.Ordinal) &&
                                        string.Equals(c.User, credential.User, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _items[idx] = credential;
        else _items.Add(credential);
        Save();
    }

    /// <summary>删除单个账号。</summary>
    public void RemoveAccount(string uuid, string client, string user)
    {
        _items.RemoveAll(c => c.SystemUuid == uuid &&
                              string.Equals(c.Client, client, StringComparison.Ordinal) &&
                              string.Equals(c.User, user, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void Remove(string uuid)
    {
        _items.RemoveAll(c => c.SystemUuid == uuid);
        Save();
    }

    public void ClearAll()
    {
        _items.Clear();
        Save();
    }

    /// <summary>校验主密码是否正确（用于修改密码前验证）。</summary>
    public bool Verify(string password)
    {
        try
        {
            if (!File.Exists(AppPaths.Vault)) return false;
            var data = File.ReadAllBytes(AppPaths.Vault);
            if (!TryParseHeader(data, out var iterations, out var salt, out var nonce, out var tag, out var cipher))
                return false;
            var key = DeriveKey(password, salt, iterations);
            var plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                return true;
            }
            catch (CryptographicException) { return false; }
            finally { Array.Clear(key); Array.Clear(plain); }
        }
        catch { return false; }
    }

    public bool ChangePassword(string newPassword, out string error)
    {
        error = "";
        if (_key is null) { error = "请先解锁"; return false; }
        try
        {
            var newSalt = RandomNumberGenerator.GetBytes(SaltSize);
            var newKey = DeriveKey(newPassword, newSalt, DefaultIterations);
            Array.Clear(_key);
            _key = newKey;
            _salt = newSalt;
            _iterations = DefaultIterations;
            Save();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseHeader(byte[] data, out int iterations, out byte[] salt,
        out byte[] nonce, out byte[] tag, out byte[] cipher)
    {
        iterations = 0; salt = Array.Empty<byte>(); nonce = Array.Empty<byte>();
        tag = Array.Empty<byte>(); cipher = Array.Empty<byte>();

        int min = Magic.Length + 4 + SaltSize + NonceSize + TagSize;
        if (data.Length < min) return false;
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) return false;

        int p = Magic.Length;
        iterations = BitConverter.ToInt32(data, p); p += 4;
        if (iterations < 10_000 || iterations > 5_000_000) return false;

        salt = data[p..(p + SaltSize)]; p += SaltSize;
        nonce = data[p..(p + NonceSize)]; p += NonceSize;
        tag = data[p..(p + TagSize)]; p += TagSize;
        cipher = data[p..];
        return cipher.Length > 0;
    }

    // ---------------- 导出 / 导入（使用独立密码加密，便于迁移给同事） ----------------

    public static string EncryptForExport(string json, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt, DefaultIterations);
        var plain = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);
        Array.Clear(key);
        Array.Clear(plain);

        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(DefaultIterations));
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipher);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static string? DecryptFromExport(string base64, string password)
    {
        try
        {
            var data = Convert.FromBase64String(base64);
            int p = 0;
            int iterations = BitConverter.ToInt32(data, p); p += 4;
            var salt = data[p..(p + SaltSize)]; p += SaltSize;
            var nonce = data[p..(p + NonceSize)]; p += NonceSize;
            var tag = data[p..(p + TagSize)]; p += TagSize;
            var cipher = data[p..];

            var key = DeriveKey(password, salt, iterations);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);
            Array.Clear(key);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}
