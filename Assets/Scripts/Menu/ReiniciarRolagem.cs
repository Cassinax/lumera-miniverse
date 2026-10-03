using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Volta a rolagem ao inicio sempre que o Scroll View aparece. O OnEnable tambem roda quando quem e ativado
// e o painel pai (o Scroll View em si fica sempre ativo na cena).
[DisallowMultipleComponent, RequireComponent(typeof(ScrollRect))]
public sealed class ReiniciarRolagem : MonoBehaviour
{
    [Tooltip("Posicao ao aparecer: X 0 = esquerda, Y 1 = topo.")]
    [SerializeField] Vector2 posicaoInicial = new Vector2(0, 1);

    ScrollRect rolagem;

    void Awake() => rolagem = GetComponent<ScrollRect>();

    void OnEnable()
    {
        Reiniciar();
        // Listas montadas na mesma ativacao (cards, saves) mudam o tamanho do Content no layout seguinte.
        StartCoroutine(ReiniciarAposLayout());
    }

    IEnumerator ReiniciarAposLayout()
    {
        yield return null;
        Reiniciar();
    }

    public void Reiniciar()
    {
        if (!rolagem) return;
        rolagem.StopMovement();
        if (rolagem.content) LayoutRebuilder.ForceRebuildLayoutImmediate(rolagem.content);
        rolagem.normalizedPosition = posicaoInicial;
    }
}
