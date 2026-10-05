// Cassinax Unity System Save - v1.0.0
// Resultado tipado das operacoes de nuvem.

namespace cassinax.savesystem
{
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
}
