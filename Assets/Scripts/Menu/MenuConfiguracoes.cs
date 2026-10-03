using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Painel_Configuracoes do Menu. Le e grava pelo Objeto Mestre; cada mudanca vale na hora e o save e
// gravado logo depois (e ao fechar o painel).
[DisallowMultipleComponent]
public sealed class MenuConfiguracoes : MonoBehaviour
{
    [Header("Volume")]
    [SerializeField] Slider volumeGeral;
    [SerializeField] TMP_Text porcentagemGeral;
    [SerializeField] Slider volumeEfeitos;
    [SerializeField] TMP_Text porcentagemEfeitos;

    [Header("Opcoes")]
    [SerializeField] Toggle vibracao;
    [SerializeField] TMP_Dropdown idioma;
    [Tooltip("Codigo de cada opcao do dropdown de idioma, na mesma ordem das opcoes.")]
    [SerializeField] string[] codigosIdioma = { "en", "pt", "es", "hi" };
    [Tooltip("Opcoes na ordem Baixo, Medio, Alto, Ultra.")]
    [SerializeField] TMP_Dropdown graficos;
    [SerializeField] Toggle ajusteAutomaticoGraficos;

    [Header("Links")]
    [SerializeField] Button botaoPoliticas;
    [Tooltip("Documento aberto pelo botao Politicas. Vazio desativa o botao.")]
    [SerializeField] string linkPoliticas = "";
    [SerializeField] Button botaoTermos;
    [Tooltip("Documento aberto pelo botao Termos. Vazio desativa o botao.")]
    [SerializeField] string linkTermos = "";

    // Apagar dados: o botao abre o Painel_ApagarDados (MenuApagarDados), configurado na cena.

    [Header("Navegacao")]
    [SerializeField] Button botaoVoltar;

    const float AtrasoGravacao = 0.35f;
    ObjetoMestre mestre;
    bool configurado, gravacaoPendente;
    float gravarEm;

    public void Abrir() => gameObject.SetActive(true);

    public void Fechar()
    {
        Gravar();
        gameObject.SetActive(false);
    }

    void Configurar()
    {
        if (configurado) return;
        configurado = true;
        if (volumeGeral) volumeGeral.onValueChanged.AddListener(_ => AoMudarVolumeGeral());
        if (volumeEfeitos) volumeEfeitos.onValueChanged.AddListener(_ => AoMudarVolumeEfeitos());
        if (vibracao) vibracao.onValueChanged.AddListener(AoMudarVibracao);
        if (idioma) idioma.onValueChanged.AddListener(AoMudarIdioma);
        if (graficos) graficos.onValueChanged.AddListener(AoMudarGraficos);
        if (ajusteAutomaticoGraficos) ajusteAutomaticoGraficos.onValueChanged.AddListener(AoMudarAjusteAutomatico);
        if (botaoPoliticas) botaoPoliticas.onClick.AddListener(() => AbrirLink(linkPoliticas));
        if (botaoTermos) botaoTermos.onClick.AddListener(() => AbrirLink(linkTermos));
        if (botaoVoltar) botaoVoltar.onClick.AddListener(Fechar);
    }

    void OnEnable()
    {
        Configurar();
        mestre = ObjetoMestre.Instancia;
        if (mestre && mestre.Graficos) mestre.Graficos.NivelAlterado += AoGraficosMudarem;
        Sincronizar();
    }

    void OnDisable()
    {
        if (mestre && mestre.Graficos) mestre.Graficos.NivelAlterado -= AoGraficosMudarem;
        Gravar();
    }

    void Update()
    {
        if (gravacaoPendente && Time.unscaledTime >= gravarEm) Gravar();
    }

    // Mostra os valores atuais sem disparar os eventos dos controles.
    void Sincronizar()
    {
        if (!mestre) return;
        if (mestre.Audio)
        {
            DefinirSlider(volumeGeral, porcentagemGeral, mestre.Audio.VolumeGeral);
            DefinirSlider(volumeEfeitos, porcentagemEfeitos, mestre.Audio.VolumeEfeitos);
        }
        if (vibracao && mestre.Vibracao) vibracao.SetIsOnWithoutNotify(mestre.Vibracao.Ativa);
        if (idioma)
        {
            int indice = Array.IndexOf(codigosIdioma, LocalizacaoLumera.Codigo(mestre.Idioma));
            if (indice >= 0) idioma.SetValueWithoutNotify(indice);
        }
        if (mestre.Graficos)
        {
            if (graficos) graficos.SetValueWithoutNotify(mestre.Graficos.NivelAtual);
            if (ajusteAutomaticoGraficos) ajusteAutomaticoGraficos.SetIsOnWithoutNotify(mestre.Graficos.AjusteAutomatico);
        }
        if (botaoPoliticas) botaoPoliticas.interactable = !string.IsNullOrWhiteSpace(linkPoliticas);
        if (botaoTermos) botaoTermos.interactable = !string.IsNullOrWhiteSpace(linkTermos);
    }

    //---------- Controles

    void AoMudarVolumeGeral()
    {
        float volume = LerSlider(volumeGeral);
        AtualizarPorcentagem(porcentagemGeral, volume);
        if (mestre && mestre.Audio) mestre.Audio.DefinirVolumeGeral(volume);
        AgendarGravacao();
    }

    void AoMudarVolumeEfeitos()
    {
        float volume = LerSlider(volumeEfeitos);
        AtualizarPorcentagem(porcentagemEfeitos, volume);
        if (mestre && mestre.Audio) mestre.Audio.DefinirVolumeEfeitos(volume);
        AgendarGravacao();
    }

    void AoMudarVibracao(bool ativa)
    {
        if (!mestre || !mestre.Vibracao) return;
        mestre.Vibracao.DefinirAtiva(ativa);
        if (ativa) mestre.Vibracao.Vibrar(); // Amostra do que foi ligado.
        AgendarGravacao();
    }

    void AoMudarIdioma(int indice)
    {
        if (!mestre || indice < 0 || indice >= codigosIdioma.Length) return;
        if (LocalizacaoLumera.TentarConverter(codigosIdioma[indice], out var novo)) mestre.DefinirIdioma(novo);
        AgendarGravacao();
    }

    void AoMudarGraficos(int nivel)
    {
        if (mestre && mestre.Graficos) mestre.Graficos.DefinirNivel(nivel);
    }

    void AoMudarAjusteAutomatico(bool ativo)
    {
        if (mestre && mestre.Graficos) mestre.Graficos.DefinirAjusteAutomatico(ativo);
        AgendarGravacao();
    }

    // O monitor de desempenho pode descer o nivel com o painel aberto.
    void AoGraficosMudarem(int nivel, bool automatico)
    {
        if (graficos) graficos.SetValueWithoutNotify(nivel);
    }

    //---------- Links

    void AbrirLink(string link)
    {
        if (!string.IsNullOrWhiteSpace(link)) Application.OpenURL(link.Trim());
    }

    //---------- Gravacao

    void AgendarGravacao()
    {
        gravacaoPendente = true;
        gravarEm = Time.unscaledTime + AtrasoGravacao;
    }

    void Gravar()
    {
        if (!gravacaoPendente) return;
        gravacaoPendente = false;
        if (mestre && mestre.Save) mestre.Save.SalvarAgora();
    }

    //---------- Utilitarios

    // Funciona com sliders de 0 a 1 ou de 0 a 100.
    static float LerSlider(Slider slider) => slider ? Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value) : 1;

    static void DefinirSlider(Slider slider, TMP_Text porcentagem, float volume)
    {
        if (slider) slider.SetValueWithoutNotify(Mathf.Lerp(slider.minValue, slider.maxValue, volume));
        AtualizarPorcentagem(porcentagem, volume);
    }

    static void AtualizarPorcentagem(TMP_Text texto, float volume)
    {
        if (texto) texto.text = Mathf.RoundToInt(volume * 100) + "%";
    }
}
