// Cassinax Unity System Save - v1.0.0
// Tipos de apoio do SaveSystem: enums, erros, filtros, contratos de storage e snapshot validado.
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
}
