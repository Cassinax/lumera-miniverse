using UnityEngine;
using UnityEngine.UI;

// Painel_Sair: confirmar fecha o app; negar fecha o painel. Abre pelo Voltar quando nada mais esta aberto no Menu.
[DisallowMultipleComponent]
public sealed class MenuSair : MonoBehaviour
{
    [SerializeField] Button confirmar;
    [SerializeField] Button negar;

    void Awake()
    {
        if (confirmar) confirmar.onClick.AddListener(Sair);
        if (negar) negar.onClick.AddListener(() => gameObject.SetActive(false));
    }

    public void Abrir() => gameObject.SetActive(true);

    void Sair()
    {
        if (ObjetoMestre.Instancia) ObjetoMestre.Instancia.FecharAplicacao();
        else Application.Quit();
    }
}
