// Cassinax Unity System Save - v0.10.0
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace cassinax.savesystem
{
    public enum PriorityLevel { Baixo = 1, Normal = 2, Alto = 3 }
    public enum SaveImportMode { ReplaceSnapshot, Merge }
    public enum SaveScope { LocalPreference, Progress, Identity, Metadata, Audit }
    public enum SaveExportPurpose { Manual, Cloud, Backup }
    public enum SaveError { None, InvalidFormat, Integrity, WrongGame, FutureCore, FutureSchema,
        DuplicateField, LimitExceeded, MigrationRequired, MigrationFailed, Storage, IdentityMismatch,
        StaleEpoch, Busy, InvalidConfiguration, StaleOperation, Unavailable, Conflict, Unknown }
    public sealed class SaveValidationException : Exception
    {
        public SaveError Error { get; }
        public SaveValidationException(SaveError error) : base(error.ToString()) { Error = error; }
    }
    public struct ExportFilter
    {
        public bool AllData;
        public List<string> ComandosIncluir, ComandosExcluir, PrefixosChave;
    }
    public static class SaveHeaders
    {
        public const string INICIO = "0001", ID_JOGO = "IDJogo", VERSAO_JOGO = "versaoJogo",
            VERSAO_ESTRUTURA = "versaoEstruturaSave", VERSAO_CORE = "versaoCore",
            DATA_CRIACAO = "dataCriacao", DATA_EXPORTACAO = "dataExportacao",
            DATA_ATUALIZACAO = "dataAtualizacao", PLATAFORMA = "plataformaOrigem",
            TIPO_SAVE = "tipoSave", HASH = "hashVerificacao";
    }
    public interface ISaveStorageAdapter
    {
        bool HasMainSave();
        string LoadMainSave();
        void SaveMainSave(string payload);
        bool HasBackup(string backupName);
        string LoadBackup(string backupName);
        void SaveBackup(string backupName, string payload);
        void DeleteBackup(string backupName);
        void DeleteAll();
    }
    public interface IValidatedSaveStorage
    {
        void ConfigureValidation(Func<string, bool> validator, int maxPayloadBytes);
    }
    public sealed class ValidatedSaveSnapshot
    {
        internal readonly object Issuer;
        public IReadOnlyDictionary<string, string> Data { get; }
        public string GameId { get; }
        public int CoreVersion { get; }
        public int SchemaVersion { get; }
        public string Identity { get; }
        public string Revision { get; }
        public string ParentRevision { get; }
        public string BaseRevision { get; }
        public long Epoch { get; }
        public bool IsTombstone { get; }
        public bool IsLegacy { get; }
        public string ProgressUpdatedAt { get; }
        internal ValidatedSaveSnapshot(object issuer, Dictionary<string, string> data, int core, bool legacy)
        {
            Issuer = issuer;
            Data = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(data));
            GameId = SaveSystem.Value(data, SaveSystem.Header("IDJogo"));
            SchemaVersion = int.Parse(data[SaveSystem.Header("versaoEstruturaSave")], CultureInfo.InvariantCulture);
            CoreVersion = core;
            Identity = SaveSystem.Owner(data);
            Revision = SaveSystem.Value(data, SaveSystem.Meta("revision"));
            ParentRevision = SaveSystem.Value(data, SaveSystem.Meta("parent"));
            BaseRevision = SaveSystem.Value(data, SaveSystem.LocalMeta("baseRevision"));
            Epoch = long.Parse(SaveSystem.Value(data, SaveSystem.Meta("epoch"), "0"), CultureInfo.InvariantCulture);
            IsTombstone = SaveSystem.Value(data, SaveSystem.Meta("tombstone")) == "true";
            IsLegacy = legacy;
            ProgressUpdatedAt = SaveSystem.Value(data, SaveSystem.Header("dataAtualizacao"));
        }
    }

    // Single-owner/main-thread orchestration. No provider callbacks may mutate this from worker threads.
    public class SaveSystem
    {
        private readonly SaveCore _core;
        private readonly ISaveStorageAdapter _storage;
        private readonly Dictionary<int, string> _legacyKeys;
        private readonly List<KeyValuePair<string, string>> _pending = new List<KeyValuePair<string, string>>();
        private readonly Dictionary<int, Func<IReadOnlyDictionary<string, string>, IDictionary<string, string>>> _migrations =
            new Dictionary<int, Func<IReadOnlyDictionary<string, string>, IDictionary<string, string>>>();
        private Dictionary<string, string> _transaction;
        private string _gameId;
        private int _schema = 1;
        private Func<string, SaveScope> _scope = DefaultScope;
        private string _persistedPlain;
        private Func<string, int, string> _legacyMigration;
        public bool AllowUnsignedLegacyLocal { get; set; }
        public SaveError LastError { get; private set; }
        public event Action<SaveError> ErroTipado;
        public event Action<ValidatedSaveSnapshot> ProgressoAlterado;
        public event Action<ValidatedSaveSnapshot> ProgressoAplicado;
        public int CoreVersion => _core.CoreVersion;
        public int AltoBatchSize { get; set; } = 10;
        public int NormalBatchSize { get; set; } = 5;
        public int BaixoBatchSize { get; set; } = 2;
        public string Revision => Get(Meta("revision")) ?? "";
        public long Epoch => long.TryParse(Get(Meta("epoch")), out var e) ? e : 0;
        public SaveSystem(SaveCore core, ISaveStorageAdapter storage, Dictionary<int, string> legacyKeys = null)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _legacyKeys = legacyKeys ?? new Dictionary<int, string>();
        }
        public void Configure(string gameId, int schema, Func<string, SaveScope> scope = null)
        {
            if (string.IsNullOrWhiteSpace(gameId) || gameId == "CHANGE_ME" || gameId.Contains("SEU_") || schema < 1 ||
                (_gameId != null && (_gameId != gameId || _schema != schema)))
                throw new SaveValidationException(SaveError.InvalidConfiguration);
            _gameId = gameId;
            _schema = schema;
            _scope = scope ?? DefaultScope;
            if (_storage is IValidatedSaveStorage validated)
                validated.ConfigureValidation(p => Validate(p, _gameId, _schema, true, out _, out _), _core.Limits.MaxPayloadBytes);
        }
        public void RegisterMigration(int fromSchema, Func<IReadOnlyDictionary<string, string>, IDictionary<string, string>> migration)
        {
            if (fromSchema < 1 || migration == null || _migrations.ContainsKey(fromSchema)) throw new ArgumentException("Invalid migration.");
            _migrations.Add(fromSchema, migration);
        }
        [Obsolete("RegisterMigration provides explicit one-step schema migrations. Core codec migration is automatic.")]
        public void SetMigrationCallbacks(Func<string, int, string> migrateCoreFormat, Func<string, int, string> migrateGameData)
        {
            if (migrateCoreFormat != null) throw new NotSupportedException("Register a schema migration instead.");
            _legacyMigration = migrateGameData;
        }
        public static string Header(string field) => "[SLG0001],[" + field + "]";
        public static string Meta(string field) => "[CUSS],[" + field + "]";
        public static string LocalMeta(string field) => "[CUSSL],[" + field + "]";
        internal static string Value(IDictionary<string, string> data, string key, string fallback = "") => data.TryGetValue(key, out var v) ? v : fallback;
        internal static string Owner(IDictionary<string, string> data)
        {
            string owner = Value(data, "[SLG0003],[accountHash]");
            return owner == "nao_vinculado" ? "" : owner;
        }
        public static SaveScope DefaultScope(string key)
        {
            if (key.StartsWith("[SLG0003],", StringComparison.Ordinal)) return SaveScope.Identity;
            if (key.StartsWith("[SLG0200],", StringComparison.Ordinal)) return SaveScope.Audit;
            if (key.StartsWith("[CUSS],", StringComparison.Ordinal) || IsHeader(key)) return SaveScope.Metadata;
            if (key.StartsWith("[SLG0002],", StringComparison.Ordinal)) return SaveScope.Progress;
            return SaveScope.LocalPreference;
        }
        private static bool IsHeader(string key) => new[] { "0001", "IDJogo", "versaoJogo", "versaoEstruturaSave",
            "versaoCore", "dataCriacao", "dataExportacao", "dataAtualizacao", "plataformaOrigem", "tipoSave", "hashVerificacao" }
            .Any(f => key == Header(f));
        public SaveScope ScopeOf(string key)
        {
            if (key.StartsWith("[CUSS],", StringComparison.Ordinal) || IsHeader(key)) return SaveScope.Metadata;
            if (key.StartsWith("[CUSSL],", StringComparison.Ordinal)) return SaveScope.LocalPreference;
            if (key.StartsWith("[SLG0003],", StringComparison.Ordinal)) return SaveScope.Identity;
            var scope = _scope(key);
            return scope == SaveScope.Metadata ? SaveScope.LocalPreference : scope;
        }
        private void RequireConfiguration()
        {
            if (_gameId == null) throw new SaveValidationException(SaveError.InvalidConfiguration);
        }
        public void Enqueue(string key, string value, PriorityLevel priority)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (_transaction != null) _transaction[key] = value;
            else _pending.Add(new KeyValuePair<string, string>(key, value));
        }
        public void EnqueueRemove(string key, PriorityLevel priority)
        {
            if (_transaction != null) _transaction.Remove(key);
            else _pending.Add(new KeyValuePair<string, string>(key, null));
        }
        public void Begin()
        {
            if (_transaction != null) throw new SaveValidationException(SaveError.Busy);
            _transaction = _core.Snapshot();
            ApplyPending(_transaction);
        }
        public bool Commit()
        {
            if (_transaction == null) throw new InvalidOperationException("Begin required.");
            var staged = _transaction;
            _transaction = null;
            if (!CommitData(staged, false, true)) { _transaction = staged; return false; }
            return true;
        }
        public void Rollback() { _transaction = null; }
        public void ProcessQueues()
        {
            if (_transaction != null || _pending.Count == 0) return;
            var staged = _core.Snapshot();
            ApplyPending(staged);
            CommitData(staged, false, true);
        }
        private void ApplyPending(Dictionary<string, string> target)
        {
            foreach (var op in _pending)
                if (op.Value == null) target.Remove(op.Key); else target[op.Key] = op.Value;
        }
        public bool HasPendingOperations() => _pending.Count > 0;
        public void DiscardPendingOperations() { _pending.Clear(); _transaction = null; }
        public string Get(string key) => _core.Get(key);
        public bool ContainsKey(string key) => _core.ContainsKey(key);
        public List<string> GetAllKeys() => _core.GetAllKeys();
        public bool HasMainSave() => _storage.HasMainSave();
        public ValidatedSaveSnapshot CapturarSnapshot()
        {
            RequireConfiguration();
            var data = _core.Snapshot();
            FillHeader(data);
            return new ValidatedSaveSnapshot(this, data, CoreVersion, false);
        }
        public string ProgressFingerprint(ValidatedSaveSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Issuer != this) throw new ArgumentException("Validated snapshot required.");
            return _core.ComputeHash(_core.Serialize(snapshot.Data.Where(p => ScopeOf(p.Key) == SaveScope.Progress)
                .ToDictionary(p => p.Key, p => p.Value)));
        }
        private void FillHeader(Dictionary<string, string> data)
        {
            RequireConfiguration();
            data[Header("IDJogo")] = _gameId;
            data[Header("versaoEstruturaSave")] = _schema.ToString(CultureInfo.InvariantCulture);
            data[Header("versaoCore")] = CoreVersion.ToString(CultureInfo.InvariantCulture);
            if (!data.ContainsKey(Header("0001"))) data[Header("0001")] = "sucesso";
            if (!data.ContainsKey(Meta("epoch"))) data[Meta("epoch")] = "0";
            if (!data.ContainsKey(Meta("revision"))) data[Meta("revision")] = "";
            if (!data.ContainsKey(Meta("parent"))) data[Meta("parent")] = "";
            if (!data.ContainsKey(Meta("tombstone"))) data[Meta("tombstone")] = "false";
            // Unknown historical dates and revisions remain unknown; no fabricated audit history.
            if (!data.ContainsKey(Header("dataCriacao"))) data[Header("dataCriacao")] = "";
            if (!data.ContainsKey(Header("dataAtualizacao"))) data[Header("dataAtualizacao")] = "";
            data.Remove(Header("hashVerificacao"));
        }
        private bool FactsEqual(IDictionary<string, string> a, IDictionary<string, string> b)
        {
            return a.Where(p => ScopeOf(p.Key) == SaveScope.Progress).OrderBy(p => p.Key, StringComparer.Ordinal)
                .SequenceEqual(b.Where(p => ScopeOf(p.Key) == SaveScope.Progress).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
        private bool CommitData(Dictionary<string, string> staged, bool imported, bool clearPending, bool reset = false)
        {
            try
            {
                RequireConfiguration();
                var old = _core.Snapshot();
                bool changed = !FactsEqual(old, staged) || reset;
                FillHeader(staged);
                if (!imported && changed)
                {
                    staged[Meta("parent")] = Revision;
                    staged[Meta("revision")] = Guid.NewGuid().ToString("N");
                    staged[Meta("tombstone")] = reset ? "true" : "false";
                    staged[Header("dataAtualizacao")] = DateTime.UtcNow.ToString("O");
                    if (string.IsNullOrEmpty(staged[Header("dataCriacao")])) staged[Header("dataCriacao")] = staged[Header("dataAtualizacao")];
                }
                string plain = _core.Serialize(staged);
                if (plain != _persistedPlain)
                {
                    _storage.SaveMainSave(Encode(staged));
                    _persistedPlain = plain;
                }
                _core.Replace(staged);
                if (clearPending) _pending.Clear();
                LastError = SaveError.None;
                var snapshot = new ValidatedSaveSnapshot(this, staged, CoreVersion, false);
                if (changed) Notify(ProgressoAlterado, snapshot);
                if (imported) Notify(ProgressoAplicado, snapshot);
                return true;
            }
            catch (Exception ex) { return Fail(Classify(ex)); }
        }
        private static void Notify(Action<ValidatedSaveSnapshot> handlers, ValidatedSaveSnapshot snapshot)
        {
            if (handlers == null) return;
            foreach (Action<ValidatedSaveSnapshot> handler in handlers.GetInvocationList())
                try { handler(snapshot); } catch { /* Observers cannot undo a durable commit. */ }
        }
        public void ForceSave()
        {
            if (_transaction != null) throw new SaveValidationException(SaveError.Busy);
            if (!CommitData(_core.Snapshot(), false, false)) throw new SaveValidationException(LastError);
        }
        public void LoadFromStorage()
        {
            RequireConfiguration();
            if (!_storage.HasMainSave()) return;
            string payload = _storage.LoadMainSave();
            if (!Validate(payload, _gameId, _schema, true, out var snapshot, out var error)) throw new SaveValidationException(error);
            _core.Replace(snapshot.Data.ToDictionary(p => p.Key, p => p.Value));
            DiscardPendingOperations();
            _persistedPlain = snapshot.IsLegacy ? null : _core.SaveToPlainText();
        }
        public void LoadFromString(string data)
        {
            RequireConfiguration();
            if (!Importar(data, _gameId, _schema)) throw new SaveValidationException(LastError);
        }
        public string Exportar(string idJogo, string versaoJogo, int versaoEstrutura, ExportFilter? filtro, string plataforma)
        {
            if (_gameId == null) Configure(idJogo, versaoEstrutura);
            if (_gameId != idJogo || _schema != versaoEstrutura) throw new SaveValidationException(SaveError.WrongGame);
            return ExportSnapshot(SaveExportPurpose.Manual, filtro, versaoJogo, plataforma);
        }
        public string ExportSnapshot(SaveExportPurpose purpose, ExportFilter? filter = null, string gameVersion = "", string platform = "")
        {
            RequireConfiguration();
            var data = _core.Snapshot();
            foreach (var key in data.Keys.ToList())
            {
                SaveScope scope = ScopeOf(key);
                bool include = purpose == SaveExportPurpose.Backup || scope == SaveScope.Progress || scope == SaveScope.Audit ||
                    (purpose == SaveExportPurpose.Manual && scope == SaveScope.Identity);
                if (scope == SaveScope.Metadata) include = true;
                if (include && scope == SaveScope.Progress && filter.HasValue && !filter.Value.AllData)
                {
                    var f = filter.Value;
                    if (f.ComandosIncluir != null && f.ComandosIncluir.Count > 0) include = f.ComandosIncluir.Any(c => key.StartsWith("[" + c + "],", StringComparison.Ordinal));
                    if (f.ComandosExcluir != null && f.ComandosExcluir.Any(c => key.StartsWith("[" + c + "],", StringComparison.Ordinal))) include = false;
                    if (f.PrefixosChave != null && f.PrefixosChave.Count > 0) include &= f.PrefixosChave.Any(p => key.StartsWith(p, StringComparison.Ordinal));
                }
                if (!include) data.Remove(key);
            }
            // Cloud transmits only the pseudonymous ownership field, never generic identity fields.
            if (purpose == SaveExportPurpose.Cloud)
            {
                string owner = Owner(_core.Snapshot());
                if (!string.IsNullOrEmpty(owner)) data["[SLG0003],[accountHash]"] = owner;
            }
            FillHeader(data);
            data[Header("versaoJogo")] = gameVersion;
            data[Header("plataformaOrigem")] = platform;
            data[Header("tipoSave")] = filter.HasValue && !filter.Value.AllData ? "filtered" : purpose.ToString();
            return Encode(data);
        }
        private string Encode(Dictionary<string, string> data)
        {
            FillHeader(data);
            string body = "[2]" + _core.Encrypt(_core.Serialize(data));
            string result = body + "." + _core.ComputeIntegrityHash(body);
            if (Encoding.UTF8.GetByteCount(result) > _core.Limits.MaxPayloadBytes) throw new SaveValidationException(SaveError.LimitExceeded);
            return result;
        }
        public bool TryValidateSnapshot(string payload, string expectedGame, int schema, out ValidatedSaveSnapshot snapshot, out SaveError error)
            => Validate(payload, expectedGame, schema, false, out snapshot, out error);
        private bool Validate(string payload, string expectedGame, int schema, bool local, out ValidatedSaveSnapshot snapshot, out SaveError error)
        {
            snapshot = null;
            error = SaveError.None;
            try
            {
                if (string.IsNullOrEmpty(payload)) throw new SaveValidationException(SaveError.InvalidFormat);
                if (payload.Length > _core.Limits.MaxPayloadBytes || Encoding.UTF8.GetByteCount(payload) > _core.Limits.MaxPayloadBytes)
                    throw new SaveValidationException(SaveError.LimitExceeded);
                int end = payload.IndexOf(']');
                if (payload[0] != '[' || end < 2 || end > 10 || !int.TryParse(payload.Substring(1, end - 1), out int core) || core < 1)
                    throw new SaveValidationException(SaveError.InvalidFormat);
                if (core > CoreVersion) throw new SaveValidationException(SaveError.FutureCore);
                _legacyKeys.TryGetValue(core, out var legacyKey);
                string cipher = payload.Substring(end + 1);
                string plain;
                if (core == 2)
                {
                    int dot = cipher.LastIndexOf('.');
                    if (dot < 0) throw new SaveValidationException(SaveError.Integrity);
                    string body = payload.Substring(0, end + 1 + dot);
                    if (!SaveCore.HashEquals(_core.ComputeIntegrityHash(body), cipher.Substring(dot + 1)))
                        throw new SaveValidationException(SaveError.Integrity);
                    plain = _core.Decrypt(cipher.Substring(0, dot));
                    if (!plain.StartsWith("CUSS2\n", StringComparison.Ordinal)) throw new SaveValidationException(SaveError.InvalidFormat);
                }
                else plain = legacyKey == null ? _core.Decrypt(cipher) : _core.DecryptCompativel(cipher, legacyKey);
                var data = _core.Parse(plain);
                if (core == 1)
                {
                    string hash = Value(data, Header("hashVerificacao"));
                    bool unsigned = hash == "" || hash == "local-nao-exportado";
                    if (!(unsigned && local && AllowUnsignedLegacyLocal))
                    {
                        string unsignedText = Regex.Replace(plain, @"(?m)^\[SLG0001\],?\[hashVerificacao\]\[[^\]]*\]\r?\n?", "");
                        string computed = legacyKey == null ? _core.ComputeIntegrityHash(unsignedText) : _core.ComputeIntegrityHash(unsignedText, legacyKey);
                        if (!SaveCore.HashEquals(computed, hash)) throw new SaveValidationException(SaveError.Integrity);
                    }
                }
                if (Value(data, Header("IDJogo")) != expectedGame) throw new SaveValidationException(SaveError.WrongGame);
                if (!int.TryParse(Value(data, Header("versaoEstruturaSave")), out int version) || version < 1)
                    throw new SaveValidationException(SaveError.InvalidFormat);
                if (version > schema) throw new SaveValidationException(SaveError.FutureSchema);
                if (int.TryParse(Value(data, Header("versaoCore")), out int headerCore) && headerCore > CoreVersion)
                    throw new SaveValidationException(SaveError.FutureCore);
                if (core == 2 && headerCore != core) throw new SaveValidationException(SaveError.InvalidFormat);
                string identity = Owner(data);
                while (version < schema)
                {
                    if (_migrations.TryGetValue(version, out var migrate))
                    {
                        var readOnly = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(data));
                        data = new Dictionary<string, string>(migrate(readOnly), StringComparer.Ordinal);
                        version++;
                    }
                    else if (_legacyMigration != null)
                    {
                        data = _core.Parse(_legacyMigration(_core.Serialize(data), version));
                        version = schema;
                    }
                    else throw new SaveValidationException(SaveError.MigrationRequired);
                    if (Owner(data) != identity || Value(data, Header("IDJogo")) != expectedGame)
                        throw new SaveValidationException(SaveError.MigrationFailed);
                    _core.Serialize(data);
                }
                data[Header("versaoEstruturaSave")] = schema.ToString(CultureInfo.InvariantCulture);
                data.Remove(Header("hashVerificacao"));
                if (!data.ContainsKey(Meta("epoch"))) data[Meta("epoch")] = "0";
                if (!long.TryParse(data[Meta("epoch")], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch) || epoch < 0)
                    throw new SaveValidationException(SaveError.InvalidFormat);
                foreach (var key in new[] { Meta("revision"), Meta("parent"), LocalMeta("baseRevision") })
                    if (Value(data, key) != "" && !Guid.TryParseExact(data[key], "N", out _)) throw new SaveValidationException(SaveError.InvalidFormat);
                if (Value(data, Meta("tombstone")) != "" && Value(data, Meta("tombstone")) != "true" && Value(data, Meta("tombstone")) != "false")
                    throw new SaveValidationException(SaveError.InvalidFormat);
                if (Value(data, Meta("tombstone")) == "true" && data.Any(p => ScopeOf(p.Key) == SaveScope.Progress))
                    throw new SaveValidationException(SaveError.InvalidFormat);
                snapshot = new ValidatedSaveSnapshot(this, data, core, core == 1);
                return true;
            }
            catch (Exception ex) { error = Classify(ex); return false; }
        }
        public bool Importar(string payload, string expectedGame, int schema)
            => Importar(payload, expectedGame, schema, SaveImportMode.ReplaceSnapshot);
        public bool Importar(string payload, string expectedGame, int schema, SaveImportMode mode)
        {
            if (_gameId == null) Configure(expectedGame, schema);
            if (!TryValidateSnapshot(payload, expectedGame, schema, out var snapshot, out var error)) return Fail(error);
            return AplicarSnapshotTransacional(snapshot, mode);
        }
        public bool AplicarSnapshotTransacional(ValidatedSaveSnapshot snapshot, SaveImportMode mode = SaveImportMode.ReplaceSnapshot, bool restoreLocalPreferences = false)
        {
            if (_transaction != null) return Fail(SaveError.Busy);
            if (snapshot == null || snapshot.Issuer != this || snapshot.GameId != _gameId) return Fail(SaveError.InvalidFormat);
            if (snapshot.SchemaVersion != _schema) return Fail(SaveError.FutureSchema);
            if (mode != SaveImportMode.Merge && mode != SaveImportMode.ReplaceSnapshot) return Fail(SaveError.InvalidConfiguration);
            if (restoreLocalPreferences && (!snapshot.Data.TryGetValue(Header("tipoSave"), out var purpose) || purpose != "Backup"))
                return Fail(SaveError.InvalidConfiguration);
            if (snapshot.Epoch < Epoch) return Fail(SaveError.StaleEpoch);
            var old = _core.Snapshot();
            string owner = Owner(old);
            if (owner != "" && owner != snapshot.Identity) return Fail(SaveError.IdentityMismatch);
            if (mode == SaveImportMode.ReplaceSnapshot && snapshot.Data.TryGetValue(Header("tipoSave"), out var kind) && kind == "filtered")
                return Fail(SaveError.InvalidFormat);
            var staged = mode == SaveImportMode.Merge ? old : restoreLocalPreferences ? new Dictionary<string, string>() :
                old.Where(p => ScopeOf(p.Key) == SaveScope.LocalPreference).ToDictionary(p => p.Key, p => p.Value);
            foreach (var pair in snapshot.Data)
                if (restoreLocalPreferences || ScopeOf(pair.Key) != SaveScope.LocalPreference) staged[pair.Key] = pair.Value;
            if (mode == SaveImportMode.Merge)
            {
                if (snapshot.Epoch != Epoch || snapshot.IsTombstone) return Fail(SaveError.StaleEpoch);
                staged[Meta("revision")] = Revision;
                staged[Meta("parent")] = Get(Meta("parent")) ?? "";
                return CommitData(staged, false, true);
            }
            return CommitData(staged, true, true);
        }
        [Obsolete("Diagnostic only. Use TryValidateSnapshot before trusting data.")]
        public bool TryPreviewPlainText(string payload, out string plainText, out string error)
        {
            plainText = "";
            error = "";
            if (!TryValidateSnapshot(payload, _gameId, _schema, out var snapshot, out var failure)) { error = failure.ToString(); return false; }
            plainText = _core.Serialize(snapshot.Data.ToDictionary(p => p.Key, p => p.Value));
            return true;
        }
        public string CriarBackupManual() { var p = ExportSnapshot(SaveExportPurpose.Backup); _storage.SaveBackup("Backup Manual", p); return p; }
        public void CriarBackupSeguranca() { _storage.SaveBackup("Backup Seguranca", ExportSnapshot(SaveExportPurpose.Backup)); }
        public void RemoverBackupSeguranca() { _storage.DeleteBackup("Backup Seguranca"); }
        public bool RestaurarBackupSeguranca() => RestoreBackup("Backup Seguranca");
        public bool RestaurarBackupAutomatico() => RestoreBackup("Backup Auto");
        public bool RestaurarBackupManual() => RestoreBackup("Backup Manual");
        private bool RestoreBackup(string name)
        {
            try
            {
                if (!_storage.HasBackup(name)) return false;
                if (!Validate(_storage.LoadBackup(name), _gameId, _schema, true, out var s, out var error)) return Fail(error);
                return AplicarSnapshotTransacional(s);
            }
            catch (Exception ex) { return Fail(Classify(ex)); }
        }
        public string ExportarParaNuvem() => ExportSnapshot(SaveExportPurpose.Cloud);
        public void ImportarDaNuvem(string data) { LoadFromString(data); }
        public bool ResetProgress(string resetRequestId)
        {
            if (string.IsNullOrWhiteSpace(resetRequestId)) return Fail(SaveError.InvalidConfiguration);
            if (Get(LocalMeta("resetRequest")) == resetRequestId) return true;
            var staged = _core.Snapshot().Where(p => ScopeOf(p.Key) == SaveScope.LocalPreference ||
                ScopeOf(p.Key) == SaveScope.Identity || ScopeOf(p.Key) == SaveScope.Metadata).ToDictionary(p => p.Key, p => p.Value);
            staged[Meta("epoch")] = checked(Epoch + 1).ToString(CultureInfo.InvariantCulture);
            staged[LocalMeta("resetRequest")] = resetRequestId;
            staged[LocalMeta("baseRevision")] = Revision;
            return CommitData(staged, false, true, true);
        }
        public bool BindIdentity(string identity, string provider, IDictionary<string, string> defaults,
            bool resetAnonymousProgress, bool explicitlyResetOtherAccount = false, string visibleEmail = "")
        {
            if (_transaction != null || HasPendingOperations()) return Fail(SaveError.Busy);
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(provider)) return Fail(SaveError.InvalidConfiguration);
            var staged = _core.Snapshot();
            string owner = Owner(staged);
            bool switching = owner != "" && owner != identity;
            if (switching && !explicitlyResetOtherAccount) return Fail(SaveError.IdentityMismatch);
            var initial = defaults ?? new Dictionary<string, string>();
            bool progress = staged.Any(p => ScopeOf(p.Key) == SaveScope.Progress &&
                (!initial.TryGetValue(p.Key, out var value) || value != p.Value));
            if (owner == "" && progress && !resetAnonymousProgress) return Fail(SaveError.IdentityMismatch);
            if (switching || (owner == "" && progress))
            {
                foreach (string key in staged.Keys.ToList())
                    if (ScopeOf(key) == SaveScope.Progress || ScopeOf(key) == SaveScope.Audit ||
                        ScopeOf(key) == SaveScope.Identity || key.StartsWith("[CUSS],", StringComparison.Ordinal) ||
                        key.StartsWith("[CUSSL],", StringComparison.Ordinal)) staged.Remove(key);
                foreach (var pair in initial)
                    if (ScopeOf(pair.Key) == SaveScope.Progress) staged[pair.Key] = pair.Value;
            }
            staged["[SLG0003],[accountHash]"] = identity;
            staged["[SLG0003],[accountProvider]"] = provider;
            staged["[SLG0003],[accountEmail]"] = visibleEmail ?? "";
            staged["[SLG0003],[accountStatus]"] = "conectado";
            return CommitData(staged, false, true);
        }
        public string CreateResetPayload(long observedRemoteEpoch, string requestId)
        {
            RequireConfiguration();
            if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("Reset request ID required.");
            var staged = _core.Snapshot().Where(p => ScopeOf(p.Key) == SaveScope.Metadata)
                .ToDictionary(p => p.Key, p => p.Value);
            string owner = Owner(_core.Snapshot());
            if (owner != "") staged["[SLG0003],[accountHash]"] = owner;
            staged[Meta("epoch")] = checked(Math.Max(Epoch, observedRemoteEpoch) + 1).ToString(CultureInfo.InvariantCulture);
            staged[Meta("revision")] = Guid.NewGuid().ToString("N");
            staged[Meta("parent")] = Revision;
            staged[Meta("tombstone")] = "true";
            staged[Meta("resetRequest")] = requestId;
            return Encode(staged);
        }
        public bool ConfirmSynced(string revision, long epoch)
        {
            if (epoch != Epoch || revision != Revision) return Fail(SaveError.StaleOperation);
            var data = _core.Snapshot();
            data[LocalMeta("baseRevision")] = revision;
            return CommitData(data, false, false);
        }
        public void ApagarTudo()
        {
            DiscardPendingOperations();
            try { _storage.DeleteAll(); _core.Clear(); _persistedPlain = null; LastError = SaveError.None; }
            catch (Exception ex) { Fail(Classify(ex)); throw; }
        }
        private bool Fail(SaveError error)
        {
            LastError = error;
            if (ErroTipado != null) foreach (Action<SaveError> handler in ErroTipado.GetInvocationList()) try { handler(error); } catch { }
            return false;
        }
        private static SaveError Classify(Exception ex) => ex is SaveValidationException validation ? validation.Error :
            ex is IOException && !(ex is InvalidDataException) ? SaveError.Storage : SaveError.InvalidFormat;
    }
}
