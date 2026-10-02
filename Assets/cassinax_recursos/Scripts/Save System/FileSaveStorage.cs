// Cassinax Unity System Save - v0.10.0
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using cassinax.savesystem;

// File.Replace where supported; fallback recovery is deterministic, not universally atomic.
public class FileSaveStorage : ISaveStorageAdapter, IValidatedSaveStorage
{
    private readonly string _directoryPath;
    private readonly string _mainFileName;
    private Func<string, bool> _validator;
    private int _maxBytes = 4 * 1024 * 1024;
    private static readonly string[] Backups = { "Backup Auto", "Backup Manual", "Backup Seguranca" };
    private readonly object _gate = new object();

    public FileSaveStorage(string folderName, string mainFileName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || Path.IsPathRooted(folderName) ||
            folderName.Split('/', '\\').Any(s => s == "." || s == ".." || s.Length == 0))
            throw new ArgumentException("Use an owned relative save directory.");
        ValidateName(mainFileName);
        string root = Path.GetFullPath(Application.persistentDataPath);
        _directoryPath = Path.GetFullPath(Path.Combine(root, folderName));
        if (!_directoryPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Save path outside root.");
        _mainFileName = mainFileName;
        if (Backups.Any(b => b.Replace(' ', '_') + ".save" == mainFileName) || mainFileName.EndsWith(".tmp") || mainFileName.EndsWith(".bak"))
            throw new ArgumentException("Reserved file name.");
        CheckLinks(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
    }
    public void ConfigureValidation(Func<string, bool> validator, int maxPayloadBytes)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        if (maxPayloadBytes < 128) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        _maxBytes = maxPayloadBytes;
    }
    public bool HasMainSave() => ExistsAny(GetMainPath());
    public string LoadMainSave() { lock (_gate) return Recover(GetMainPath()); }
    public void SaveMainSave(string payload) { lock (_gate) WriteRecoverable(GetMainPath(), payload); }
    public bool HasBackup(string backupName) => ExistsAny(GetBackupPath(backupName));
    public string LoadBackup(string backupName) { lock (_gate) return Recover(GetBackupPath(backupName)); }
    public void SaveBackup(string backupName, string payload) { lock (_gate) WriteRecoverable(GetBackupPath(backupName), payload); }
    public void DeleteBackup(string backupName) { lock (_gate) DeleteManaged(GetBackupPath(backupName)); }
    public void DeleteAll()
    {
        lock (_gate)
        {
            // Only the four owned names and their two sidecars; never exports or the directory.
            DeleteManaged(GetMainPath());
            foreach (string backup in Backups) DeleteManaged(GetBackupPath(backup));
        }
    }
    public string GetMainPath() => CheckedPath(_mainFileName);
    public string GetBackupPath(string backupName)
    {
        if (!Backups.Contains(backupName)) throw new ArgumentException("Unknown managed backup.");
        return CheckedPath(backupName.Replace(' ', '_') + ".save");
    }
    private string CheckedPath(string name)
    {
        ValidateName(name);
        string path = Path.Combine(_directoryPath, name);
        CheckLinks(path);
        return path;
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == ".." ||
            name.Contains("/") || name.Contains("\\")) throw new ArgumentException("Invalid managed filename.");
    }
    private static void CheckLinks(string path)
    {
        for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked save paths are not supported.");
    }
    private bool ExistsAny(string path) => File.Exists(path) || File.Exists(path + ".bak") || File.Exists(path + ".tmp");
    private string ReadValid(string path)
    {
        CheckLinks(path);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > _maxBytes) return null;
        // Bound the read itself as well, including concurrent growth.
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new MemoryStream())
        {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + count > _maxBytes) return null;
                output.Write(buffer, 0, count);
            }
            string payload;
            try { payload = new UTF8Encoding(false, true).GetString(output.ToArray()); }
            catch (DecoderFallbackException) { return null; }
            return _validator != null && _validator(payload) ? payload : null;
        }
    }
    private string Recover(string path)
    {
        if (_validator == null) throw new InvalidOperationException("ConfigureValidation required.");
        // Prefer committed main, then previous good copy; tmp is last resort on first write.
        foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" })
        {
            string payload = ReadValid(candidate);
            if (payload != null) return payload;
        }
        if (ExistsAny(path)) throw new SaveValidationException(SaveError.Integrity);
        return "";
    }
    protected virtual void WriteStage(string stage) { }
    protected virtual bool SupportsAtomicReplace => true;
    private void WriteRecoverable(string path, string payload)
    {
        if (_validator == null || payload == null || Encoding.UTF8.GetByteCount(payload) > _maxBytes || !_validator(payload))
            throw new SaveValidationException(SaveError.Integrity);
        CheckLinks(path);
        string temp = path + ".tmp", backup = path + ".bak";
        CheckLinks(temp);
        CheckLinks(backup);
        Directory.CreateDirectory(_directoryPath);
        // If only tmp survived, preserve it before opening tmp with Create.
        if (ReadValid(path) == null && ReadValid(backup) == null && ReadValid(temp) != null)
            File.Copy(temp, backup, true);
        WriteStage("before-temp");
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(payload);
            stream.Write(bytes, 0, bytes.Length);
            WriteStage("after-temp-write");
            stream.Flush(true);
        }
        WriteStage("after-temp-flush");
        if (ReadValid(temp) == null) throw new IOException("Temporary save validation failed.");
        if (File.Exists(path) && ReadValid(path) != null)
        {
            WriteStage("before-replace");
            if (SupportsAtomicReplace)
            {
                try
                {
                    File.Replace(temp, path, backup);
                    WriteStage("after-replace");
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (NotSupportedException) { }
            }
            // Do not catch IO failures as 'unsupported': disk failures must propagate.
            File.Copy(path, backup, true);
            WriteStage("after-backup");
        }
        if (File.Exists(path)) File.Delete(path);
        WriteStage("after-main-remove");
        File.Move(temp, path);
        WriteStage("after-main-move");
    }
    private void DeleteManaged(string path)
    {
        foreach (string owned in new[] { path, path + ".tmp", path + ".bak" })
        {
            CheckLinks(owned);
            if (File.Exists(owned)) File.Delete(owned);
        }
    }
}
