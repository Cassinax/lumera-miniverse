// Cassinax Unity System Save - v1.0.0
// Reset de progresso, vinculo de identidade, epoca e erros.
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
