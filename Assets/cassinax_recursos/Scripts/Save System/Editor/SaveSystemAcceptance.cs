// Cassinax Unity System Save - v1.0.0
// Tests and fake providers are excluded from players, including Development Builds.
#if UNITY_EDITOR || SAVE_STANDALONE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using cassinax.savesystem;

public static class CassinaxSaveAcceptance
{
    private const string Key = "test-only-key-16";
    private const string Game = "test.save.contract";
    private const string P = "[SLG0002],[";
    private static int _passed;
    private sealed class MemoryStorage : ISaveStorageAdapter
    {
        public string Main;
        public bool Fail;
        public int Writes;
        private readonly Dictionary<string, string> _backups = new Dictionary<string, string>();
        public bool HasMainSave() => Main != null;
        public string LoadMainSave() => Main;
        public void SaveMainSave(string payload) { if (Fail) throw new IOException("injected"); Main = payload; Writes++; }
        public bool HasBackup(string name) => _backups.ContainsKey(name);
        public string LoadBackup(string name) => _backups[name];
        public void SaveBackup(string name, string payload) { _backups[name] = payload; }
        public void DeleteBackup(string name) { _backups.Remove(name); }
        public void DeleteAll() { Main = null; _backups.Clear(); }
    }
    private sealed class FaultStorage : FileSaveStorage
    {
        public string Stage;
        public bool Kill = false;
        public bool Atomic;
        public FaultStorage(string folder) : base(folder, "main.save") { }
        protected override bool SupportsAtomicReplace => Atomic;
        protected override void WriteStage(string stage)
        {
            if (Stage != stage) return;
#if SAVE_STANDALONE
            if (Kill) Process.GetCurrentProcess().Kill();
#endif
            throw new IOException("injected disk failure");
        }
    }
    private sealed class FakeProvider : ISessionCloudSaveAdapter
    {
        public CloudSaveSession Session { get; set; } = new CloudSaveSession("fake", "account-A", "session-1");
        public bool IsAvailable => true;
        public bool SupportsConditionalCommit => true;
        public List<CloudSaveRequest> Requests = new List<CloudSaveRequest>();
        public List<Action<CloudSaveResult>> Callbacks = new List<Action<CloudSaveResult>>();
        public void Execute(CloudSaveRequest request, Action<CloudSaveResult> completion) { Requests.Add(request); Callbacks.Add(completion); }
        public void ReleaseContext(object context) { }
        public void Reply(int index, string payload = null, string revision = "")
        {
            var result = CloudSaveResult.Ok(payload); result.ProviderRevision = revision;
            Callbacks[index](result);
        }
    }
    private sealed class ConditionalMemoryProvider : ISessionCloudSaveAdapter
    {
        public sealed class Backend { public string Payload; public int Revision; }
        private readonly Backend _backend;
        public CloudSaveSession Session { get; } = new CloudSaveSession("memory", "account-A", Guid.NewGuid().ToString("N"));
        public bool IsAvailable => true;
        public bool SupportsConditionalCommit => true;
        public ConditionalMemoryProvider(Backend backend) { _backend = backend; }
        public void ReleaseContext(object context) { }
        public void Execute(CloudSaveRequest request, Action<CloudSaveResult> completion)
        {
            if (!Session.Matches(request.Session)) { completion(CloudSaveResult.Outcome(CloudSaveStatus.Unavailable)); return; }
            string revision = _backend.Revision == 0 ? "" : _backend.Revision.ToString();
            if (request.Operation == CloudSaveOperation.Read)
            {
                var result = CloudSaveResult.Ok(_backend.Payload); result.ProviderRevision = revision;
                completion(result);
            }
            else if (request.ExpectedProviderRevision != revision) completion(CloudSaveResult.Outcome(CloudSaveStatus.Conflict));
            else { _backend.Payload = request.Payload; _backend.Revision++; completion(CloudSaveResult.Ok()); }
        }
    }
    private static SaveSystem New(out SaveCore core, out MemoryStorage storage, int schema = 1, string owner = "")
    {
        core = new SaveCore(Key, 2);
        storage = new MemoryStorage();
        var s = new SaveSystem(core, storage);
        s.Configure(Game, schema);
        if (owner != "") s.Enqueue("[SLG0003],[accountHash]", owner, PriorityLevel.Normal);
        s.ForceSave();
        s.ProcessQueues();
        return s;
    }
    private static void Set(SaveSystem s, string key, string value) { s.Enqueue(key, value, PriorityLevel.Normal); s.ProcessQueues(); }
    private static void Check(bool condition, string message = "assertion") { if (!condition) throw new Exception(message); }
    private static void Test(string name, Action body)
    {
        body(); _passed++;
#if SAVE_STANDALONE
        Console.WriteLine("PASS " + name);
#else
        UnityEngine.Debug.Log("PASS " + name);
#endif
    }
    private static string Signed(SaveCore core, IDictionary<string, string> data)
    {
        string body = "[2]" + core.Encrypt(core.Serialize(data));
        return body + "." + core.ComputeIntegrityHash(body);
    }
    private static string Legacy(SaveCore core, bool duplicate = false, bool signed = true)
    {
        string text = "[SLG0001][0001][sucesso]\n[SLG0001][IDJogo][" + Game +
            "]\n[SLG0001][versaoEstruturaSave][1]\n[SLG0001][versaoCore][1]\n[SLG0002],[coins][4]\n";
        if (duplicate) text += "[SLG0002][coins][5]\n";
        if (signed) text = "[SLG0001][hashVerificacao][" + core.ComputeIntegrityHash(text) + "]\n" + text;
        return "[1]" + core.Encrypt(text);
    }
#if UNITY_EDITOR
    // Chamado pela janela do pacote e por -executeMethod em modo batch.
    public static void RunBatch() { Run(Array.Empty<string>()); }
#endif
    public static int Run(string[] args)
    {
#if SAVE_STANDALONE
        if (args.Length > 0 && args[0] == "kill")
        {
            var disk = new FaultStorage(args[1]) { Stage = args[2], Kill = true, Atomic = args[3] == "true" };
            var s = new SaveSystem(new SaveCore(Key, 2), disk); s.Configure(Game, 1); s.LoadFromStorage();
            Set(s, P + "coins]", "new");
            return 99;
        }
#endif
        Test("codec arbitrary UTF8 and empty roundtrip", () =>
        {
            var core = new SaveCore(Key, 1);
            var values = new Dictionary<string, string> { { "[]\nkey", "{\"name\":\"ação 漢字 🧪\",\"x\":[1,2]}\n\r\n[]" }, { "empty", "" } };
            var parsed = core.Parse(core.Decrypt(core.Encrypt(core.Serialize(values))));
            Check(values.All(p => parsed[p.Key] == p.Value));
            core.Set("ab-cd", "1"); core.Set("cd-ab", "2");
            Check(core.GetKeysByPrefix("ab").SequenceEqual(new[] { "ab-cd" }));
        });
        Test("legacy HMAC migration without fabricated history", () =>
        {
            var s = New(out var core, out _);
            Check(s.Importar(Legacy(core), Game, 1));
            Check(s.Get(P + "coins]") == "4" && s.Revision == "");
            Check(s.Get(SaveSystem.Header("dataAtualizacao")) == "");
        });
        Test("unsigned legacy only by explicit local policy", () =>
        {
            var s = New(out var c, out var disk);
            disk.Main = Legacy(c, signed: false);
            bool threw = false;
            try { s.LoadFromStorage(); } catch { threw = true; }
            Check(threw);
            s.AllowUnsignedLegacyLocal = true; s.LoadFromStorage();
            Check(s.Get(P + "coins]") == "4");
            Check(!s.Importar(disk.Main, Game, 1));
        });
        Test("replace removes absent progress and preserves local preferences", () =>
        {
            var s = New(out _, out _); Set(s, P + "coins]", "1"); Set(s, "local.sound", "off");
            string old = s.ExportarParaNuvem(); Set(s, P + "unlock]", "yes"); Set(s, "local.sound", "on");
            Check(s.Importar(old, Game, 1)); Check(s.Get(P + "unlock]") == null && s.Get("local.sound") == "on");
            Set(s, P + "unlock]", "yes");
            Check(s.Importar(old, Game, 1, SaveImportMode.Merge)); Check(s.Get(P + "unlock]") == "yes");
        });
        Test("invalid HMAC game schema duplicate future core do not mutate or write", () =>
        {
            var s = New(out var c, out var disk); Set(s, P + "coins]", "7");
            var data = s.CapturarSnapshot().Data.ToDictionary(p => p.Key, p => p.Value);
            string before = c.SaveToPlainText(); int writes = disk.Writes;
            string good = s.ExportarParaNuvem();
            var bad = new List<string> { good.Substring(0, good.Length - 1) + (good.EndsWith("0") ? "1" : "0"), "[99]x", Legacy(c, true) };
            data[SaveSystem.Header("IDJogo")] = "another-game"; bad.Add(Signed(c, data));
            data[SaveSystem.Header("IDJogo")] = Game; data[SaveSystem.Header("versaoEstruturaSave")] = "999"; bad.Add(Signed(c, data));
            foreach (var payload in bad) Check(!s.Importar(payload, Game, 1));
            Check(c.SaveToPlainText() == before && disk.Writes == writes);
        });
        Test("schema migrations are pure and missing steps rejected", () =>
        {
            var old = New(out _, out _); Set(old, P + "old]", "value");
            var current = New(out _, out _, 2);
            Check(!current.Importar(old.ExportarParaNuvem(), Game, 2) && current.LastError == SaveError.MigrationRequired);
            current.RegisterMigration(1, input => { var d = input.ToDictionary(p => p.Key, p => p.Value); d[P + "new]"] = d[P + "old]"]; d.Remove(P + "old]"); return d; });
            Check(current.Importar(old.ExportarParaNuvem(), Game, 2));
            Check(current.Get(P + "new]") == "value" && old.Get(P + "old]") == "value");
        });
        Test("transaction one commit event no-op rollback and disk failure", () =>
        {
            var s = New(out var c, out var disk); int events = 0; s.ProgressoAlterado += _ => events++;
            int writes = disk.Writes;
            s.Begin(); for (int i = 0; i < 100; i++) s.Enqueue(P + i + "]", i.ToString(), PriorityLevel.Normal);
            Check(s.Commit()); Check(events == 1 && disk.Writes == writes + 1);
            string revision = s.Revision, before = c.SaveToPlainText();
            Set(s, P + "0]", "0"); Check(s.Revision == revision && events == 1);
            s.Begin(); s.Enqueue(P + "0]", "x", PriorityLevel.Normal); s.Rollback(); Check(c.SaveToPlainText() == before);
            disk.Fail = true; s.Begin(); s.Enqueue(P + "0]", "failed", PriorityLevel.Normal); Check(!s.Commit());
            Check(c.SaveToPlainText() == before && events == 1); s.Rollback();
        });
        Test("read export scopes and revision are stable", () =>
        {
            var s = New(out _, out var disk, owner: "account-A");
            Set(s, P + "coins]", "9"); Set(s, "local.sound", "off"); Set(s, "[SLG0003],[accountEmail]", "private@example.test");
            string revision = s.Revision, date = s.Get(SaveSystem.Header("dataAtualizacao")); int writes = disk.Writes;
            for (int i = 0; i < 2; i++)
            {
                var p = s.ExportarParaNuvem(); Check(s.TryValidateSnapshot(p, Game, 1, out var snap, out _));
                Check(!snap.Data.ContainsKey("local.sound") && !snap.Data.ContainsKey("[SLG0003],[accountEmail]"));
                Check(snap.Identity == "account-A");
            }
            Check(s.Revision == revision && s.Get(SaveSystem.Header("dataAtualizacao")) == date && disk.Writes == writes);
        });
        Test("bounded decompression and truncation", () =>
        {
            var c = new SaveCore(Key, 2);
            string zip = c.Encrypt(new string('x', 100000));
            var small = new SaveCore(Key, 2, true, new SaveLimits { MaxPlainTextBytes = 1000 });
            bool rejected = false; try { small.Decrypt(zip); } catch (SaveValidationException e) { rejected = e.Error == SaveError.LimitExceeded; }
            Check(rejected);
            var s = New(out _, out _); Check(!s.Importar("[2]" + zip.Substring(0, zip.Length / 2), Game, 1));
        });
        Test("conflict ignores clocks and never sums economics", () =>
        {
            var resolver = new DefaultSaveConflictResolver();
            var a = new SaveConflictCandidate { isValid = true, identity = "A", revision = "a", parentRevision = "base", epoch = 1, updatedAtUtc = DateTime.MaxValue };
            var b = new SaveConflictCandidate { isValid = true, identity = "A", revision = "b", parentRevision = "base", epoch = 1, updatedAtUtc = DateTime.MinValue };
            Check(resolver.Resolve(a, b) == SaveConflictDecision.AskUser);
            b.parentRevision = a.revision; Check(resolver.Resolve(a, b) == SaveConflictDecision.UseIncoming);
            b.identity = "B"; Check(resolver.Resolve(a, b) == SaveConflictDecision.RejectIncoming);
        });
        Test("reset epoch idempotent clears pending and old snapshot rejected", () =>
        {
            var s = New(out _, out _); Set(s, P + "coins]", "9");
            string old = s.ExportarParaNuvem(); s.Enqueue(P + "late]", "1", PriorityLevel.Normal);
            Check(s.ResetProgress("reset-1")); string rev = s.Revision; long epoch = s.Epoch;
            Check(s.ResetProgress("reset-1") && s.Revision == rev && s.Epoch == epoch);
            s.ProcessQueues(); Check(s.Get(P + "late]") == null && s.Get(P + "coins]") == null);
            Check(!s.Importar(old, Game, 1) && s.LastError == SaveError.StaleEpoch);
        });
        Test("cloud old account callback duplicate timeout cannot free another operation", () =>
        {
            var s = New(out _, out _, owner: "account-A"); Set(s, P + "coins]", "1");
            var fake = new FakeProvider(); double now = 0;
            var sync = new SaveSyncCoordinator(s, fake, Game, 1, () => now) { PodeSincronizarAgora = () => true };
            Check(sync.Synchronize("main")); fake.Session = new CloudSaveSession("fake", "account-B", "session-2");
            fake.Reply(0); Check(fake.Requests.Count == 1 && s.Get(P + "coins]") == "1");
            fake.Session = new CloudSaveSession("fake", "account-A", "session-3");
            Check(sync.Synchronize("main")); now = 99; sync.Tick(); fake.Reply(1); Check(fake.Requests.Count == 2);
            Check(sync.Synchronize("main")); fake.Reply(0); Check(sync.State == SaveSyncState.Syncing);
            fake.Reply(2); Check(fake.Requests.Count == 4); fake.Reply(2); Check(fake.Requests.Count == 4);
            fake.Reply(3); Check(sync.State == SaveSyncState.Ready);
        });
        Test("cloud rejects local changes during read and remote reset failure preserves local", () =>
        {
            var s = New(out _, out _, owner: "account-A"); Set(s, P + "coins]", "9");
            var fake = new FakeProvider(); var sync = new SaveSyncCoordinator(s, fake, Game, 1) { PodeSincronizarAgora = () => true };
            sync.Synchronize("main"); Set(s, P + "coins]", "10"); fake.Reply(0); Check(fake.Requests.Count == 1);
            Check(sync.RequestReset("main", "remote-1")); fake.Reply(1);
            fake.Callbacks[2](CloudSaveResult.Fail("network"));
            Check(s.Get(P + "coins]") == "10" && s.Epoch == 0);
            sync.RequestReset("main", "remote-1"); fake.Reply(3); fake.Reply(4);
            Check(s.Get(P + "coins]") == null && s.Epoch == 1);
            int count = fake.Requests.Count; sync.RequestReset("main", "remote-1"); Check(fake.Requests.Count == count);
        });
        Test("multipart missing out-of-order duplicate and whole part limit", () =>
        {
            string text = new string('x', 240);
            var parts = SavePayloadChunks.SplitForTextTransfer(text, 92);
            Check(parts.Count == 4 && parts.All(p => p.Length <= 92));
            var missing = SavePayloadChunks.AnalyzeTextParts(new[] { parts[2], parts[0] });
            Check(!missing.IsComplete && missing.MissingParts.SequenceEqual(new[] { 2, 4 }));
            Check(SavePayloadChunks.AnalyzeTextParts(parts.AsEnumerable().Reverse()).Payload == text);
            var duplicate = SavePayloadChunks.AnalyzeTextParts(parts.Concat(new[] { parts[0].Replace("xxx", "yyy") }));
            Check(!duplicate.IsComplete && duplicate.Errors.Count > 0);
            Check(!SavePayloadChunks.AnalyzeTextParts(new[] { "[1-999999999999999]x[fim-1]" }).IsComplete);
            Check(!SavePayloadChunks.AnalyzeTextParts(new[] { parts[0] + "truncated" }).IsComplete);
        });
        Test("account bind resets anonymous progress atomically and switch requires explicit reset", () =>
        {
            var s = New(out _, out var disk); Set(s, P + "coins]", "99"); Set(s, "local.sound", "on");
            var defaults = new Dictionary<string, string> { { P + "coins]", "0" } };
            Check(!s.BindIdentity("A", "fake", defaults, false));
            disk.Fail = true; Check(!s.BindIdentity("A", "fake", defaults, true));
            Check(s.Get(P + "coins]") == "99" && s.CapturarSnapshot().Identity == "");
            disk.Fail = false; Check(s.BindIdentity("A", "fake", defaults, true));
            Check(s.Get(P + "coins]") == "0" && s.Get("local.sound") == "on");
            Set(s, P + "coins]", "8"); Check(s.BindIdentity("A", "fake", defaults, true));
            Check(s.Get(P + "coins]") == "8");
            Check(!s.BindIdentity("B", "fake", defaults, true));
            Check(s.BindIdentity("B", "fake", defaults, true, true));
            Check(s.Get(P + "coins]") == "0" && s.CapturarSnapshot().Identity == "B");
        });
        Test("schema bypass and mutable snapshot are rejected", () =>
        {
            var s = New(out var c, out _);
            var data = s.CapturarSnapshot().Data.ToDictionary(p => p.Key, p => p.Value);
            data[SaveSystem.Header("versaoEstruturaSave")] = "2";
            Check(s.TryValidateSnapshot(Signed(c, data), Game, 2, out var snapshot, out _));
            Check(!s.AplicarSnapshotTransacional(snapshot));
            bool immutable = false;
            try { ((IDictionary<string, string>)snapshot.Data)[P + "tamper]"] = "x"; } catch (NotSupportedException) { immutable = true; }
            Check(immutable);
        });
        Test("two isolated clients divergence CAS and stale epoch after reset", () =>
        {
            var a = New(out _, out _, owner: "account-A"); Set(a, P + "coins]", "10");
            var b = New(out _, out _, owner: "account-A");
            var backend = new ConditionalMemoryProvider.Backend();
            var providerA = new ConditionalMemoryProvider(backend);
            var providerB = new ConditionalMemoryProvider(backend);
            var syncA = new SaveSyncCoordinator(a, providerA, Game, 1) { PodeSincronizarAgora = () => true };
            var syncB = new SaveSyncCoordinator(b, providerB, Game, 1) { PodeSincronizarAgora = () => true };
            Check(syncA.Synchronize("main"));
            Check(b.Importar(backend.Payload, Game, 1)); Check(b.ConfirmSynced(b.Revision, b.Epoch));
            Set(a, P + "coins]", "7"); Set(b, P + "coins]", "8");
            Check(syncA.Synchronize("main"));
            SaveConflictPrompt prompt = null; syncB.ConflitoEncontrado += p => prompt = p;
            Check(syncB.Synchronize("main") && prompt != null);
            Check(b.Get(P + "coins]") == "8"); prompt.ChooseIncoming();
            Check(b.Get(P + "coins]") == "7");
            string previous = backend.Payload;
            Check(syncA.RequestReset("main", "shared-reset"));
            Set(b, P + "coins]", "100"); Check(syncB.Synchronize("main"));
            Check(b.Epoch == a.Epoch && b.Get(P + "coins]") == null);
            Check(!b.Importar(previous, Game, 1));
            CloudSaveResult stale = default;
            providerB.Execute(new CloudSaveRequest(providerB.Session, "main", 77, CloudSaveOperation.Commit,
                previous, "obsolete", null, TimeSpan.FromSeconds(1)), r => stale = r);
            Check(stale.Status == CloudSaveStatus.Conflict);
        });
        foreach (bool atomic in new[] { false, true })
            foreach (string stage in atomic ? new[] { "before-temp", "after-temp-write", "after-temp-flush", "before-replace", "after-replace" } :
                new[] { "before-temp", "after-temp-write", "after-temp-flush", "before-replace", "after-backup", "after-main-remove", "after-main-move" })
            {
                Test("disk fault recovery " + atomic + "/" + stage, () => DiskRecovery(stage, atomic, false));
#if SAVE_STANDALONE
                Test("process kill recovery " + atomic + "/" + stage, () => DiskRecovery(stage, atomic, true));
#endif
            }
        Test("delete only managed files and sidecars", () =>
        {
            var disk = new FaultStorage("delete-" + Guid.NewGuid().ToString("N"));
            var s = new SaveSystem(new SaveCore(Key, 2), disk); s.Configure(Game, 1); s.ForceSave();
            string payload = s.ExportarParaNuvem();
            var paths = new[] { disk.GetMainPath(), disk.GetBackupPath("Backup Auto"), disk.GetBackupPath("Backup Manual"), disk.GetBackupPath("Backup Seguranca") };
            foreach (string path in paths) foreach (string suffix in new[] { "", ".bak", ".tmp" }) File.WriteAllText(path + suffix, payload);
            string export = Path.Combine(Path.GetDirectoryName(disk.GetMainPath()), "manual-export.save"); File.WriteAllText(export, payload);
            s.ApagarTudo();
            Check(paths.All(p => !File.Exists(p) && !File.Exists(p + ".bak") && !File.Exists(p + ".tmp")) && File.Exists(export));
            File.Delete(export); Directory.Delete(Path.GetDirectoryName(export));
            bool rejected = false; try { new FileSaveStorage("../outside", "main.save"); } catch (ArgumentException) { rejected = true; }
            Check(rejected);
        });
#if SAVE_STANDALONE
        Console.WriteLine("TOTAL PASS " + _passed);
#else
        UnityEngine.Debug.Log("TOTAL PASS " + _passed);
#endif
        return 0;
    }
    private static void DiskRecovery(string stage, bool atomic, bool kill)
    {
        string folder = "fault-" + Guid.NewGuid().ToString("N");
        var disk = new FaultStorage(folder) { Atomic = atomic };
        var s = new SaveSystem(new SaveCore(Key, 2), disk); s.Configure(Game, 1);
        Set(s, P + "coins]", "old");
#if SAVE_STANDALONE
        if (kill)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(CassinaxSaveAcceptance).Assembly.Location);
            foreach (string arg in new[] { "kill", folder, stage, atomic ? "true" : "false" }) start.ArgumentList.Add(arg);
            using (var child = Process.Start(start)) { Check(child.WaitForExit(15000), "child timeout"); Check(child.ExitCode != 0); }
        }
        else
#endif
        {
            disk.Stage = stage; Set(s, P + "coins]", "new");
        }
        var reopened = new SaveSystem(new SaveCore(Key, 2), new FileSaveStorage(folder, "main.save"));
        reopened.Configure(Game, 1); reopened.LoadFromStorage();
        Check(new[] { "old", "new" }.Contains(reopened.Get(P + "coins]")), "lost last valid copy at " + stage);
        reopened.ApagarTudo(); Directory.Delete(Path.GetDirectoryName(disk.GetMainPath()));
    }
}
#endif
