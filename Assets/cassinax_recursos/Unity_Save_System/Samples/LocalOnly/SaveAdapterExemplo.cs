// Cassinax Unity System Save - v1.0.0
//
// ESTE ARQUIVO E UM EXEMPLO. NAO USE COMO SCRIPT DO JOGO.
//
// Ele existe para ser lido e copiado. O adapter e a ponte entre o jogo e a API, e e a
// UNICA peca que o projeto recria por conta propria, fora de "cassinax_recursos" --
// normalmente na pasta principal de scripts do projeto, junto dos outros controladores.
//
// Como usar:
//   1. Leia este exemplo.
//   2. Crie o adapter do projeto fora de "cassinax_recursos", com o nome que o projeto
//      usa (por exemplo SaveAdapter), adaptando ID, chave, escopos, dados padrao e
//      migracoes. Classes parciais ajudam a separar assuntos em arquivos.
//   3. Apague este exemplo do projeto.
//
// Por que o nome tem "Exemplo": para que uma reimportacao do pacote nunca sobrescreva o
// adapter que o projeto escreveu. O exemplo e substituido, o seu adapter nao e tocado.
//
// Todo o resto do pacote e fixo: nao modifique os arquivos de Runtime e Editor. Precisando
// de algo que a API nao oferece, o caminho e pedir a mudanca no pacote, nao editar a copia
// local -- uma correcao local se perde na proxima atualizacao.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using cassinax.savesystem;

/// <summary>
/// Exemplo de fachada entre o jogo e a API. Adapte dados padrao, escopos e migracoes ao
/// projeto. Nao exige SDK de autenticacao: funciona como save local desde o primeiro uso.
/// </summary>
public partial class SaveAdapterExemplo : MonoBehaviour
{
    public enum LocalStorageMode { PlayerPrefs, File }
    [Flags] public enum SaveFeatureFlags { Nenhum = 0, ExportacaoManual = 1, MultipartTexto = 2,
        MultipartArquivos = 4, Nuvem = 8, Conta = 16, Compras = 32, Auditoria = 64, AntiCompartilhamento = 128 }
    public enum AccountBindingMode { Nenhum, Opcional, Obrigatorio }
    public enum AccountIdentityMode { Nenhum, HashDaConta, EmailAutenticado, EmailAutenticadoComEmailVisivel }
    public enum AccountSwitchPolicy { Reject, RequireExplicitReset }
    public enum ResetPolicy { LocalOnly, RemoteFirst }
    [Serializable] public struct ChaveLegada { public int versao; public string chave; }
    [Serializable] public struct DadoPadrao
    {
        public string comando, chave, valor;
        public PriorityLevel prioridade;
        public DadoPadrao(string comando, string chave, string valor, PriorityLevel prioridade)
        { this.comando = comando; this.chave = chave; this.valor = valor; this.prioridade = prioridade; }
    }
    [Serializable] public struct EscopoDeChave
    {
        public string prefixo;
        public SaveScope escopo;
    }

    [Header("Identificacao imutavel do jogo")]
    [SerializeField] private string _idJogo = "CHANGE_ME";
    [SerializeField] private int _versaoEstruturaSave = 1;
    [SerializeField] private bool _usarVersaoAutomatica = true;
    [SerializeField] private string _versaoJogoManual = "1.0.0";
    [SerializeField] private bool _usarDeteccaoAutomatica = true;
    [SerializeField] private string _plataformaManual = "Desktop";
    [Header("Codec e limites")]
    [SerializeField] private string _chaveCriptografia = "CHANGE_ME_16_BYTE";
    [SerializeField] private bool _usarCompressao = true;
    [SerializeField] private int _limitePayloadBytes = 4 * 1024 * 1024;
    [SerializeField] private int _limiteDescomprimidoBytes = 8 * 1024 * 1024;
    [Tooltip("Somente migracao local de versoes antigas sem HMAC. Desative apos a transicao.")]
    [SerializeField] private bool _permitirLegadoLocalSemHmac = false;
    [SerializeField] private List<ChaveLegada> _chavesLegadas = new List<ChaveLegada>();
    [Header("Armazenamento local")]
    [SerializeField] private LocalStorageMode _storageMode = LocalStorageMode.File;
    [SerializeField] private bool _forcarPlayerPrefsNoWebGL = true;
    [SerializeField] private string _storageFolderName = "CassinaxUnitySystemSave";
    [SerializeField] private string _mainSaveFileName = "main.save";
    [Tooltip("Compatibilidade com uma pasta antiga exclusiva deste jogo. Novos jogos usam subpasta por ID.")]
    [SerializeField] private bool _usarPastaLegadaExclusiva = false;
    [Header("Inicializacao")]
    [SerializeField] private bool _inicializarNoAwake = true;
    [SerializeField] private bool _processarFilaAutomaticamente = true;
    [SerializeField] private float _intervaloProcessamento = 0.1f;
    [SerializeField] private List<DadoPadrao> _dadosPadrao = new List<DadoPadrao>();
    [Tooltip("Prefixo mais longo vence. Chaves sem regra sao locais. Metadados e identidade sao reservados.")]
    [SerializeField] private List<EscopoDeChave> _escopos = new List<EscopoDeChave>
    {
        new EscopoDeChave { prefixo = "[SLG0002],", escopo = SaveScope.Progress },
        new EscopoDeChave { prefixo = "[SLG0200],", escopo = SaveScope.Audit }
    };
    [Header("Recursos opcionais")]
    [SerializeField] private SaveFeatureFlags _recursosAtivos = SaveFeatureFlags.ExportacaoManual |
        SaveFeatureFlags.MultipartTexto | SaveFeatureFlags.MultipartArquivos;
    [SerializeField] private AccountBindingMode _modoVinculoConta = AccountBindingMode.Opcional;
    [SerializeField] private AccountIdentityMode _modoIdentidadeConta = AccountIdentityMode.HashDaConta;
    [SerializeField] private bool _resetarSaveComConteudoSemContaAoVincular = true;
    [SerializeField] private AccountSwitchPolicy _trocaDeConta = AccountSwitchPolicy.Reject;
    [SerializeField] private ResetPolicy _politicaReset = ResetPolicy.LocalOnly;
    [Header("Sincronizacao")]
    [SerializeField] private string _slotSitePadrao = "main";
    [SerializeField] private float _timeoutNuvemSegundos = 30;
    [SerializeField] private float _intervaloAutoSyncSegundos = 60;
    [SerializeField] private bool _sincronizarAutomaticamente = false;
    [SerializeField] private CassinaxSiteSaveApi _siteApi = null;

    private static SaveAdapterExemplo _instancia;
    private SaveCore _saveCore;
    private SaveSystem _saveSystem;
    private SaveSyncCoordinator _sync;
    private CloudSaveSession _identidade;
    private bool _inicializado, _salvando, _falhaPersistencia;
    private float _ultimoAutoSync;
    private ISaveConflictResolver _resolvedor = new DefaultSaveConflictResolver();
    private const string Conta = "[SLG0003],[";
    public Func<bool> PodeSincronizarAgora { get; set; }
    public SaveSyncState Estado { get; private set; } = SaveSyncState.Local;
    public SaveError UltimoErro { get; private set; }
    public event Action<SaveSyncState> EstadoAlterado;
    public event Action<SaveError> ErroTipado;
    public event Action<ValidatedSaveSnapshot> ProgressoAlterado;
    public event Action<ValidatedSaveSnapshot> ProgressoAplicado;
    public event Action<SaveConflictPrompt> ConflitoEncontrado;
    public event Action ResetSolicitado;
    public event Action ResetConfirmado;

    public static SaveAdapterExemplo ObterInstancia
    {
        get
        {
#if UNITY_2022_2_OR_NEWER
            // Retain the legacy first-instance selection on projects with duplicate components.
#pragma warning disable CS0618
            if (_instancia == null) _instancia = FindFirstObjectByType<SaveAdapterExemplo>();
#pragma warning restore CS0618
#else
            if (_instancia == null) _instancia = FindAnyObjectByType<SaveAdapterExemplo>();
#endif
            return _instancia;
        }
    }
    public bool Inicializado => _inicializado;
    public bool salvando => _salvando;
    public int CoreVersion => SaveCore.CurrentCodecVersion;
    public string VersaoJogo => _usarVersaoAutomatica ? Application.version : _versaoJogoManual;
    public bool NuvemDoSiteDisponivel => _sync != null && _identidade != null && Ativo(SaveFeatureFlags.Nuvem);
    private void Awake() { if (_instancia == null) _instancia = this; if (_inicializarNoAwake) Inicializar(); }
    private void Start() { if (_processarFilaAutomaticamente) StartCoroutine(ProcessamentoContinuo()); }
    private void Update()
    {
        _sync?.Tick();
        if (_sincronizarAutomaticamente && _sync != null && PodeSincronizarAgora != null && PodeSincronizarAgora() &&
            Time.unscaledTime - _ultimoAutoSync >= _intervaloAutoSyncSegundos)
        { _ultimoAutoSync = Time.unscaledTime; SincronizarComSite(); }
    }
    private void OnApplicationPause(bool paused) { if (paused) SalvarAgora(); }
    private void OnApplicationFocus(bool focused) { if (!focused) SalvarAgora(); }
    private void OnApplicationQuit() { SalvarAgora(); }
    private void OnDestroy() { _sync?.CancelarEspera(); if (_instancia == this) _instancia = null; }

    public void Inicializar()
    {
        if (_inicializado) return;
        if (string.IsNullOrWhiteSpace(_idJogo) || _idJogo == "CHANGE_ME" ||
            _idJogo.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || _idJogo.Contains("/") || _idJogo.Contains("\\") ||
            _versaoEstruturaSave < 1 || _chaveCriptografia == "CHANGE_ME_16_BYTE")
        { Report(SaveError.InvalidConfiguration); return; }
        try
        {
            _saveCore = new SaveCore(_chaveCriptografia, 2, _usarCompressao,
                new SaveLimits { MaxPayloadBytes = _limitePayloadBytes, MaxPlainTextBytes = _limiteDescomprimidoBytes });
            ISaveStorageAdapter storage = _storageMode == LocalStorageMode.PlayerPrefs ||
                (_forcarPlayerPrefsNoWebGL && Application.platform == RuntimePlatform.WebGLPlayer)
                ? (ISaveStorageAdapter)new PlayerPrefsSaveStorage("Dados Usuario." + _idJogo)
                : new FileSaveStorage(_usarPastaLegadaExclusiva ? _storageFolderName : Path.Combine(_storageFolderName, _idJogo), _mainSaveFileName);
            _saveSystem = new SaveSystem(_saveCore, storage, _chavesLegadas.ToDictionary(k => k.versao, k => k.chave));
            _saveSystem.AllowUnsignedLegacyLocal = _permitirLegadoLocalSemHmac;
            _saveSystem.Configure(_idJogo, _versaoEstruturaSave, Escopo);
            RegistrarMigracoes(_saveSystem);
            _saveSystem.ErroTipado += Report;
            _saveSystem.ProgressoAlterado += snapshot => ProgressoAlterado?.Invoke(snapshot);
            _saveSystem.ProgressoAplicado += snapshot => ProgressoAplicado?.Invoke(snapshot);
            bool exists = _saveSystem.HasMainSave();
            if (exists)
            {
                try { _saveSystem.LoadFromStorage(); }
                catch
                {
                    // Keep damaged/unknown files for recovery. Local play may continue in memory.
                    if (!_saveSystem.RestaurarBackupAutomatico())
                    {
                        _falhaPersistencia = true;
                        foreach (var d in _dadosPadrao) _saveCore.Set(BuildKey(d.comando, d.chave), d.valor);
                        Report(SaveError.Storage);
                    }
                }
            }
            _inicializado = true;
            if (!exists) AplicarPadroes();
            if (!_falhaPersistencia)
            {
                GarantirContaPadrao();
                _saveSystem.ProcessQueues();
                _saveSystem.ForceSave();
            }
            if (!_falhaPersistencia) MudarEstado(SaveSyncState.Local);
        }
        catch { Report(SaveError.InvalidConfiguration); }
    }
    protected virtual void RegistrarMigracoes(SaveSystem system) { }
    private void GarantirInicializacao()
    {
        if (!_inicializado) Inicializar();
        if (!_inicializado) throw new SaveValidationException(SaveError.InvalidConfiguration);
    }
    private bool Ativo(SaveFeatureFlags feature) => (_recursosAtivos & feature) == feature;
    private SaveScope Escopo(string key)
    {
        foreach (var rule in _escopos.Where(r => !string.IsNullOrEmpty(r.prefixo)).OrderByDescending(r => r.prefixo.Length))
            if (key.StartsWith(rule.prefixo, StringComparison.Ordinal)) return rule.escopo;
        return SaveScope.LocalPreference;
    }
    private static string BuildKey(string comando, string chave)
    {
        if (string.IsNullOrWhiteSpace(comando) || string.IsNullOrEmpty(chave) || comando.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0)
            throw new ArgumentException("Invalid command/key.");
        return "[" + comando.Trim().ToUpperInvariant() + "],[" + chave + "]";
    }
    private string ResolveKey(string comando, string chave)
    {
        switch (comando)
        {
            case "salvar infos sistema": case "obter infos sistema": return BuildKey("SLG0001", chave);
            case "salvar xp jogador": case "obter xp jogador": return BuildKey("SLG0002", "xp");
            case "salvar nivel jogador": case "obter nivel jogador": return BuildKey("SLG0002", "nivel");
            case "salvar moedas jogador": case "obter moedas jogador": return BuildKey("SLG0002", "moedas");
            case "salvar diamantes jogador": case "obter diamantes jogador": return BuildKey("SLG0002", "diamantes");
            case "salvar posicao peca": case "obter posicao peca": return BuildKey("SLG0011", chave);
            default: return BuildKey(comando, chave);
        }
    }
    public void SalvarDados(string comando, string chave, string dado)
    {
        GarantirInicializacao();
        string key = ResolveKey(comando, chave);
        if (_falhaPersistencia) { _saveCore.Set(key, dado); return; }
        _saveSystem.Enqueue(key, dado, PriorityLevel.Normal);
    }
    public void RemoverDado(string comando, string chave, PriorityLevel prioridade = PriorityLevel.Normal)
    {
        GarantirInicializacao();
        if (_falhaPersistencia) { _saveCore.Remove(ResolveKey(comando, chave)); return; }
        _saveSystem.EnqueueRemove(ResolveKey(comando, chave), prioridade);
    }
    public string ObterDado(string comando, string chave)
    { GarantirInicializacao(); return _saveSystem.Get(ResolveKey(comando, chave)) ?? "BuscarDado erro"; }
    public void Begin() { GarantirInicializacao(); _saveSystem.Begin(); }
    public bool Commit() { GarantirInicializacao(); return !_falhaPersistencia && _saveSystem.Commit(); }
    public void Rollback() { _saveSystem?.Rollback(); }
    public void SalvarAgora()
    {
        if (!_inicializado || _falhaPersistencia) return;
        _salvando = true;
        try { _saveSystem.ProcessQueues(); if (!_saveSystem.HasPendingOperations()) _saveSystem.ForceSave(); }
        catch { Report(SaveError.Storage); }
        finally { _salvando = false; }
    }
    private IEnumerator ProcessamentoContinuo()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(Math.Max(0.05f, _intervaloProcessamento));
            if (_inicializado && !_falhaPersistencia) _saveSystem.ProcessQueues();
        }
    }
    public ValidatedSaveSnapshot CapturarSnapshot() { GarantirInicializacao(); return _saveSystem.CapturarSnapshot(); }
    public bool ValidarSnapshot(string payload, out ValidatedSaveSnapshot snapshot, out SaveError error)
    { GarantirInicializacao(); return _saveSystem.TryValidateSnapshot(payload, _idJogo, _versaoEstruturaSave, out snapshot, out error); }
    public bool AplicarSnapshotTransacional(ValidatedSaveSnapshot snapshot, SaveImportMode mode = SaveImportMode.ReplaceSnapshot)
    {
        GarantirInicializacao();
        if (_falhaPersistencia || !ValidarDono(snapshot)) return false;
        _sync?.CancelarEspera();
        return _saveSystem.AplicarSnapshotTransacional(snapshot, mode);
    }
    public string ExportarSaveCompleto() => ExportarSave(new ExportFilter { AllData = true });
    public string ExportarSave(ExportFilter filtro)
    {
        GarantirInicializacao();
        if (!Ativo(SaveFeatureFlags.ExportacaoManual)) throw new SaveValidationException(SaveError.Unavailable);
        return _saveSystem.Exportar(_idJogo, VersaoJogo, _versaoEstruturaSave, filtro, ObterPlataforma());
    }
    public bool ImportarSave(string dadosCriptografados) => ImportarSave(dadosCriptografados, SaveImportMode.ReplaceSnapshot);
    public bool ImportarSave(string dadosCriptografados, SaveImportMode mode)
    {
        if (!Ativo(SaveFeatureFlags.ExportacaoManual)) return false;
        if (!ValidarSnapshot(dadosCriptografados, out var snapshot, out var error)) { Report(error); return false; }
        return AplicarSnapshotTransacional(snapshot, mode);
    }
    public string CriarBackupManual() { GarantirInicializacao(); return _saveSystem.CriarBackupManual(); }
    public string ExportarParaNuvem() { GarantirInicializacao(); return _saveSystem.ExportarParaNuvem(); }
    public void ImportarDaNuvem(string payload)
    {
        if (!ValidarSnapshot(payload, out var snapshot, out var error) || !AplicarSnapshotTransacional(snapshot))
            throw new SaveValidationException(error == SaveError.None ? SaveError.IdentityMismatch : error);
    }
    public Dictionary<string, string> ExportarParaNuvemComLimite(int maxCharsPorItem, string prefixo = "main")
    { GarantirInicializacao(); return SavePayloadChunks.Split(ExportarParaNuvem(), maxCharsPorItem, prefixo, _saveCore.ComputeHash); }
    public bool ImportarDaNuvemComLimite(IDictionary<string, string> itens, string prefixo = "main")
    {
        GarantirInicializacao();
        if (!SavePayloadChunks.TryJoin(itens, prefixo, _saveCore.ComputeHash, out var payload, out _)) return false;
        if (!ValidarSnapshot(payload, out var snapshot, out var error)) { Report(error); return false; }
        return AplicarSnapshotTransacional(snapshot);
    }

    public void ConfigurarNuvem(ISessionCloudSaveAdapter provider, Func<bool> safeHook)
    {
        GarantirInicializacao();
        _sync?.CancelarEspera();
        PodeSincronizarAgora = safeHook;
        _sync = new SaveSyncCoordinator(_saveSystem, provider, _idJogo, _versaoEstruturaSave)
        { Timeout = TimeSpan.FromSeconds(Math.Max(1, _timeoutNuvemSegundos)), Resolver = _resolvedor,
            PodeSincronizarAgora = () => !_falhaPersistencia && _identidade != null && _identidade.Matches(provider.Session) &&
                PodeSincronizarAgora != null && PodeSincronizarAgora() };
        _sync.EstadoAlterado += MudarEstado;
        _sync.ConflitoEncontrado += prompt => ConflitoEncontrado?.Invoke(prompt);
        _sync.ResetSolicitado += () => ResetSolicitado?.Invoke();
        _sync.ResetConfirmado += () => ResetConfirmado?.Invoke();
    }
    public void IdentidadeMudou(CloudSaveSession authenticatedSession)
    {
        _sync?.IdentidadeMudou();
        _identidade = authenticatedSession;
        MudarEstado(authenticatedSession == null ? SaveSyncState.WaitingForLogin : SaveSyncState.Local);
    }
    public void DefinirResolvedorDeConflito(ISaveConflictResolver resolvedor)
    { _resolvedor = resolvedor ?? new DefaultSaveConflictResolver(); if (_sync != null) _sync.Resolver = _resolvedor; }
    public void SincronizarComSite(string slot = null, Action<bool, string> onComplete = null)
    {
        if (!Ativo(SaveFeatureFlags.Nuvem) || _sync == null)
        { onComplete?.Invoke(false, "SessionProviderRequired"); return; }
        SalvarAgora();
        _sync.Synchronize(slot ?? _slotSitePadrao, r => onComplete?.Invoke(r.Success && r.Status == CloudSaveStatus.SdkSuccess, r.Error));
    }
    public void EnviarSaveParaSite(string slot = null, Action<bool, string> onComplete = null, bool forcar = false)
    { SincronizarComSite(slot, onComplete); }
    public void BaixarSaveDoSite(string slot = null, Action<bool, string> onComplete = null)
    { SincronizarComSite(slot, onComplete); }
    public void ApagarSaveNoSite(string slot = null, Action<bool, string> onComplete = null)
    { onComplete?.Invoke(false, "UseResetProgressProtocol"); }
    public void ApagarTodosOsSlotsNoSite(Action<bool, string> onComplete = null)
    { onComplete?.Invoke(false, "PhysicalDeleteRequiresProviderConfirmation"); }
    public bool SolicitarReset(string requestId, Action<CloudSaveResult> onComplete = null)
    {
        GarantirInicializacao();
        if (_politicaReset == ResetPolicy.RemoteFirst)
            return _sync != null && _sync.RequestReset(_slotSitePadrao, requestId, onComplete);
        _sync?.CancelarEspera();
        ResetSolicitado?.Invoke();
        bool ok = _saveSystem.ResetProgress(requestId);
        if (ok) ResetConfirmado?.Invoke();
        onComplete?.Invoke(ok ? CloudSaveResult.Ok() : CloudSaveResult.Fail(_saveSystem.LastError.ToString()));
        return ok;
    }
    public void ApagarTodosDados()
    {
        GarantirInicializacao();
        if (_politicaReset == ResetPolicy.RemoteFirst || !string.IsNullOrEmpty(CapturarSnapshot().Identity))
        { Report(SaveError.InvalidConfiguration); return; }
        _sync?.CancelarEspera();
        _saveSystem.ApagarTudo();
        _falhaPersistencia = false;
    }
    public string ObterIdiomaDoPortal() => _siteApi != null ? _siteApi.Idioma : "pt";
    public void ObterConfigRemotaDoSite(Action<bool, string> onComplete)
    {
        if (_siteApi == null) { onComplete?.Invoke(false, null); return; }
        _siteApi.ObterConfigRemota(result => onComplete?.Invoke(result.Success, result.Json));
    }

    public string CriarIdentidadePseudonima(string provider, string providerUserId)
    {
        GarantirInicializacao();
        return _saveCore.ComputeIntegrityHash(_idJogo + "|" + provider.Trim() + "|" + providerUserId.Trim());
    }
    public void VincularConta(string provider, string identificadorConta) { TentarVincularConta(provider, identificadorConta); }
    public bool TentarVincularConta(string provider, string identificadorConta)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(identificadorConta)) return false;
        return VincularIdentidade(provider, CriarIdentidadePseudonima(provider, identificadorConta), "");
    }
    public bool TentarVincularContaComEmailAutenticado(string provider, string identificadorConta, string emailAutenticado)
    {
        GarantirInicializacao();
        if (_modoIdentidadeConta != AccountIdentityMode.EmailAutenticado &&
            _modoIdentidadeConta != AccountIdentityMode.EmailAutenticadoComEmailVisivel) return false;
        if (string.IsNullOrWhiteSpace(emailAutenticado)) return false;
        string email = emailAutenticado.Trim().ToLowerInvariant();
        return VincularIdentidade(provider, CriarIdentidadePseudonimaPorEmail(email),
            _modoIdentidadeConta == AccountIdentityMode.EmailAutenticadoComEmailVisivel ? email : "");
    }
    public string CriarIdentidadePseudonimaPorEmail(string authenticatedEmail)
    {
        GarantirInicializacao();
        if (string.IsNullOrWhiteSpace(authenticatedEmail)) throw new ArgumentException("Verified email required.");
        return _saveCore.ComputeIntegrityHash(_idJogo + "|email|" + authenticatedEmail.Trim().ToLowerInvariant());
    }
    private bool VincularIdentidade(string provider, string identity, string visibleEmail)
    {
        GarantirInicializacao();
        if (_falhaPersistencia || !Ativo(SaveFeatureFlags.Conta) || _modoVinculoConta == AccountBindingMode.Nenhum ||
            _modoIdentidadeConta == AccountIdentityMode.Nenhum ||
            _identidade == null || _identidade.Identity != identity || _identidade.Provider != provider) return false;
        SalvarAgora();
        _sync?.CancelarEspera();
        return _saveSystem.BindIdentity(identity, provider,
            _dadosPadrao.ToDictionary(d => BuildKey(d.comando, d.chave), d => d.valor),
            _resetarSaveComConteudoSemContaAoVincular, false, visibleEmail);
    }
    public bool ConfirmarTrocaContaComReset()
    {
        GarantirInicializacao();
        if (_trocaDeConta != AccountSwitchPolicy.RequireExplicitReset || _identidade == null ||
            _falhaPersistencia || !Ativo(SaveFeatureFlags.Conta)) return false;
        SalvarAgora();
        _sync?.CancelarEspera();
        return _saveSystem.BindIdentity(_identidade.Identity, _identidade.Provider,
            _dadosPadrao.ToDictionary(d => BuildKey(d.comando, d.chave), d => d.valor), true, true);
    }
    private bool TemProgresso()
    {
        var defaults = _dadosPadrao.ToDictionary(d => BuildKey(d.comando, d.chave), d => d.valor);
        foreach (var pair in CapturarSnapshot().Data)
            if (_saveSystem.ScopeOf(pair.Key) == SaveScope.Progress &&
                (!defaults.TryGetValue(pair.Key, out var value) || pair.Value != value)) return true;
        return false;
    }
    private bool ValidarDono(ValidatedSaveSnapshot snapshot)
    {
        if (snapshot == null) return false;
        if (!Ativo(SaveFeatureFlags.Conta) || _modoVinculoConta == AccountBindingMode.Nenhum)
            return snapshot.Identity == "";
        if (snapshot.Identity != "") return _identidade != null && _identidade.Identity == snapshot.Identity;
        return _modoVinculoConta != AccountBindingMode.Obrigatorio && CapturarSnapshot().Identity == "";
    }
    private void GarantirContaPadrao()
    {
        if (!Ativo(SaveFeatureFlags.Conta)) return;
        if (!_saveSystem.ContainsKey(Conta + "accountHash]")) _saveSystem.Enqueue(Conta + "accountHash]", "nao_vinculado", PriorityLevel.Normal);
        if (!_saveSystem.ContainsKey(Conta + "accountStatus]")) _saveSystem.Enqueue(Conta + "accountStatus]", "nao_conectado", PriorityLevel.Normal);
    }
    public void MarcarContaNaoConectada() { IdentidadeMudou(null); }
    public string ObterStatusConta() => _identidade == null ? "nao_conectado" : "conectado";
    public string ObterProviderConta() { GarantirInicializacao(); return _saveSystem.Get(Conta + "accountProvider]") ?? "nenhum"; }
    public string ObterHashConta() { GarantirInicializacao(); return _saveSystem.Get(Conta + "accountHash]") ?? "nao_vinculado"; }
    private void AplicarPadroes()
    {
        foreach (var d in _dadosPadrao) _saveSystem.Enqueue(BuildKey(d.comando, d.chave), d.valor, d.prioridade);
    }

    public void RegistrarGanho(string recurso, long quantidade, long saldoAntes, long saldoDepois, string origem, string referencia = "")
        => RegistrarAuditoria("ganho", recurso, quantidade.ToString(), saldoAntes.ToString(), saldoDepois.ToString(), origem, referencia);
    public void RegistrarGasto(string recurso, long quantidade, long saldoAntes, long saldoDepois, string origem, string referencia = "")
        => RegistrarAuditoria("gasto", recurso, quantidade.ToString(), saldoAntes.ToString(), saldoDepois.ToString(), origem, referencia);
    public void RegistrarUso(string recurso, long quantidade, long saldoAntes, long saldoDepois, string origem, string referencia = "")
        => RegistrarAuditoria("uso", recurso, quantidade.ToString(), saldoAntes.ToString(), saldoDepois.ToString(), origem, referencia);
    public void RegistrarCompra(string recurso, long quantidade, long saldoAntes, long saldoDepois, string origem, string referencia = "")
        => RegistrarAuditoria("compra", recurso, quantidade.ToString(), saldoAntes.ToString(), saldoDepois.ToString(), origem, referencia);
    public void RegistrarAuditoria(string tipo, string recurso, string quantidade, string saldoAntes, string saldoDepois, string origem, string referencia = "")
    {
        if (!Ativo(SaveFeatureFlags.Auditoria)) return;
        GarantirInicializacao();
        var record = new AuditRecord { utc = DateTime.UtcNow.ToString("O"), type = tipo, resource = recurso,
            quantity = quantidade, before = saldoAntes, after = saldoDepois, source = origem, reference = referencia };
        _saveSystem.Enqueue(BuildKey("SLG0200", Guid.NewGuid().ToString("N")), JsonUtility.ToJson(record), PriorityLevel.Baixo);
    }
    [Serializable] private sealed class AuditRecord
    { public string utc, type, resource, quantity, before, after, source, reference; }
    public int ReconciliarComprasDaPlataforma(IEnumerable<PlatformPurchaseReceipt> receipts)
    {
        if (!Ativo(SaveFeatureFlags.Compras) || receipts == null) return 0;
        GarantirInicializacao();
        int count = 0;
        var seen = new HashSet<string>();
        foreach (var receipt in receipts)
        {
            if (!receipt.owned || string.IsNullOrEmpty(receipt.productId) || string.IsNullOrEmpty(receipt.transactionId)) continue;
            string key = BuildKey("SLG0100", receipt.productId);
            if (_saveSystem.ContainsKey(key) || !seen.Add(key)) continue;
            _saveSystem.Enqueue(key, "owned", PriorityLevel.Normal); count++;
        }
        return count;
    }
    public bool CompraEstaRegistrada(string productId) { GarantirInicializacao(); return _saveSystem.Get(BuildKey("SLG0100", productId)) == "owned"; }
    public List<string> ExportarSaveCompletoEmPartesTexto(int maxCharsPorParte)
    {
        if (!Ativo(SaveFeatureFlags.MultipartTexto)) throw new SaveValidationException(SaveError.Unavailable);
        return SavePayloadChunks.SplitForTextTransfer(ExportarSaveCompleto(), maxCharsPorParte);
    }
    public SavePayloadChunks.TextPartsResult AnalisarPartesTexto(IEnumerable<string> partes) => SavePayloadChunks.AnalyzeTextParts(partes);
    public bool ImportarSaveDePartesTexto(IEnumerable<string> partes) => Ativo(SaveFeatureFlags.MultipartTexto) &&
        SavePayloadChunks.TryJoinTextParts(partes, out var payload, out _, out _) && ImportarSave(payload);
    public List<string> ExportarSaveCompletoParaArquivosEmPartes(int maxCharsPorParte, string directoryPath = null)
    {
        if (!Ativo(SaveFeatureFlags.MultipartArquivos)) throw new SaveValidationException(SaveError.Unavailable);
        string dir = directoryPath ?? Path.Combine(Application.persistentDataPath, "SaveExports", _idJogo, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var parts = SavePayloadChunks.SplitForTextTransfer(ExportarSaveCompleto(), maxCharsPorParte);
        var paths = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        { string path = Path.Combine(dir, (i + 1).ToString("D3") + ".savepart"); File.WriteAllText(path, parts[i]); paths.Add(path); }
        return paths;
    }
    public bool ImportarSaveDeArquivosEmPartes(IEnumerable<string> filePaths)
    {
        if (!Ativo(SaveFeatureFlags.MultipartArquivos) || filePaths == null) return false;
        var parts = new List<string>(); long total = 0;
        foreach (string file in filePaths)
        {
            var info = new FileInfo(file);
            total += info.Length;
            if (parts.Count >= SavePayloadChunks.MaxParts || total > SavePayloadChunks.MaxTransferChars) return false;
            parts.Add(File.ReadAllText(file));
        }
        return SavePayloadChunks.TryJoinTextParts(parts, out var payload, out _, out _) && ImportarSave(payload);
    }
    public string ObterCaminhoExportacaoPadrao() => Path.Combine(Application.persistentDataPath, "SaveExports", _idJogo, "manual.save");
    public string ExportarSaveCompletoParaArquivo(string filePath = null)
    {
        string path = filePath ?? ObterCaminhoExportacaoPadrao();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, ExportarSaveCompleto()); return path;
    }
    public bool ImportarSaveDeArquivo(string filePath)
    {
        if (new FileInfo(filePath).Length > _limitePayloadBytes) { Report(SaveError.LimitExceeded); return false; }
        return ImportarSave(File.ReadAllText(filePath));
    }
    public string ObterPlataforma() => _usarDeteccaoAutomatica ? Application.platform.ToString() : _plataformaManual;
    private void Report(SaveError error) { UltimoErro = error; MudarEstado(SaveSyncState.Failed); ErroTipado?.Invoke(error); }
    private void MudarEstado(SaveSyncState state) { if (Estado != state) { Estado = state; EstadoAlterado?.Invoke(state); } }
}
