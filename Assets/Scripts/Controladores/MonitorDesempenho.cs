using UnityEngine;

// Filho "Monitor_Desempenho" do Objeto Mestre. Mede o FPS o tempo todo; se ficar abaixo do minimo por um
// periodo constante, desce um nivel grafico. So age com o ajuste automatico ligado (padrao).
[DisallowMultipleComponent]
public sealed class MonitorDesempenho : MonoBehaviour
{
    [Tooltip("Abaixo deste FPS o tempo de desempenho ruim comeca a contar.")]
    [SerializeField, Min(1)] float fpsMinimo = 30;
    [Tooltip("Segundos seguidos abaixo do minimo para descer um nivel. Uma amostra boa zera a contagem.")]
    [SerializeField, Min(1)] float segundosAbaixo = 30;
    [Tooltip("Duracao de cada amostra de FPS, em segundos.")]
    [SerializeField, Min(0.1f)] float duracaoAmostra = 1;
    [Tooltip("Segundos ignorados depois de carregar cena, voltar ao app ou trocar de nivel (picos esperados).")]
    [SerializeField, Min(0)] float carencia = 5;

    [Header("Estado (leitura)")]
    [SerializeField] float fpsMedido;
    [SerializeField] float tempoAbaixo;

    ControladorGraficos graficos;
    ObjetoMestre mestre;
    float tempoAmostra, carenciaRestante;
    int quadros;

    public void Configurar(ObjetoMestre dono, ControladorGraficos controlador)
    {
        mestre = dono;
        if (graficos) graficos.NivelAlterado -= AoMudarNivel;
        graficos = controlador;
        if (graficos) graficos.NivelAlterado += AoMudarNivel;
        Reiniciar();
    }

    void OnDestroy()
    {
        if (graficos) graficos.NivelAlterado -= AoMudarNivel;
    }

    void Update()
    {
        // Nada a reduzir, ajuste desligado ou cena carregando: nao conta.
        if (!graficos || !graficos.AjusteAutomatico || graficos.NivelAtual <= 0 || (mestre && mestre.CarregandoCena))
        {
            Reiniciar();
            return;
        }
        float dt = Time.unscaledDeltaTime;
        if (carenciaRestante > 0)
        {
            carenciaRestante -= dt;
            return;
        }
        quadros++;
        tempoAmostra += dt;
        if (tempoAmostra < duracaoAmostra) return;
        fpsMedido = quadros / tempoAmostra;
        // Periodo constante: uma amostra no minimo ou acima zera a contagem.
        tempoAbaixo = fpsMedido < fpsMinimo ? tempoAbaixo + tempoAmostra : 0;
        quadros = 0;
        tempoAmostra = 0;
        if (tempoAbaixo >= segundosAbaixo)
        {
            Debug.Log($"[Graficos] {fpsMedido:0.0} FPS por {tempoAbaixo:0} s: descendo um nivel.", this);
            graficos.ReduzirNivel();
        }
    }

    void AoMudarNivel(int nivel, bool automatico) => Reiniciar();

    void OnApplicationPause(bool pausado) => Reiniciar();
    void OnApplicationFocus(bool foco) => Reiniciar();

    void Reiniciar()
    {
        tempoAbaixo = tempoAmostra = 0;
        quadros = 0;
        carenciaRestante = carencia;
    }
}
