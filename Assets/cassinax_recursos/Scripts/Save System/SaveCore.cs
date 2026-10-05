// Cassinax Unity System Save - v1.0.0
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace cassinax.savesystem
{
    public sealed class SaveLimits
    {
        public int MaxPayloadBytes = 4 * 1024 * 1024;
        public int MaxPlainTextBytes = 8 * 1024 * 1024;
        public int MaxEntries = 20000;
        public int MaxKeyBytes = 1024;
        public int MaxValueBytes = 256 * 1024;
        internal void Validate()
        {
            if (MaxPayloadBytes < 128 || MaxPlainTextBytes < 128 || MaxEntries < 1 ||
                MaxKeyBytes < 1 || MaxValueBytes < 1) throw new ArgumentOutOfRangeException(nameof(SaveLimits));
        }
    }

    public class SaveCore
    {
        public const int CurrentCodecVersion = 2;
        private const string Codec = "CUSS2\n";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly object _lock = new object();
        private Dictionary<string, string> _data = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string _currentKey;
        public int CoreVersion => CurrentCodecVersion;
        public bool UseCompression { get; }
        public SaveLimits Limits { get; }

        public SaveCore(string encryptionKey, int coreVersion, bool useCompression = true, SaveLimits limits = null)
        {
            _currentKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
            int length = Utf8.GetByteCount(encryptionKey);
            if (length != 16 && length != 24 && length != 32) throw new ArgumentException("Invalid AES key length.");
            if (coreVersion < 1 || coreVersion > CurrentCodecVersion) throw new ArgumentOutOfRangeException(nameof(coreVersion));
            UseCompression = useCompression;
            Limits = limits ?? new SaveLimits();
            Limits.Validate();
        }
        public void Set(string key, string value)
        {
            ValidateEntry(key, value);
            lock (_lock)
            {
                if (!_data.ContainsKey(key) && _data.Count >= Limits.MaxEntries) throw new SaveValidationException(SaveError.LimitExceeded);
                _data[key] = value;
            }
        }
        public string Get(string key) { lock (_lock) return _data.TryGetValue(key, out var value) ? value : null; }
        public void Remove(string key) { lock (_lock) _data.Remove(key); }
        public bool ContainsKey(string key) { lock (_lock) return _data.ContainsKey(key); }
        public List<string> GetAllKeys() { lock (_lock) return _data.Keys.ToList(); }
        public List<string> GetKeysByPrefix(string prefix) { lock (_lock) return _data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList(); }
        public List<string> GetKeysContaining(string text) { lock (_lock) return _data.Keys.Where(k => k.Contains(text)).ToList(); }
        public List<string> GetKeysByCommands(List<string> commands) { lock (_lock) return _data.Keys.Where(k => commands.Any(c => k.StartsWith("[" + c + "],", StringComparison.Ordinal))).ToList(); }
        public void Clear() { lock (_lock) _data.Clear(); }
        public Dictionary<string, string> Snapshot() { lock (_lock) return new Dictionary<string, string>(_data, StringComparer.Ordinal); }
        public void Replace(IDictionary<string, string> data)
        {
            Serialize(data);
            lock (_lock) _data = new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
        public void LoadFromPlainText(string text) { Replace(Parse(text)); }
        public string SaveToPlainText() { return Serialize(Snapshot()); }

        // Independent base64 fields preserve arbitrary UTF-8, delimiters and empty values.
        public string Serialize(IDictionary<string, string> data)
        {
            if (data.Count > Limits.MaxEntries) throw new SaveValidationException(SaveError.LimitExceeded);
            var result = new StringBuilder(Codec);
            foreach (var pair in data.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ValidateEntry(pair.Key, pair.Value);
                long size = ((long)Utf8.GetByteCount(pair.Key) + 2) / 3 * 4 + ((long)Utf8.GetByteCount(pair.Value) + 2) / 3 * 4 + 2;
                if (result.Length + size > Limits.MaxPlainTextBytes) throw new SaveValidationException(SaveError.LimitExceeded);
                result.Append(Convert.ToBase64String(Utf8.GetBytes(pair.Key))).Append(':')
                    .Append(Convert.ToBase64String(Utf8.GetBytes(pair.Value))).Append('\n');
            }
            return result.ToString();
        }
        public Dictionary<string, string> Parse(string text)
        {
            CheckText(text, Limits.MaxPlainTextBytes);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            bool modern = text.StartsWith(Codec, StringComparison.Ordinal);
            if (text.StartsWith("CUSS", StringComparison.Ordinal) && !modern) throw new InvalidDataException("Unknown codec.");
            using (var reader = new StringReader(modern ? text.Substring(Codec.Length) : text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 && !modern) continue;
                    string key, value;
                    if (modern)
                    {
                        int separator = line.IndexOf(':');
                        if (separator < 1 || line.IndexOf(':', separator + 1) >= 0) throw new InvalidDataException("Invalid codec entry.");
                        key = Utf8.GetString(Convert.FromBase64String(line.Substring(0, separator)));
                        value = Utf8.GetString(Convert.FromBase64String(line.Substring(separator + 1)));
                    }
                    else
                    {
                        var match = Regex.Match(line, @"^\[([^\[\]\r\n]+)\],?\[([^\[\]\r\n]+)\]\[([^\[\]\r\n]*)\]$");
                        if (!match.Success || match.Groups[1].Value.StartsWith("/", StringComparison.Ordinal))
                            throw new InvalidDataException("Invalid legacy snapshot entry.");
                        key = "[" + match.Groups[1].Value + "],[" + match.Groups[2].Value + "]";
                        value = match.Groups[3].Value;
                    }
                    ValidateEntry(key, value);
                    if (result.ContainsKey(key)) throw new SaveValidationException(SaveError.DuplicateField);
                    if (result.Count >= Limits.MaxEntries) throw new SaveValidationException(SaveError.LimitExceeded);
                    result.Add(key, value);
                }
            }
            return result;
        }
        private void ValidateEntry(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || value == null) throw new InvalidDataException("Invalid entry.");
            CheckText(key, Limits.MaxKeyBytes);
            CheckText(value, Limits.MaxValueBytes);
        }
        private static void CheckText(string text, int maxBytes)
        {
            if (text == null || text.Length > maxBytes || Utf8.GetByteCount(text) > maxBytes)
                throw new SaveValidationException(SaveError.LimitExceeded);
        }
        public string Encrypt(string plainText)
        {
            CheckText(plainText, Limits.MaxPlainTextBytes);
            byte[] bytes = Utf8.GetBytes(plainText);
            if (UseCompression)
            {
                using (var output = new MemoryStream())
                {
                    using (var zip = new GZipStream(output, CompressionMode.Compress, true)) zip.Write(bytes, 0, bytes.Length);
                    bytes = output.ToArray();
                }
            }
            using (var aes = Aes.Create())
            {
                aes.Key = Utf8.GetBytes(_currentKey);
                aes.GenerateIV();
                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
                    if (((long)encrypted.Length + 18) / 3 * 4 > Limits.MaxPayloadBytes) throw new SaveValidationException(SaveError.LimitExceeded);
                    byte[] message = new byte[16 + encrypted.Length];
                    Buffer.BlockCopy(aes.IV, 0, message, 0, 16);
                    Buffer.BlockCopy(encrypted, 0, message, 16, encrypted.Length);
                    return Convert.ToBase64String(message);
                }
            }
        }
        public string Decrypt(string payload) { return DecryptCompativel(payload, _currentKey); }
        public string DecryptCompativel(string payload, string key)
        {
            CheckText(payload, Limits.MaxPayloadBytes);
            byte[] message = Convert.FromBase64String(payload);
            if (message.Length < 32 || (message.Length - 16) % 16 != 0) throw new InvalidDataException("Truncated payload.");
            byte[] decrypted;
            using (var aes = Aes.Create())
            {
                aes.Key = Utf8.GetBytes(key);
                aes.IV = message.Take(16).ToArray();
                using (var decryptor = aes.CreateDecryptor()) decrypted = decryptor.TransformFinalBlock(message, 16, message.Length - 16);
            }
            // A gzip signature commits to gzip; invalid gzip never falls back to plaintext.
            if (decrypted.Length >= 2 && decrypted[0] == 0x1f && decrypted[1] == 0x8b)
            {
                using (var input = new MemoryStream(decrypted))
                using (var zip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = zip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > Limits.MaxPlainTextBytes) throw new SaveValidationException(SaveError.LimitExceeded);
                        output.Write(buffer, 0, read);
                    }
                    decrypted = output.ToArray();
                }
            }
            if (decrypted.Length > Limits.MaxPlainTextBytes) throw new SaveValidationException(SaveError.LimitExceeded);
            return Utf8.GetString(decrypted);
        }
        public string ComputeHash(string text)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Utf8.GetBytes(text)));
        }
        public string ComputeIntegrityHash(string text) { return ComputeIntegrityHash(text, _currentKey); }
        public string ComputeIntegrityHash(string text, string key)
        {
            using (var hmac = new HMACSHA256(Utf8.GetBytes(key)))
                return Hex(hmac.ComputeHash(Utf8.GetBytes((text ?? "").Replace("\r\n", "\n").Replace("\r", "\n"))));
        }
        public static bool HashEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != 64 || b.Length != 64) return false;
            int difference = 0;
            for (int i = 0; i < 64; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
    }
}
