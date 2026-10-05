// Cassinax Unity System Save - v1.0.0
// Maquina de estados de sincronizacao na thread principal.
using System;
using System.Diagnostics;
namespace cassinax.savesystem
{
    public enum SaveSyncState { Local, Ready, WaitingForLogin, Syncing, Conflict, Failed }

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
