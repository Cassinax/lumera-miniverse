// Cassinax Unity System Save - v1.0.0
// Cliente das APIs de jogo do portal Cassinax. Componente de cena.
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
    public partial class CassinaxSiteSaveApi : MonoBehaviour, IAsyncCloudSaveAdapter
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

    }
}
