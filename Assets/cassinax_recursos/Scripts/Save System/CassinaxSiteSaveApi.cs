// Cassinax Unity System Save
// Version: v0.10.0
// Status: integration-pilot

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace cassinax.savesystem
{
    /// <summary>Contexto publicado pela pagina do jogo no site Cassinax.</summary>
    public struct CassinaxSiteContexto
    {
        public int JogoId;
        public bool UsuarioLogado;
        public string CsrfToken;
        public string Idioma;
        public string Build;
        public string GameConfigKey;

        public bool PodeSalvar => UsuarioLogado && JogoId > 0;
    }

    /// <summary>Resultado da consulta de configuracao remota do jogo.</summary>
    public struct CassinaxGameConfigResult
    {
        public bool Success;
        public bool NotModified;
        public string Json;
        public string ETag;
        public string Error;
        public long StatusCode;
    }

    /// <summary>
    /// Cliente HTTP das APIs de jogo do site Cassinax e implementacao de
    /// <see cref="IAsyncCloudSaveAdapter"/> para o portal.
    ///
    /// Endpoints usados:
    ///   POST /jogos/api/salvar.php
    ///   GET  /jogos/api/carregar.php?jogo_id=[id]&amp;slot=[slot]
    ///   POST /jogos/api/apagar.php
    ///   GET  /jogos/api/csrf_token.php
    ///   GET  /jogos/api/check-login.php
    ///   GET  /api/v1/game-config/[gameKey]
    ///
    /// Este script e fixo da API: nao coloque regra de jogo aqui. Quem decide
    /// quando sincronizar e o <c>SaveAdapter</c> do projeto.
    /// </summary>
    [AddComponentMenu("Cassinax/Save System/Cassinax Site Save Api")]
    public class CassinaxSiteSaveApi : MonoBehaviour, IAsyncCloudSaveAdapter
    {
        //------------------------------------------------------------- Constantes

        private const string ROTA_SALVAR = "/jogos/api/salvar.php";
        private const string ROTA_CARREGAR = "/jogos/api/carregar.php";
        private const string ROTA_APAGAR = "/jogos/api/apagar.php";
        private const string ROTA_CSRF = "/jogos/api/csrf_token.php";
        private const string ROTA_CHECK_LOGIN = "/jogos/api/check-login.php";
        private const string ROTA_GAME_CONFIG = "/api/v1/game-config/";

        private const string PREF_ETAG = "Cassinax.GameConfig.ETag.";
        private const string PREF_CONFIG = "Cassinax.GameConfig.Json.";

        /// <summary>Limite total por slot aceito pelo site: 64 MB.</summary>
        public const int LIMITE_BYTES_POR_SLOT = 64 * 1024 * 1024;

        /// <summary>Limite por chunk aceito pelo site: ~16 MB.</summary>
        public const int LIMITE_BYTES_POR_CHUNK = 16 * 1024 * 1024;

        /// <summary>Maximo de chunks aceito pelo site.</summary>
        public const int MAXIMO_DE_CHUNKS = 512;

        private static readonly Regex SlotValidoRegex =
            new Regex(@"^[A-Za-z0-9._:-]{1,80}$", RegexOptions.CultureInvariant);

        private static readonly string[] IdiomasSuportados = { "pt", "en", "es" };

        //------------------------------------------------------------- Configuracao

        [Header("Origem")]
        [Tooltip("Vazio usa a mesma origem da pagina (obrigatorio em WebGL publicado). " +
                 "No Editor, informe a origem completa. Ex: https://cassinax.com")]
        [SerializeField] private string _baseUrl = "";

        [Header("Contexto fora do WebGL")]
        [Tooltip("ID do jogo usado no Editor e em builds nativas. Em WebGL vem de window.JOGO_ID.")]
        [SerializeField] private int _jogoIdManual = 0;

        [Tooltip("Chave de configuracao remota usada fora do WebGL. Em WebGL vem de window.GAME_CONFIG_KEY.")]
        [SerializeField] private string _gameConfigKeyManual = "";

        [Tooltip("Simula usuario logado no Editor. Em WebGL vem de window.USUARIO_LOGADO.")]
        [SerializeField] private bool _usuarioLogadoManual = false;

        [Header("Rede")]
        [Tooltip("Tempo maximo por tentativa, em segundos. Ignorado pelo navegador em WebGL.")]
        [SerializeField] private int _timeoutSegundos = 30;

        [Tooltip("Numero de tentativas por operacao. Refaz apenas erro de conexao e erro 5xx.")]
        [SerializeField] private int _tentativas = 3;

        [Tooltip("Espera base entre tentativas, em segundos.")]
        [SerializeField] private float _esperaEntreTentativas = 2f;

        [Tooltip("Dobra a espera a cada tentativa e sorteia o valor final dentro da janela. " +
                 "Evita que muitos jogadores voltem ao servidor no mesmo instante.")]
        [SerializeField] private bool _backoffExponencial = true;

        [Tooltip("Teto da espera entre tentativas, em segundos.")]
        [SerializeField] private float _esperaMaximaEntreTentativas = 30f;

        [Header("Payload")]
        [Tooltip("Acima deste tamanho em bytes, o save e enviado no modo chunked do site.")]
        [SerializeField] private int _bytesParaUsarChunked = 8 * 1024 * 1024;

        [Tooltip("Se ativado, valida o payload_hash devolvido pelo site ao carregar.")]
        [SerializeField] private bool _validarHashAoCarregar = true;

        [Header("Diagnostico")]
        [Tooltip("Registra no console cada chamada e resultado das APIs do site.")]
        [SerializeField] private bool _logDetalhado = false;

        //------------------------------------------------------------- Estado

        private static CassinaxSiteSaveApi _instancia;

        private CassinaxSiteContexto _contexto;
        private string _csrfTokenBuscado;
        private string _configGameKeyEmMemoria;
        private string _configJsonEmMemoria;
        private string _configETagEmMemoria;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string CassinaxObterContextoJson();
#endif

        public static CassinaxSiteSaveApi ObterInstancia
        {
            get
            {
                if (_instancia == null)
#if UNITY_2022_2_OR_NEWER
                    // Preserve the previous first-instance choice on duplicate components.
#pragma warning disable CS0618
                    _instancia = FindFirstObjectByType<CassinaxSiteSaveApi>();
#pragma warning restore CS0618
#else
                    _instancia = FindObjectOfType<CassinaxSiteSaveApi>();
#endif

                return _instancia;
            }
        }

        /// <summary>Contexto atual da pagina. Atualizado no Awake e antes de cada chamada.</summary>
        public CassinaxSiteContexto Contexto => _contexto;

        /// <summary>Idioma do portal validado. Sempre pt, en ou es.</summary>
        public string Idioma => NormalizarIdioma(_contexto.Idioma);

        /// <summary>Build publicada pelo site, usada para cache busting. Pode vir vazia.</summary>
        public string Build => _contexto.Build ?? string.Empty;

        /// <summary>Chave do jogo para a configuracao remota.</summary>
        public string GameConfigKey =>
            string.IsNullOrWhiteSpace(_contexto.GameConfigKey) ? _gameConfigKeyManual : _contexto.GameConfigKey;

        /// <summary>True quando existe usuario logado e ID de jogo valido.</summary>
        public bool IsAvailable => _contexto.PodeSalvar;

        //------------------------------------------------------------- Ciclo de vida

        private void Awake()
        {
            if (_instancia == null)
                _instancia = this;

            AtualizarContexto();
        }

        /// <summary>
        /// Rele o contexto da pagina. Em WebGL publicado usa a ponte
        /// <c>CassinaxContexto.jslib</c>; fora dele usa os campos manuais do Inspector.
        /// </summary>
        public void AtualizarContexto()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                _contexto = LerContexto(CassinaxObterContextoJson());

                // A pagina e a fonte preferida do token. Se ela nao expoe a meta tag,
                // reaproveita o token ja obtido em /jogos/api/csrf_token.php.
                if (string.IsNullOrEmpty(_contexto.CsrfToken))
                    _contexto.CsrfToken = _csrfTokenBuscado;

                return;
            }
            catch (Exception)
            {
                Debug.LogError("[CassinaxSiteSaveApi] ContextReadFailed");
            }
#endif
            _contexto = new CassinaxSiteContexto
            {
                JogoId = _jogoIdManual,
                UsuarioLogado = _usuarioLogadoManual,
                CsrfToken = _csrfTokenBuscado,
                Idioma = string.IsNullOrWhiteSpace(_contexto.Idioma) ? "pt" : _contexto.Idioma,
                Build = _contexto.Build,
                GameConfigKey = _gameConfigKeyManual
            };
        }

        private CassinaxSiteContexto LerContexto(string json)
        {
            var contexto = new CassinaxSiteContexto();

            SaveJsonMinimo.TryLerInt(json, "jogoId", out contexto.JogoId);
            SaveJsonMinimo.TryLerBool(json, "usuarioLogado", out contexto.UsuarioLogado);
            SaveJsonMinimo.TryLerString(json, "csrfToken", out contexto.CsrfToken);
            SaveJsonMinimo.TryLerString(json, "idioma", out contexto.Idioma);
            SaveJsonMinimo.TryLerString(json, "build", out contexto.Build);
            SaveJsonMinimo.TryLerString(json, "gameConfigKey", out contexto.GameConfigKey);

            return contexto;
        }

        //------------------------------------------------------------- IAsyncCloudSaveAdapter

        /// <summary>Envia o payload completo do save para o slot informado.</summary>
        public void UploadSave(string slotId, string payload, Action<CloudSaveResult> onComplete)
        {
            UploadSave(slotId, payload, 0, 0, onComplete);
        }

        /// <summary>
        /// Envia o payload completo informando tambem as versoes de estrutura e de Core,
        /// que o site guarda junto do save para permitir migracao no cliente.
        /// </summary>
        public void UploadSave(string slotId, string payload, int structureVersion, int coreVersion, Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(UploadSaveRotina(slotId, payload, structureVersion, coreVersion, onComplete));
        }

        /// <summary>Baixa o payload do slot. Sucesso com Payload vazio significa "sem save no site".</summary>
        public void DownloadSave(string slotId, Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(DownloadSaveRotina(slotId, onComplete));
        }

        /// <summary>Apaga o save de um slot. Use somente com confirmacao explicita do jogador.</summary>
        public void DeleteSave(string slotId, Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(DeleteSaveRotina(slotId, false, onComplete));
        }

        /// <summary>Apaga todos os slots do jogo. Use somente com confirmacao explicita do jogador.</summary>
        public void DeleteAllSlots(Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(DeleteSaveRotina(null, true, onComplete));
        }

        /// <summary>Consulta se a sessao do site continua logada.</summary>
        public void VerificarLogin(Action<bool> onComplete)
        {
            StartCoroutine(VerificarLoginRotina(onComplete));
        }

        /// <summary>
        /// Consulta a configuracao remota do jogo. Devolve o JSON cru para o projeto
        /// interpretar, mais o ETag usado no cache.
        /// </summary>
        public void ObterConfigRemota(Action<CassinaxGameConfigResult> onComplete)
        {
            StartCoroutine(ObterConfigRemotaRotina(GameConfigKey, onComplete));
        }

        //------------------------------------------------------------- Rotinas

        private IEnumerator UploadSaveRotina(string slotId, string payload, int structureVersion, int coreVersion, Action<CloudSaveResult> onComplete)
        {
            AtualizarContexto();

            if (!ValidarContextoParaEscrita(out string erroContexto))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroContexto));
                yield break;
            }

            if (!TryNormalizarSlot(slotId, out string slot, out string erroSlot))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroSlot));
                yield break;
            }

            payload = payload ?? string.Empty;
            int totalBytes = Encoding.UTF8.GetByteCount(payload);

            if (totalBytes > LIMITE_BYTES_POR_SLOT)
            {
                Responder(onComplete, CloudSaveResult.Fail(
                    $"Save de {totalBytes} bytes excede o limite de {LIMITE_BYTES_POR_SLOT} bytes por slot do site."));
                yield break;
            }

            yield return GarantirCsrf();

            if (string.IsNullOrEmpty(_contexto.CsrfToken))
            {
                Responder(onComplete, CloudSaveResult.Fail("Token CSRF indisponivel."));
                yield break;
            }

            string hash = CalcularSha256(payload);
            string corpo;

            if (totalBytes > _bytesParaUsarChunked)
            {
                if (!TryMontarCorpoChunked(slot, payload, hash, totalBytes, structureVersion, coreVersion, out corpo, out string erroChunk))
                {
                    Responder(onComplete, CloudSaveResult.Fail(erroChunk));
                    yield break;
                }
            }
            else
            {
                corpo = MontarCorpoSimples(slot, payload, hash, totalBytes, structureVersion, coreVersion);
            }

            yield return EnviarComTentativas(
                () => CriarPost(ROTA_SALVAR, corpo),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        Responder(onComplete, resultado);
                        return;
                    }

                    Responder(onComplete, InterpretarSucesso(resultado, payload));
                });
        }

        private IEnumerator DownloadSaveRotina(string slotId, Action<CloudSaveResult> onComplete)
        {
            AtualizarContexto();

            if (!ValidarContextoParaLeitura(out string erroContexto))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroContexto));
                yield break;
            }

            if (!TryNormalizarSlot(slotId, out string slot, out string erroSlot))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroSlot));
                yield break;
            }

            string url = MontarUrl($"{ROTA_CARREGAR}?jogo_id={_contexto.JogoId}&slot={UnityWebRequest.EscapeURL(slot)}");

            yield return EnviarComTentativas(
                () => CriarGet(url),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        Responder(onComplete, resultado);
                        return;
                    }

                    Responder(onComplete, InterpretarCarregamento(resultado));
                });
        }

        private IEnumerator DeleteSaveRotina(string slotId, bool todosSlots, Action<CloudSaveResult> onComplete)
        {
            AtualizarContexto();

            if (!ValidarContextoParaEscrita(out string erroContexto))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroContexto));
                yield break;
            }

            string slot = "main";
            if (!todosSlots && !TryNormalizarSlot(slotId, out slot, out string erroSlot))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroSlot));
                yield break;
            }

            yield return GarantirCsrf();

            if (string.IsNullOrEmpty(_contexto.CsrfToken))
            {
                Responder(onComplete, CloudSaveResult.Fail("Token CSRF indisponivel."));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"jogo_id\":").Append(_contexto.JogoId).Append(',');
            sb.Append("\"csrf_token\":\"").Append(SaveJsonMinimo.Escapar(_contexto.CsrfToken)).Append('"');

            if (todosSlots)
                sb.Append(",\"todos_slots\":true");
            else
                sb.Append(",\"slot\":\"").Append(SaveJsonMinimo.Escapar(slot)).Append('"');

            sb.Append('}');

            string corpo = sb.ToString();

            yield return EnviarComTentativas(
                () => CriarPost(ROTA_APAGAR, corpo),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        Responder(onComplete, resultado);
                        return;
                    }

                    Responder(onComplete, InterpretarSucesso(resultado, null));
                });
        }

        private IEnumerator VerificarLoginRotina(Action<bool> onComplete)
        {
            AtualizarContexto();

            yield return EnviarComTentativas(
                () => CriarGet(MontarUrl(ROTA_CHECK_LOGIN)),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        onComplete?.Invoke(false);
                        return;
                    }

                    string json = resultado.Payload ?? string.Empty;
                    bool logado;

                    if (!SaveJsonMinimo.TryLerBool(json, "logado", out logado) &&
                        !SaveJsonMinimo.TryLerBool(json, "usuario_logado", out logado) &&
                        !SaveJsonMinimo.TryLerBool(json, "logged_in", out logado))
                    {
                        SaveJsonMinimo.TryLerBool(json, "sucesso", out logado);
                    }

                    onComplete?.Invoke(logado);
                });
        }

        private IEnumerator ObterConfigRemotaRotina(string gameKey, Action<CassinaxGameConfigResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                onComplete?.Invoke(new CassinaxGameConfigResult
                {
                    Success = false,
                    Error = "gameKey da configuracao remota nao definida."
                });
                yield break;
            }

            string etagConhecida = ObterETagSalva(gameKey);
            string url = MontarUrl(ROTA_GAME_CONFIG + UnityWebRequest.EscapeURL(gameKey));

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(1, _timeoutSegundos);

                if (!string.IsNullOrEmpty(etagConhecida))
                    req.SetRequestHeader("If-None-Match", etagConhecida);

                yield return req.SendWebRequest();

                long status = req.responseCode;

                if (status == 304)
                {
                    onComplete?.Invoke(new CassinaxGameConfigResult
                    {
                        Success = true,
                        NotModified = true,
                        Json = ObterConfigSalva(gameKey),
                        ETag = etagConhecida,
                        StatusCode = status
                    });
                    yield break;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Config remota indisponivel nao deve derrubar o jogo:
                    // devolve o ultimo JSON conhecido quando existir.
                    string cache = ObterConfigSalva(gameKey);

                    onComplete?.Invoke(new CassinaxGameConfigResult
                    {
                        Success = !string.IsNullOrEmpty(cache),
                        NotModified = !string.IsNullOrEmpty(cache),
                        Json = cache,
                        ETag = etagConhecida,
                        Error = DescreverErro(req),
                        StatusCode = status
                    });
                    yield break;
                }

                string json = req.downloadHandler.text;
                string etag = req.GetResponseHeader("ETag");

                if (string.IsNullOrEmpty(etag))
                    etag = req.GetResponseHeader("X-Cassinax-Config-ETag");

                SalvarConfig(gameKey, json, etag);

                if (_logDetalhado)
                    Debug.Log("[CassinaxSiteSaveApi] ConfigUpdated");

                onComplete?.Invoke(new CassinaxGameConfigResult
                {
                    Success = true,
                    NotModified = false,
                    Json = json,
                    ETag = etag,
                    StatusCode = status
                });
            }
        }

        private IEnumerator GarantirCsrf()
        {
            if (!string.IsNullOrEmpty(_contexto.CsrfToken))
                yield break;

            using (UnityWebRequest req = UnityWebRequest.Get(MontarUrl(ROTA_CSRF)))
            {
                req.timeout = Mathf.Max(1, _timeoutSegundos);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[CassinaxSiteSaveApi] CsrfUnavailable HTTP {req.responseCode}");
                    yield break;
                }

                string json = req.downloadHandler.text ?? string.Empty;

                if (SaveJsonMinimo.TryLerString(json, "csrf_token", out string token) ||
                    SaveJsonMinimo.TryLerString(json, "csrfToken", out token) ||
                    SaveJsonMinimo.TryLerString(json, "token", out token))
                {
                    _csrfTokenBuscado = token;
                    _contexto.CsrfToken = token;
                }
            }
        }

        //------------------------------------------------------------- HTTP

        private UnityWebRequest CriarGet(string url)
        {
            UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = Mathf.Max(1, _timeoutSegundos);
            return req;
        }

        private UnityWebRequest CriarPost(string rota, string corpoJson)
        {
            var req = new UnityWebRequest(MontarUrl(rota), UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(corpoJson));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, _timeoutSegundos);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-CSRF-Token", _contexto.CsrfToken ?? string.Empty);
            return req;
        }

        /// <summary>
        /// Executa a requisicao repetindo apenas falhas transitorias: erro de conexao e HTTP 5xx.
        /// Erros de regra (400, 401, 413, 422) nao sao repetidos.
        /// </summary>
        private IEnumerator EnviarComTentativas(Func<UnityWebRequest> criarRequisicao, Action<CloudSaveResult> onComplete)
        {
            int maximo = Mathf.Max(1, _tentativas);
            CloudSaveResult ultimo = CloudSaveResult.Fail("Requisicao nao executada.");

            for (int tentativa = 1; tentativa <= maximo; tentativa++)
            {
                using (UnityWebRequest req = criarRequisicao())
                {
                    if (_logDetalhado)
                        Debug.Log($"[CassinaxSiteSaveApi] HTTP attempt {tentativa}/{maximo}");

                    yield return req.SendWebRequest();

                    long status = req.responseCode;
                    bool conexaoOk = req.result == UnityWebRequest.Result.Success;

                    if (conexaoOk)
                    {
                        onComplete?.Invoke(new CloudSaveResult
                        {
                            Success = true,
                            Payload = req.downloadHandler != null ? req.downloadHandler.text : string.Empty,
                            StatusCode = status
                        });
                        yield break;
                    }

                    ultimo = CloudSaveResult.Fail(DescreverErro(req), status);

                    bool transitorio = req.result == UnityWebRequest.Result.ConnectionError || status >= 500;
                    if (!transitorio || tentativa == maximo)
                        break;
                }

                yield return new WaitForSecondsRealtime(CalcularEspera(tentativa));
            }

            onComplete?.Invoke(ultimo);
        }

        /// <summary>
        /// Espera antes da proxima tentativa.
        ///
        /// Com backoff exponencial, a janela dobra a cada tentativa e o valor final e
        /// sorteado dentro dela. Dobrar reduz a pressao sobre um servidor que ja esta
        /// sobrecarregado; o sorteio impede que varios jogadores que falharam no mesmo
        /// instante voltem juntos e repitam a sobrecarga.
        /// </summary>
        private float CalcularEspera(int tentativa)
        {
            float baseEspera = Mathf.Max(0f, _esperaEntreTentativas);

            if (!_backoffExponencial || baseEspera <= 0f)
                return baseEspera;

            float teto = Mathf.Max(baseEspera, _esperaMaximaEntreTentativas);
            float janela = Mathf.Min(baseEspera * Mathf.Pow(2f, tentativa - 1), teto);

            return UnityEngine.Random.Range(janela * 0.5f, janela);
        }

        //------------------------------------------------------------- Interpretacao das respostas

        /// <summary>Le sucesso/erro de uma resposta de salvar ou apagar.</summary>
        private CloudSaveResult InterpretarSucesso(CloudSaveResult bruto, string payloadEnviado)
        {
            string json = bruto.Payload ?? string.Empty;

            if (!SaveJsonMinimo.TryLerBool(json, "sucesso", out bool sucesso))
            {
                return CloudSaveResult.Fail(
                    $"Resposta do site em formato inesperado: {Resumir(json)}", bruto.StatusCode);
            }

            if (!sucesso)
            {
                SaveJsonMinimo.TryLerString(json, "erro", out string erro);
                return CloudSaveResult.Fail(
                    string.IsNullOrEmpty(erro) ? "O site rejeitou a operacao." : erro, bruto.StatusCode);
            }

            return CloudSaveResult.Ok(payloadEnviado, bruto.StatusCode);
        }

        /// <summary>Le o payload de uma resposta de carregar.</summary>
        private CloudSaveResult InterpretarCarregamento(CloudSaveResult bruto)
        {
            string json = bruto.Payload ?? string.Empty;

            if (!SaveJsonMinimo.TryLerBool(json, "sucesso", out bool sucesso))
            {
                return CloudSaveResult.Fail(
                    $"Resposta do site em formato inesperado: {Resumir(json)}", bruto.StatusCode);
            }

            if (!sucesso)
            {
                SaveJsonMinimo.TryLerString(json, "erro", out string erro);
                return CloudSaveResult.Fail(
                    string.IsNullOrEmpty(erro) ? "O site rejeitou a leitura do save." : erro, bruto.StatusCode);
            }

            // Sem save gravado ainda: sucesso com payload vazio.
            if (SaveJsonMinimo.ValorEhNulo(json, "dados") || !SaveJsonMinimo.ContemChave(json, "dados"))
                return CloudSaveResult.Ok(null, bruto.StatusCode);

            if (!SaveJsonMinimo.TryLerString(json, "payload", out string payload) || string.IsNullOrEmpty(payload))
            {
                return CloudSaveResult.Fail(
                    "O save no site nao tem 'dados.payload'. Ele provavelmente foi gravado por outro cliente.",
                    bruto.StatusCode);
            }

            if (_validarHashAoCarregar &&
                SaveJsonMinimo.TryLerString(json, "payload_hash", out string hashRemoto) &&
                !string.IsNullOrEmpty(hashRemoto))
            {
                string hashLocal = CalcularSha256(payload);
                if (!string.Equals(hashLocal, hashRemoto, StringComparison.OrdinalIgnoreCase))
                {
                    return CloudSaveResult.Fail(
                        "Hash do payload baixado nao confere. O save pode ter sido truncado.", bruto.StatusCode);
                }
            }

            var resultado = CloudSaveResult.Ok(payload, bruto.StatusCode);
            SaveJsonMinimo.TryLerInt(json, "structureVersion", out resultado.StructureVersion);
            SaveJsonMinimo.TryLerInt(json, "coreVersion", out resultado.CoreVersion);

            return resultado;
        }

        //------------------------------------------------------------- Montagem do corpo

        private string MontarCorpoSimples(string slot, string payload, string hash, int totalBytes, int structureVersion, int coreVersion)
        {
            var sb = new StringBuilder(payload.Length + 512);

            sb.Append('{');
            sb.Append("\"jogo_id\":").Append(_contexto.JogoId).Append(',');
            sb.Append("\"slot\":\"").Append(SaveJsonMinimo.Escapar(slot)).Append("\",");
            sb.Append("\"csrf_token\":\"").Append(SaveJsonMinimo.Escapar(_contexto.CsrfToken)).Append("\",");
            sb.Append("\"dados\":{");
            sb.Append("\"payload\":\"").Append(SaveJsonMinimo.Escapar(payload)).Append("\",");
            sb.Append("\"payload_hash\":\"").Append(hash).Append("\",");
            sb.Append("\"payload_bytes\":").Append(totalBytes.ToString(CultureInfo.InvariantCulture));
            AnexarVersoes(sb, structureVersion, coreVersion);
            sb.Append('}');
            sb.Append('}');

            return sb.ToString();
        }

        private bool TryMontarCorpoChunked(
            string slot,
            string payload,
            string hash,
            int totalBytes,
            int structureVersion,
            int coreVersion,
            out string corpo,
            out string error)
        {
            corpo = null;
            error = null;

            // O payload exportado e Base64 com prefixo ASCII, entao caractere equivale a byte.
            int maxCharsPorChunk = Mathf.Min(_bytesParaUsarChunked, LIMITE_BYTES_POR_CHUNK);
            if (maxCharsPorChunk <= 0)
            {
                error = "Limite de bytes por chunk invalido.";
                return false;
            }

            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(payload.Length / (float)maxCharsPorChunk));

            if (totalChunks > MAXIMO_DE_CHUNKS)
            {
                error = $"Save exigiria {totalChunks} chunks, acima do maximo de {MAXIMO_DE_CHUNKS} do site.";
                return false;
            }

            var sb = new StringBuilder(payload.Length + 1024);

            sb.Append('{');
            sb.Append("\"jogo_id\":").Append(_contexto.JogoId).Append(',');
            sb.Append("\"slot\":\"").Append(SaveJsonMinimo.Escapar(slot)).Append("\",");
            sb.Append("\"csrf_token\":\"").Append(SaveJsonMinimo.Escapar(_contexto.CsrfToken)).Append("\",");
            sb.Append("\"dados\":{");
            sb.Append("\"chunked\":true,");
            sb.Append("\"chunks\":[");

            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * maxCharsPorChunk;
                int length = Mathf.Min(maxCharsPorChunk, payload.Length - start);

                if (i > 0)
                    sb.Append(',');

                sb.Append("{\"index\":").Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"payload\":\"").Append(SaveJsonMinimo.Escapar(payload.Substring(start, length)));
                sb.Append("\"}");
            }

            sb.Append("],");
            sb.Append("\"meta\":{\"totalChunks\":").Append(totalChunks.ToString(CultureInfo.InvariantCulture)).Append("},");
            sb.Append("\"payload_hash\":\"").Append(hash).Append("\",");
            sb.Append("\"payload_bytes\":").Append(totalBytes.ToString(CultureInfo.InvariantCulture));
            AnexarVersoes(sb, structureVersion, coreVersion);
            sb.Append('}');
            sb.Append('}');

            corpo = sb.ToString();
            return true;
        }

        private void AnexarVersoes(StringBuilder sb, int structureVersion, int coreVersion)
        {
            if (structureVersion > 0)
                sb.Append(",\"structureVersion\":").Append(structureVersion.ToString(CultureInfo.InvariantCulture));

            if (coreVersion > 0)
                sb.Append(",\"coreVersion\":").Append(coreVersion.ToString(CultureInfo.InvariantCulture));
        }

        //------------------------------------------------------------- Utilitarios

        private bool ValidarContextoParaEscrita(out string error)
        {
            if (!_contexto.UsuarioLogado)
            {
                error = "Usuario nao esta logado no site.";
                return false;
            }

            if (_contexto.JogoId <= 0)
            {
                error = "ID do jogo indisponivel. Verifique window.JOGO_ID na pagina.";
                return false;
            }

            error = null;
            return true;
        }

        private bool ValidarContextoParaLeitura(out string error)
        {
            return ValidarContextoParaEscrita(out error);
        }

        private bool TryNormalizarSlot(string slotId, out string slot, out string error)
        {
            slot = string.IsNullOrWhiteSpace(slotId) ? "main" : slotId.Trim();
            error = null;

            if (SlotValidoRegex.IsMatch(slot))
                return true;

            error = $"Slot '{slot}' invalido. Use ate 80 caracteres entre A-Z a-z 0-9 . _ : -";
            slot = null;
            return false;
        }

        private string MontarUrl(string rotaOuUrl)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                return rotaOuUrl;

            return _baseUrl.TrimEnd('/') + rotaOuUrl;
        }

        private static string CalcularSha256(string texto)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(texto ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));

                return sb.ToString();
            }
        }

        private static string NormalizarIdioma(string idioma)
        {
            string normalizado = (idioma ?? string.Empty).Trim().ToLowerInvariant();

            foreach (string suportado in IdiomasSuportados)
            {
                if (normalizado == suportado)
                    return suportado;
            }

            return "pt";
        }

        private string DescreverErro(UnityWebRequest req)
        {
            string corpo = req.downloadHandler != null ? req.downloadHandler.text : null;

            if (!string.IsNullOrEmpty(corpo) &&
                SaveJsonMinimo.TryLerString(corpo, "erro", out string erroApi) &&
                !string.IsNullOrEmpty(erroApi))
            {
                return $"HTTP {req.responseCode}: {erroApi}";
            }

            return $"HTTP {req.responseCode}: {req.error}";
        }

        private static string Resumir(string texto)
        {
            texto = (texto ?? string.Empty).Replace("\n", " ").Replace("\r", " ");
            return texto.Length <= 200 ? texto : texto.Substring(0, 200) + "...";
        }

        private void Responder(Action<CloudSaveResult> onComplete, CloudSaveResult resultado)
        {
            if (_logDetalhado)
            {
                Debug.Log(resultado.Success
                    ? $"[CassinaxSiteSaveApi] OK ({resultado.StatusCode})."
                    : $"[CassinaxSiteSaveApi] Falha ({resultado.StatusCode}).");
            }

            onComplete?.Invoke(resultado);
        }

        //------------------------------------------------------------- Cache da config remota

        private string ObterETagSalva(string gameKey)
        {
            return PlayerPrefs.GetString(PREF_ETAG + gameKey, string.Empty);
        }

        private string ObterConfigSalva(string gameKey)
        {
            if (_configGameKeyEmMemoria == gameKey && !string.IsNullOrEmpty(_configJsonEmMemoria))
                return _configJsonEmMemoria;

            return PlayerPrefs.GetString(PREF_CONFIG + gameKey, string.Empty);
        }

        private void SalvarConfig(string gameKey, string json, string etag)
        {
            _configGameKeyEmMemoria = gameKey;
            _configJsonEmMemoria = json;
            _configETagEmMemoria = etag;

            PlayerPrefs.SetString(PREF_CONFIG + gameKey, json ?? string.Empty);
            PlayerPrefs.SetString(PREF_ETAG + gameKey, etag ?? string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>ETag da ultima configuracao remota recebida nesta sessao.</summary>
        public string ConfigETag => _configETagEmMemoria;
    }
}
