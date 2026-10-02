// Cassinax Unity System Save - v0.10.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
    public interface ISaveConflictResolver { SaveConflictDecision Resolve(SaveConflictCandidate local, SaveConflictCandidate incoming); }
    public struct PlatformPurchaseReceipt
    {
        public string productId, transactionId, source;
        public bool owned;
        public PlatformPurchaseReceipt(string productId, string transactionId, string source, bool owned = true)
        { this.productId = productId; this.transactionId = transactionId; this.source = source; this.owned = owned; }
    }
    public interface IPlatformPurchaseAdapter { IEnumerable<PlatformPurchaseReceipt> GetOwnedPurchases(); }
    // Legacy transports remain source compatible. They do not imply session isolation or conditional writes.
    public interface ICloudSaveAdapter
    {
        bool IsAvailable { get; }
        IDictionary<string, string> DownloadSaveItems(string slotId);
        void UploadSaveItems(string slotId, IDictionary<string, string> items);
    }
    public interface IAsyncCloudSaveAdapter
    {
        bool IsAvailable { get; }
        void UploadSave(string slotId, string payload, Action<CloudSaveResult> onComplete);
        void DownloadSave(string slotId, Action<CloudSaveResult> onComplete);
        void DeleteSave(string slotId, Action<CloudSaveResult> onComplete);
    }
    public enum CloudSaveStatus { Unknown, SdkSuccess, Error, Conflict, Unavailable, UserCancelled, WaitCancelled, Timeout }
    public struct CloudSaveResult
    {
        public bool Success;
        public string Payload, Error;
        public long StatusCode;
        public int StructureVersion, CoreVersion;
        public CloudSaveStatus Status;
        public string ProviderRevision;
        public object Context;
        public bool HasPayload => !string.IsNullOrEmpty(Payload);
        public static CloudSaveResult Ok(string payload = null, long statusCode = 200)
            => new CloudSaveResult { Success = true, Status = CloudSaveStatus.SdkSuccess, Payload = payload, StatusCode = statusCode };
        public static CloudSaveResult Fail(string error, long statusCode = 0)
            => new CloudSaveResult { Success = false, Status = CloudSaveStatus.Error, Error = error, StatusCode = statusCode };
        public static CloudSaveResult Outcome(CloudSaveStatus status)
            => new CloudSaveResult { Status = status, Success = status == CloudSaveStatus.SdkSuccess, Error = status.ToString() };
    }
    public sealed class CloudSaveSession
    {
        public string Provider { get; }
        public string Identity { get; }
        public string SessionId { get; }
        public CloudSaveSession(string provider, string identity, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Authenticated session required.");
            Provider = provider; Identity = identity; SessionId = sessionId;
        }
        public bool Matches(CloudSaveSession other) => other != null && other.Provider == Provider &&
            other.Identity == Identity && other.SessionId == SessionId;
    }
    public enum CloudSaveOperation { Read, Commit, PhysicalDelete }
    public sealed class CloudSaveRequest
    {
        public CloudSaveSession Session { get; }
        public string Slot { get; }
        public long Ticket { get; }
        public CloudSaveOperation Operation { get; }
        public string Payload { get; }
        public string ExpectedProviderRevision { get; }
        public object Context { get; }
        public TimeSpan Timeout { get; }
        public CloudSaveRequest(CloudSaveSession session, string slot, long ticket, CloudSaveOperation operation,
            string payload, string expectedProviderRevision, object context, TimeSpan timeout)
        {
            Session = session; Slot = slot; Ticket = ticket; Operation = operation; Payload = payload;
            ExpectedProviderRevision = expectedProviderRevision; Context = context; Timeout = timeout;
        }
    }
    public interface ISessionCloudSaveAdapter
    {
        CloudSaveSession Session { get; }
        bool IsAvailable { get; }
        bool SupportsConditionalCommit { get; }
        // Implementations bind the SDK operation to request.Session and dispatch completion to Unity's main thread.
        // Read returns a context/lease and revision; Commit must preserve open/resolve/commit semantics.
        void Execute(CloudSaveRequest request, Action<CloudSaveResult> completion);
        void ReleaseContext(object context);
    }
    public enum SaveSyncState { Local, Ready, WaitingForLogin, Syncing, Conflict, Failed }
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

    // Main-thread state machine. An elapsed wait is never claimed as cancellation of remote work.
    public sealed class SaveSyncCoordinator
    {
        private readonly SaveSystem _system;
        private readonly ISessionCloudSaveAdapter _provider;
        private readonly Func<double> _seconds;
        private readonly string _game;
        private readonly int _schema;
        private long _ticket;
        private int _phase;
        private bool _busy;
        private double _deadline;
        private CloudSaveSession _session;
        private string _slot, _capturedRevision, _resetId;
        private long _capturedEpoch;
        private object _context;
        private Action<CloudSaveResult> _completion;
        public Func<bool> PodeSincronizarAgora { get; set; }
        public ISaveConflictResolver Resolver { get; set; } = new DefaultSaveConflictResolver();
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public SaveSyncState State { get; private set; } = SaveSyncState.Local;
        public event Action<SaveSyncState> EstadoAlterado;
        public event Action<SaveConflictPrompt> ConflitoEncontrado;
        public event Action ResetSolicitado;
        public event Action ResetConfirmado;
        public SaveSyncCoordinator(SaveSystem system, ISessionCloudSaveAdapter provider, string game, int schema, Func<double> seconds = null)
        {
            _system = system; _provider = provider; _game = game; _schema = schema;
            var watch = Stopwatch.StartNew();
            _seconds = seconds ?? (() => watch.Elapsed.TotalSeconds);
        }
        public void IdentidadeMudou() { Invalidate(CloudSaveStatus.WaitCancelled); ChangeState(SaveSyncState.WaitingForLogin); }
        public void CancelarEspera() { Invalidate(CloudSaveStatus.WaitCancelled); }
        public void Tick()
        {
            if (!_busy) return;
            if (!_session.Matches(_provider.Session)) { IdentidadeMudou(); return; }
            if (_seconds() >= _deadline) Invalidate(CloudSaveStatus.Timeout);
        }
        public bool Synchronize(string slot, Action<CloudSaveResult> completion = null) => Start(slot, null, completion);
        public bool RequestReset(string slot, string requestId, Action<CloudSaveResult> completion = null)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return false;
            if (_system.Get(SaveSystem.LocalMeta("remoteResetId")) == requestId) { completion?.Invoke(CloudSaveResult.Ok()); return true; }
            return Start(slot, requestId, completion);
        }
        private bool Start(string slot, string resetId, Action<CloudSaveResult> completion)
        {
            Tick();
            if (_busy) { completion?.Invoke(CloudSaveResult.Fail("Busy")); return false; }
            if (string.IsNullOrWhiteSpace(slot) || Timeout <= TimeSpan.Zero || PodeSincronizarAgora == null || !PodeSincronizarAgora())
            { completion?.Invoke(CloudSaveResult.Fail("UnsafeMoment")); return false; }
            _session = _provider.Session;
            if (!_provider.IsAvailable || _session == null)
            { ChangeState(SaveSyncState.WaitingForLogin); completion?.Invoke(CloudSaveResult.Outcome(CloudSaveStatus.Unavailable)); return false; }
            if (!_provider.SupportsConditionalCommit)
            { ChangeState(SaveSyncState.Failed); completion?.Invoke(CloudSaveResult.Fail("ConditionalCommitRequired")); return false; }
            if (_system.CapturarSnapshot().Identity != _session.Identity)
            { completion?.Invoke(CloudSaveResult.Fail("IdentityMismatch")); return false; }
            if (_system.HasPendingOperations()) { completion?.Invoke(CloudSaveResult.Fail("PendingLocalChanges")); return false; }
            _busy = true; _ticket++; _phase = 0; _slot = slot; _resetId = resetId;
            _capturedRevision = _system.Revision; _capturedEpoch = _system.Epoch;
            _deadline = _seconds() + Timeout.TotalSeconds; _completion = completion;
            ChangeState(SaveSyncState.Syncing);
            if (resetId != null) ResetSolicitado?.Invoke();
            Read(_ticket);
            return true;
        }
        private bool Current(long ticket, int phase)
        {
            Tick();
            if (!_busy || ticket != _ticket || phase != _phase) return false;
            if (_system.Revision != _capturedRevision || _system.Epoch != _capturedEpoch || _system.HasPendingOperations() ||
                PodeSincronizarAgora == null || !PodeSincronizarAgora())
            { Finish(CloudSaveResult.Fail("LocalChanged")); return false; }
            return true;
        }
        private CloudSaveRequest Request(long ticket, CloudSaveOperation operation, string payload = null, string revision = null)
            => new CloudSaveRequest(_session, _slot, ticket, operation, payload, revision, _context, Timeout);
        private void Read(long ticket)
        {
            int phase = ++_phase;
            bool answered = false;
            try
            {
                _provider.Execute(Request(ticket, CloudSaveOperation.Read), result =>
                {
                    if (answered) return;
                    answered = true;
                    if (!Current(ticket, phase)) { if (result.Context != null) _provider.ReleaseContext(result.Context); return; }
                    _context = result.Context;
                    if (result.Status != CloudSaveStatus.SdkSuccess || !result.Success) { Finish(result); return; }
                    ValidatedSaveSnapshot incoming = null;
                    if (result.HasPayload)
                    {
                        if (!_system.TryValidateSnapshot(result.Payload, _game, _schema, out incoming, out var error))
                        { Finish(CloudSaveResult.Fail(error.ToString())); return; }
                        if (incoming.Identity != _session.Identity) { Finish(CloudSaveResult.Fail("IdentityMismatch")); return; }
                    }
                    if (_resetId != null)
                    {
                        if (incoming != null && incoming.Data.TryGetValue(SaveSystem.Meta("resetRequest"), out var completedReset) &&
                            completedReset == _resetId)
                        {
                            if (!_system.AplicarSnapshotTransacional(incoming))
                            { Finish(CloudSaveResult.Fail(_system.LastError.ToString())); return; }
                            _system.Enqueue(SaveSystem.LocalMeta("remoteResetId"), _resetId, PriorityLevel.Alto);
                            _system.ProcessQueues();
                            if (_system.HasPendingOperations() || !_system.ConfirmSynced(_system.Revision, _system.Epoch))
                            { Finish(CloudSaveResult.Fail(_system.LastError.ToString())); return; }
                            ResetConfirmado?.Invoke();
                            Finish(CloudSaveResult.Ok());
                            return;
                        }
                        long epoch = Math.Max(_system.Epoch, incoming?.Epoch ?? 0);
                        Commit(ticket, result.ProviderRevision, _system.CreateResetPayload(epoch, _resetId), true);
                        return;
                    }
                    if (incoming == null) { Commit(ticket, result.ProviderRevision, _system.ExportarParaNuvem(), false); return; }
                    var localSnapshot = _system.CapturarSnapshot();
                    var local = SaveConflictCandidate.From(localSnapshot, null, SaveConflictSource.Local);
                    var remote = SaveConflictCandidate.From(incoming, result.Payload, SaveConflictSource.Cloud);
                    local.contentHash = _system.ProgressFingerprint(localSnapshot);
                    remote.contentHash = _system.ProgressFingerprint(incoming);
                    var decision = Resolver.Resolve(local, remote);
                    if (decision == SaveConflictDecision.AskUser)
                    {
                        ChangeState(SaveSyncState.Conflict);
                        ConflitoEncontrado?.Invoke(new SaveConflictPrompt(local, remote, choice =>
                        {
                            if (Current(ticket, phase)) Resolve(ticket, choice, incoming, result);
                        }));
                    }
                    else Resolve(ticket, decision, incoming, result);
                });
            }
            catch { if (_busy && ticket == _ticket && phase == _phase) Finish(CloudSaveResult.Outcome(CloudSaveStatus.Unknown)); }
        }
        private void Resolve(long ticket, SaveConflictDecision decision, ValidatedSaveSnapshot incoming, CloudSaveResult remote)
        {
            if (decision == SaveConflictDecision.UseIncoming)
            {
                if (!_system.AplicarSnapshotTransacional(incoming)) { Finish(CloudSaveResult.Fail(_system.LastError.ToString())); return; }
                _system.ConfirmSynced(_system.Revision, _system.Epoch);
                Finish(CloudSaveResult.Ok());
            }
            else if (decision == SaveConflictDecision.KeepLocal)
            {
                // A previous epoch can never overwrite a reset, including a custom resolver/UI.
                if (incoming.Epoch != _system.Epoch) { Finish(CloudSaveResult.Outcome(CloudSaveStatus.Conflict)); return; }
                if (incoming.Revision == _system.Revision && !string.IsNullOrEmpty(incoming.Revision) &&
                    _system.ProgressFingerprint(incoming) == _system.ProgressFingerprint(_system.CapturarSnapshot()))
                { _system.ConfirmSynced(_system.Revision, _system.Epoch); Finish(CloudSaveResult.Ok()); return; }
                Commit(ticket, remote.ProviderRevision, _system.ExportarParaNuvem(), false);
            }
            else Finish(CloudSaveResult.Outcome(decision == SaveConflictDecision.AskUser ? CloudSaveStatus.UserCancelled : CloudSaveStatus.Conflict));
        }
        private void Commit(long ticket, string expected, string payload, bool reset)
        {
            if (expected == null) { Finish(CloudSaveResult.Fail("ExpectedRevisionRequired")); return; }
            int phase = ++_phase;
            bool answered = false;
            ChangeState(SaveSyncState.Syncing);
            try
            {
                _provider.Execute(Request(ticket, CloudSaveOperation.Commit, payload, expected), result =>
                {
                    if (answered) return;
                    answered = true;
                    if (!Current(ticket, phase)) return;
                    if (result.Success && result.Status == CloudSaveStatus.SdkSuccess)
                    {
                        if (reset)
                        {
                            if (!_system.TryValidateSnapshot(payload, _game, _schema, out var snapshot, out var error) ||
                                !_system.AplicarSnapshotTransacional(snapshot))
                            { Finish(CloudSaveResult.Fail(error == SaveError.None ? _system.LastError.ToString() : error.ToString())); return; }
                            _system.Enqueue(SaveSystem.LocalMeta("remoteResetId"), _resetId, PriorityLevel.Alto);
                            _system.ProcessQueues();
                            if (_system.HasPendingOperations()) { Finish(CloudSaveResult.Fail(_system.LastError.ToString())); return; }
                            ResetConfirmado?.Invoke();
                        }
                        if (!_system.ConfirmSynced(_system.Revision, _system.Epoch))
                        { Finish(CloudSaveResult.Fail(_system.LastError.ToString())); return; }
                    }
                    Finish(result);
                });
            }
            catch { if (_busy && ticket == _ticket && phase == _phase) Finish(CloudSaveResult.Outcome(CloudSaveStatus.Unknown)); }
        }
        private void Invalidate(CloudSaveStatus status)
        {
            _ticket++;
            if (_busy) Finish(CloudSaveResult.Outcome(status));
        }
        private void Finish(CloudSaveResult result)
        {
            if (!_busy) return;
            _busy = false; _phase++;
            var context = _context; _context = null;
            var callback = _completion; _completion = null;
            if (context != null) try { _provider.ReleaseContext(context); } catch { }
            ChangeState(result.Success && result.Status == CloudSaveStatus.SdkSuccess ? SaveSyncState.Ready : SaveSyncState.Failed);
            callback?.Invoke(result);
        }
        private void ChangeState(SaveSyncState state) { if (State != state) { State = state; EstadoAlterado?.Invoke(state); } }
    }
}
