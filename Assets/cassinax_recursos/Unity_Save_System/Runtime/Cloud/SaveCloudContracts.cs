// Cassinax Unity System Save - v1.0.0
// Contratos de transporte de nuvem: legado, assincrono e com sessao.
using System;
using System.Collections.Generic;
namespace cassinax.savesystem
{
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
}
