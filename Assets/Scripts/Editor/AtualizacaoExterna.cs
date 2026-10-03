using System.IO;
using UnityEditor;

// Atualiza os assets quando um processo externo (edicao direta de arquivos) pede, sem precisar dar foco no Unity.
// Pedido: criar o arquivo Temp/atualizar_unity.pedido na raiz do projeto. O Editor verifica a cada segundo,
// inclusive em segundo plano, apaga o pedido e roda AssetDatabase.Refresh.
[InitializeOnLoad]
static class AtualizacaoExterna
{
    const string Pedido = "Temp/atualizar_unity.pedido";
    static double proximaVerificacao;

    static AtualizacaoExterna() => EditorApplication.update += Verificar;

    static void Verificar()
    {
        if (EditorApplication.timeSinceStartup < proximaVerificacao) return;
        proximaVerificacao = EditorApplication.timeSinceStartup + 1;
        // Compilando, importando ou em Play: espera o proximo ciclo.
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(Pedido)) return;
        File.Delete(Pedido);
        AssetDatabase.Refresh();
    }
}
