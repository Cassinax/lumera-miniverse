// Cassinax Unity System Save - v1.0.0
// Fila por prioridade, transacoes e leitura do estado confirmado.
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
    }
}
