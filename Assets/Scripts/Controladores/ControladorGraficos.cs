using System;
using UnityEngine;

// Filho "Graficos_Controller" do Objeto Mestre. Presets Baixo, Medio, Alto e Ultra = niveis de mesmo nome em
// Project Settings > Quality, cada um com seu asset do URP em Assets/Settings/Graficos.
// O save guarda o nome do nivel. Padrao: Medio.
[DisallowMultipleComponent]
public sealed class ControladorGraficos : MonoBehaviour
{
    public static readonly string[] Niveis = { "Baixo", "Medio", "Alto", "Ultra" };

    [Tooltip("Quadros por segundo alvo de cada nivel, na ordem Baixo, Medio, Alto, Ultra.")]
    [SerializeField] int[] fpsAlvo = { 30, 60, 60, 60 };

    public int NivelAtual { get; private set; } = 1;
    public bool AjusteAutomatico { get; private set; } = true;
    // nivel, automatico (true quando foi o monitor de desempenho que reduziu).
    public event Action<int, bool> NivelAlterado;
    public event Action<bool> AjusteAutomaticoAlterado;

    SaveAdapter save;

    public static int IndiceDoNome(string nome)
    {
        int indice = Array.IndexOf(Niveis, nome);
        return indice >= 0 ? indice : Array.IndexOf(Niveis, SaveAdapter.QUALIDADE_GRAFICA_PADRAO);
    }

    public void Configurar(SaveAdapter adapter)
    {
        save = adapter;
        string nome = save ? save.ObterTextoConfig(SaveAdapter.CHAVE_QUALIDADE_GRAFICA, SaveAdapter.QUALIDADE_GRAFICA_PADRAO)
            : SaveAdapter.QUALIDADE_GRAFICA_PADRAO;
        Aplicar(IndiceDoNome(nome), false, false);
        AjusteAutomatico = !save || save.ObterBoolConfig(SaveAdapter.CHAVE_AJUSTE_AUTOMATICO_GRAFICO, true);
        AjusteAutomaticoAlterado?.Invoke(AjusteAutomatico);
    }

    public void DefinirNivel(int nivel) => Aplicar(nivel, true, false);

    // Chamado pelo monitor de desempenho. Falso quando ja esta no nivel mais baixo.
    public bool ReduzirNivel()
    {
        if (NivelAtual <= 0) return false;
        Aplicar(NivelAtual - 1, true, true);
        return true;
    }

    public void DefinirAjusteAutomatico(bool ativo, bool salvar = true)
    {
        AjusteAutomatico = ativo;
        if (salvar && save) save.SalvarConfig(SaveAdapter.CHAVE_AJUSTE_AUTOMATICO_GRAFICO, ativo);
        AjusteAutomaticoAlterado?.Invoke(ativo);
    }

    void Aplicar(int nivel, bool salvar, bool automatico)
    {
        nivel = Mathf.Clamp(nivel, 0, Niveis.Length - 1);
        int qualidade = Array.IndexOf(QualitySettings.names, Niveis[nivel]);
        if (qualidade < 0)
            Debug.LogWarning($"[Graficos] O nivel '{Niveis[nivel]}' nao existe em Project Settings > Quality. " +
                "Confira os niveis Baixo, Medio, Alto e Ultra.", this);
        else if (qualidade != QualitySettings.GetQualityLevel())
            QualitySettings.SetQualityLevel(qualidade, true);
        // targetFrameRate so vale com o VSync desligado; no celular o padrao seria 30.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fpsAlvo != null && nivel < fpsAlvo.Length ? fpsAlvo[nivel] : 60;
        NivelAtual = nivel;
        if (salvar && save)
        {
            save.SalvarConfig(SaveAdapter.CHAVE_QUALIDADE_GRAFICA, Niveis[nivel]);
            save.SalvarAgora();
        }
        NivelAlterado?.Invoke(nivel, automatico);
    }
}
