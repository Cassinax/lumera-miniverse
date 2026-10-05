// Cassinax Unity System Save - v1.0.0
// Janela de acesso aos recursos do pacote. Window > Cassinax > Save System.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace cassinax.savesystem.editor
{
    /// <summary>
    /// Ponto de entrada do pacote no Editor: versao instalada, documentacao, exemplo e
    /// verificacoes. Segue o mesmo lugar das outras janelas Cassinax, em
    /// <c>Window &gt; Cassinax</c>.
    ///
    /// A janela nao cria nem altera save nenhum. Tudo que ela faz e abrir documentos,
    /// localizar arquivos do pacote e rodar a suite de aceitacao, que trabalha em pasta
    /// temporaria propria.
    /// </summary>
    public sealed class JanelaSaveSystem : EditorWindow
    {
        private const string Versao = "1.0.0";
        private const string Raiz = "Assets/cassinax_recursos/Unity_Save_System";
        private const string Assinatura = "Cassinax Unity System Save";

        private Vector2 _rolagem;
        private string _mensagem;
        private List<string> _adaptadores;

        [MenuItem("Window/Cassinax/Save System")]
        public static void Abrir()
        {
            var janela = GetWindow<JanelaSaveSystem>();
            janela.titleContent = new GUIContent("Save System");
            janela.minSize = new Vector2(420, 360);
            janela.Atualizar();
            janela.Show();
        }

        private void OnEnable() => Atualizar();

        private void OnGUI()
        {
            _rolagem = EditorGUILayout.BeginScrollView(_rolagem);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Cassinax Unity System Save", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Versao " + Versao + (Instalado ? "" : "  (pacote nao encontrado)"),
                EditorStyles.miniLabel);
            EditorGUILayout.Space(8);

            DesenharAdapter();
            EditorGUILayout.Space(8);
            DesenharDocumentacao();
            EditorGUILayout.Space(8);
            DesenharVerificacao();
            EditorGUILayout.Space(8);
            DesenharManutencao();

            if (!string.IsNullOrEmpty(_mensagem))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_mensagem, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private static bool Instalado => AssetDatabase.IsValidFolder(Raiz);

        //--------------------------------------------------------------------- Adapter

        private void DesenharAdapter()
        {
            EditorGUILayout.LabelField("Adapter do projeto", EditorStyles.miniBoldLabel);

            if (_adaptadores == null || _adaptadores.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Nenhum adapter proprio encontrado.\n\n" +
                    "O adapter e a ponte entre o jogo e a API, e e a unica peca que o projeto " +
                    "escreve. Leia o exemplo, crie o seu fora de cassinax_recursos (por exemplo " +
                    "em Assets/Scripts/Controladores) e depois remova o exemplo.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Encontrado(s):", EditorStyles.miniLabel);
                foreach (string caminho in _adaptadores)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("    " + caminho, EditorStyles.miniLabel);
                        if (GUILayout.Button("Mostrar", EditorStyles.miniButton, GUILayout.Width(64)))
                            Revelar(caminho);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Abrir o exemplo")) AbrirExemplo();
                if (GUILayout.Button("Criar exemplo na cena")) CriarExemploNaCena();
                if (GUILayout.Button("Procurar adapters")) Atualizar();
            }
        }

        /// <summary>
        /// Dispara o item de menu que o proprio exemplo registra. A janela nao referencia a
        /// assembly de Samples: assim o pacote continua compilando em projetos que ja
        /// removeram os exemplos, que e o estado esperado depois da adocao.
        /// </summary>
        private void CriarExemploNaCena()
        {
            _mensagem = EditorApplication.ExecuteMenuItem("Window/Cassinax/Exemplo de Save Local")
                ? null
                : "Os exemplos nao estao neste projeto. Isso e o esperado quando o adapter " +
                  "proprio ja existe e o exemplo foi removido.";
        }

        private void AbrirExemplo()
        {
            string caminho = Raiz + "/Samples/LocalOnly/SaveAdapterExemplo.cs";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(caminho);
            if (asset == null)
            {
                _mensagem = "Exemplo nao encontrado. Ele pode ja ter sido removido do projeto, " +
                            "o que e o esperado depois que o adapter proprio existe.";
                return;
            }
            AssetDatabase.OpenAsset(asset);
            _mensagem = null;
        }

        //--------------------------------------------------------------------- Documentacao

        private void DesenharDocumentacao()
        {
            EditorGUILayout.LabelField("Documentacao", EditorStyles.miniBoldLabel);
            Documento("README", "README.md");
            Documento("Referencia da API", "CassinaxUnitySystemSave-Referencia.md");
            Documento("Fluxo tecnico", "CassinaxUnitySystemSave-FluxoTecnico.md");
            Documento("Matriz de plataformas", "MatrizDePlataformas.md");
        }

        private void Documento(string rotulo, string arquivo)
        {
            string caminho = Raiz + "/Documentation/" + arquivo;
            using (new EditorGUI.DisabledScope(!File.Exists(Absoluto(caminho))))
            {
                if (GUILayout.Button(rotulo))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(caminho);
                    if (asset != null) AssetDatabase.OpenAsset(asset);
                }
            }
        }

        //--------------------------------------------------------------------- Verificacao

        private void DesenharVerificacao()
        {
            EditorGUILayout.LabelField("Verificacao", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "A suite roda em pasta temporaria e nao toca o save do projeto.",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("Rodar suite de aceitacao"))
                RodarSuite();

            EditorGUILayout.LabelField(
                "Relatorio de escopo: chame _saveSystem.BuildScopeReport().Resumo() no seu " +
                "adapter para ver o que nao viaja para a nuvem.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void RodarSuite()
        {
            try
            {
                int falhas = CassinaxSaveAcceptance.Run(Array.Empty<string>());
                _mensagem = falhas == 0
                    ? "Suite de aceitacao aprovada. Veja o detalhamento no Console."
                    : "A suite reportou falha. Veja o Console para o cenario exato.";
            }
            catch (Exception e)
            {
                _mensagem = "Nao foi possivel rodar a suite: " + e.Message;
            }
        }

        //--------------------------------------------------------------------- Manutencao

        private void DesenharManutencao()
        {
            EditorGUILayout.LabelField("Manutencao", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "Depois de atualizar o pacote, use o Auto Fix para remover sobras de " +
                "layouts anteriores. Duas copias da mesma classe impedem a compilacao.",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("Abrir Auto Fix"))
            {
                if (!EditorApplication.ExecuteMenuItem("Window/Cassinax/Auto Fix"))
                    _mensagem = "Auto Fix nao esta instalado neste projeto.";
            }
        }

        //--------------------------------------------------------------------- Apoio

        /// <summary>
        /// Procura adapters escritos pelo projeto: scripts que usam a API, fora de
        /// <c>cassinax_recursos</c> e sem a assinatura do pacote.
        /// </summary>
        private void Atualizar()
        {
            var encontrados = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("SaveAdapter t:MonoScript"))
            {
                string caminho = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(caminho)) continue;
                if (caminho.IndexOf("/cassinax_recursos/", StringComparison.Ordinal) >= 0) continue;
                if (TemAssinatura(caminho)) continue;
                encontrados.Add(caminho);
            }
            _adaptadores = encontrados.Distinct(StringComparer.Ordinal)
                                      .OrderBy(p => p, StringComparer.Ordinal).ToList();
            Repaint();
        }

        private static bool TemAssinatura(string caminho)
        {
            try
            {
                string absoluto = Absoluto(caminho);
                if (!File.Exists(absoluto)) return false;
                using (var leitor = new StreamReader(absoluto))
                {
                    char[] buffer = new char[1024];
                    int lidos = leitor.Read(buffer, 0, buffer.Length);
                    return lidos > 0 &&
                           new string(buffer, 0, lidos).IndexOf(Assinatura, StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception) { return false; }
        }

        private static void Revelar(string caminho)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(caminho);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        private static string Absoluto(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);
    }
}
#endif
