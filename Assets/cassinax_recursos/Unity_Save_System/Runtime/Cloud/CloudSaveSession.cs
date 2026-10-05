// Cassinax Unity System Save - v1.0.0
// Sessao autenticada e requisicao de nuvem.
using System;
namespace cassinax.savesystem
{
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
}
