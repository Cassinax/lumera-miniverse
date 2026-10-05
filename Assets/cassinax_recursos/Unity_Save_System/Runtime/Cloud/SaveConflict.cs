// Cassinax Unity System Save - v1.0.0
// Conflito local/remoto: candidatos, decisao, resolvedor padrao e prompt de UI.
using System;
namespace cassinax.savesystem
{
    public enum SaveConflictSource { Local, Cloud, Platform, ManualImport }
    public enum SaveConflictDecision { KeepLocal, UseIncoming, Merge, AskUser, RejectIncoming }
    public struct SaveConflictCandidate
    {
        public string payload;
        public SaveConflictSource source;
        public DateTime updatedAtUtc;
        public int structureVersion;
        public bool isValid;
        public string identity, revision, parentRevision, baseRevision, contentHash;
        public long epoch;
        public bool tombstone;
        public static SaveConflictCandidate From(ValidatedSaveSnapshot snapshot, string payload, SaveConflictSource source)
        {
            return new SaveConflictCandidate { payload = payload, source = source, isValid = snapshot != null,
                structureVersion = snapshot?.SchemaVersion ?? 0, identity = snapshot?.Identity,
                revision = snapshot?.Revision, parentRevision = snapshot?.ParentRevision,
                baseRevision = snapshot?.BaseRevision, epoch = snapshot?.Epoch ?? 0, tombstone = snapshot?.IsTombstone ?? false };
        }
    }
    public interface ISaveConflictResolver { SaveConflictDecision Resolve(SaveConflictCandidate local, SaveConflictCandidate incoming); }
    public class DefaultSaveConflictResolver : ISaveConflictResolver
    {
        public SaveConflictDecision Resolve(SaveConflictCandidate local, SaveConflictCandidate incoming)
        {
            if (!incoming.isValid) return SaveConflictDecision.RejectIncoming;
            if (!local.isValid) return SaveConflictDecision.UseIncoming;
            if (local.identity != incoming.identity) return SaveConflictDecision.RejectIncoming;
            if (incoming.epoch < local.epoch) return SaveConflictDecision.RejectIncoming;
            if (incoming.epoch > local.epoch) return SaveConflictDecision.UseIncoming;
            if (!string.IsNullOrEmpty(local.revision) && local.revision == incoming.revision)
                return !string.IsNullOrEmpty(local.contentHash) && local.contentHash == incoming.contentHash
                    ? SaveConflictDecision.KeepLocal : SaveConflictDecision.AskUser;
            if (!string.IsNullOrEmpty(local.revision) && (incoming.parentRevision == local.revision ||
                local.baseRevision == local.revision)) return SaveConflictDecision.UseIncoming;
            if (!string.IsNullOrEmpty(incoming.revision) &&
                (local.parentRevision == incoming.revision || local.baseRevision == incoming.revision))
                return SaveConflictDecision.KeepLocal;
            return SaveConflictDecision.AskUser;
        }
    }
    public sealed class SaveConflictPrompt
    {
        public SaveConflictCandidate Local { get; }
        public SaveConflictCandidate Incoming { get; }
        private Action<SaveConflictDecision> _answer;
        internal SaveConflictPrompt(SaveConflictCandidate local, SaveConflictCandidate incoming, Action<SaveConflictDecision> answer)
        { Local = local; Incoming = incoming; _answer = answer; }
        public void ChooseLocal() { Answer(SaveConflictDecision.KeepLocal); }
        public void ChooseIncoming() { Answer(SaveConflictDecision.UseIncoming); }
        public void Defer() { Answer(SaveConflictDecision.AskUser); }
        private void Answer(SaveConflictDecision decision) { var callback = _answer; _answer = null; callback?.Invoke(decision); }
    }
}
