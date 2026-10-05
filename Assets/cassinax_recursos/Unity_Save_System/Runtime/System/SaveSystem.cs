// Cassinax Unity System Save - v1.0.0
// Nucleo: estado, configuracao e registro de migracoes. Orquestracao de dono unico na thread principal.
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
    public partial class SaveSystem
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
    }
}
