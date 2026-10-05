// Cassinax Unity System Save - v1.0.0
// Snapshot confirmado, impressao de progresso e commit interno.
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
    }
}
