using UnityEngine;

// Filho "Vibracao_Controller" do Objeto Mestre. Os jogos chamam Vibrar(); a opcao das configuracoes decide.
[DisallowMultipleComponent]
public sealed class ControladorVibracao : MonoBehaviour
{
    public bool Ativa { get; private set; } = true;
    SaveAdapter save;

    public void Configurar(SaveAdapter adapter)
    {
        save = adapter;
        Ativa = !save || save.ObterBoolConfig(SaveAdapter.CHAVE_VIBRACAO, true);
    }

    public void DefinirAtiva(bool ativa, bool salvar = true)
    {
        Ativa = ativa;
        if (salvar && save) save.SalvarConfig(SaveAdapter.CHAVE_VIBRACAO, ativa);
    }

    public void Vibrar()
    {
        if (!Ativa) return;
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }
}
