using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lumera Miniverse: nasce na cena do Menu e vive ate o app fechar (ordem sempre Menu > Jogo).
// Cada controlador e um filho deste objeto: Save_Adapter, Audio_Controller, Vibracao_Controller,
// Graficos_Controller, Monitor_Desempenho... Cenas acessam tudo por ObjetoMestre.Instancia, nunca por
// referencia serializada: ao voltar ao Menu, a copia da cena e descartada e a original continua.
[DefaultExecutionOrder(-20000), DisallowMultipleComponent]
public sealed class ObjetoMestre : MonoBehaviour
{
    public const string CenaMenu = "Menu";

    [Tooltip("Painel_Carregando, filho do Canvas deste objeto.")]
    [SerializeField] GameObject painelCarregando;
    [SerializeField] TMP_Text textoCarregando;

    static readonly TextoLocalizado TextoCarregando = new("Carregando...", "Loading...", "Cargando...");

    static ObjetoMestre instancia;
    public static ObjetoMestre Instancia => instancia;

    public SaveAdapter Save { get; private set; }
    public ControladorAudio Audio { get; private set; }
    public ControladorVibracao Vibracao { get; private set; }
    public ControladorGraficos Graficos { get; private set; }
    public IdiomaLumera Idioma { get; private set; } = IdiomaLumera.Portugues;
    public bool CarregandoCena { get; private set; }

    public event Action<IdiomaLumera> IdiomaAlterado;
    public event Action CenaDisponivel;

    void Awake()
    {
        if (instancia && instancia != this)
        {
            // Copia trazida pela cena do Menu ao voltar: desativa antes que os filhos acordem
            // (um segundo SaveAdapter abriria o mesmo arquivo de save).
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        instancia = this;
        DontDestroyOnLoad(gameObject);
        Save = GetComponentInChildren<SaveAdapter>(true);
        Audio = GetComponentInChildren<ControladorAudio>(true);
        Vibracao = GetComponentInChildren<ControladorVibracao>(true);
        Graficos = GetComponentInChildren<ControladorGraficos>(true);
        if (!Save) Debug.LogError("[Objeto Mestre] Falta o SaveAdapter no filho Save_Adapter.", this);
        if (painelCarregando && !textoCarregando) textoCarregando = painelCarregando.GetComponentInChildren<TMP_Text>(true);
    }

    void OnDestroy()
    {
        if (instancia == this) instancia = null;
    }

    // Start: todos os filhos ja acordaram (o SaveAdapter inicializa no proprio Awake).
    void Start()
    {
        AplicarConfiguracoesSalvas();
        MostrarCarregando(false);
    }

    void AplicarConfiguracoesSalvas()
    {
        if (Audio) Audio.Configurar(Save);
        if (Vibracao) Vibracao.Configurar(Save);
        if (Graficos) Graficos.Configurar(Save);
        var monitor = GetComponentInChildren<MonitorDesempenho>(true);
        if (monitor) monitor.Configurar(this, Graficos);
        PrepararIdioma();
    }

    //---------- Idioma

    public void DefinirIdioma(IdiomaLumera idioma)
    {
        AplicarIdioma(idioma);
        if (Save) Save.SalvarConfig(SaveAdapter.CHAVE_IDIOMA, LocalizacaoLumera.Codigo(idioma));
    }

    void PrepararIdioma()
    {
        string codigo = Save ? Save.ObterTextoConfig(SaveAdapter.CHAVE_IDIOMA, "") : "";
        AplicarIdioma(LocalizacaoLumera.TentarConverter(codigo, out var salvo) ? salvo : LocalizacaoLumera.DoSistema());
    }

    void AplicarIdioma(IdiomaLumera idioma)
    {
        Idioma = idioma;
        if (textoCarregando) textoCarregando.text = TextoCarregando.Obter(idioma);
        IdiomaAlterado?.Invoke(idioma);
    }

    //---------- Cenas

    public void CarregarCena(string nome)
    {
        if (CarregandoCena || string.IsNullOrEmpty(nome)) return;
        StartCoroutine(ExecutarCarregamento(nome));
    }

    public void VoltarAoMenu() => CarregarCena(CenaMenu);

    IEnumerator ExecutarCarregamento(string nome)
    {
        CarregandoCena = true;
        if (Save) Save.SalvarAgora();
        MostrarCarregando(true);
        yield return null; // Deixa o painel aparecer antes do trabalho pesado.
        AsyncOperation carregamento = SceneManager.LoadSceneAsync(nome);
        if (carregamento == null)
        {
            Debug.LogError($"[Objeto Mestre] Nao foi possivel carregar a cena '{nome}'. Ela esta no Build Profile?", this);
            MostrarCarregando(false);
            CarregandoCena = false;
            yield break;
        }
        while (!carregamento.isDone) yield return null;
        yield return null;
        MostrarCarregando(false);
        CarregandoCena = false;
        CenaDisponivel?.Invoke();
    }

    void MostrarCarregando(bool visivel)
    {
        if (!painelCarregando) return;
        if (visivel && textoCarregando) textoCarregando.text = TextoCarregando.Obter(Idioma);
        painelCarregando.SetActive(visivel);
    }

    //---------- Dados

    // Apaga o save, volta todas as configuracoes ao padrao e reabre o Menu limpo.
    public bool ApagarDados()
    {
        if (CarregandoCena || !Save || !Save.RestaurarPadroesDeFabrica()) return false;
        AplicarConfiguracoesSalvas();
        CarregarCena(CenaMenu);
        return true;
    }
}
