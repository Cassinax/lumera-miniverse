using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiloCI.Bootstrap
{
    [DefaultExecutionOrder(-10000)]
    public class ObjetoMestreSceneGuard : MonoBehaviour
    {
        //-------------------------------------------------------------
        // Inicializacao
        //-------------------------------------------------------------

        // Cena onde o ObjetoMestre deve existir.
        [SerializeField] private string preMenuSceneName = "Stat Game";

        //-------------------------------------------------------------

        private void Awake()
        {
            // Se a cena abriu sem ObjetoMestre, precisa voltar para o pre-menu.
            if (FindAnyObjectByType<global::ObjetoMestre>() == null)
            {
                // Em qualquer cena que nao seja o pre-menu, carrega o pre-menu por nome.
                if (SceneManager.GetActiveScene().name != preMenuSceneName)
                {
                    SceneManager.LoadScene(preMenuSceneName);
                }
                else
                {
                    // Se o proprio pre-menu nao tem mestre, apenas alerta para correcao no editor.
                    Debug.LogWarning("ObjetoMestre nao foi encontrado nessa cena.");
                }
            }

            // A script e auto destrutiva: ela so precisa verificar uma vez.
            Destroy(this);
        }

        //-------------------------------------------------------------
    }
}
