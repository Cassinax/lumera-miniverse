// Cassinax Unity System Save - v1.0.0
// Importacao, aplicacao transacional de snapshot e previa de texto.
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
    }
}
