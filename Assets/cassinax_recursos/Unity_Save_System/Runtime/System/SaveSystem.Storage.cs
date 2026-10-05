// Cassinax Unity System Save - v1.0.0
// Gravacao, carga, backups e atalhos de nuvem.
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
    }
}
