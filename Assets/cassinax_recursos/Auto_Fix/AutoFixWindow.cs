// Cassinax Auto Fix - v1.0.0
// Janela de manutencao: mostra os achados e aplica somente o que o usuario marcar.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace cassinax.autofix
{
    /// <summary>
    /// Janela do Auto Fix. Nada e removido sem marcacao e confirmacao: a janela existe
    /// para o usuario ver cada caminho e a justificativa antes de decidir.
    /// </summary>
    public sealed class AutoFixWindow : EditorWindow
    {
        private List<IAutoFixModule> _modulos;
        private List<AutoFixFinding> _achados;
        private readonly HashSet<AutoFixFinding> _marcados = new HashSet<AutoFixFinding>();
        private Vector2 _rolagem;
        private bool _varreu;

        [MenuItem("Window/Cassinax/Auto Fix")]
        public static void Abrir()
        {
            var janela = GetWindow<AutoFixWindow>();
            janela.titleContent = new GUIContent("Auto Fix");
            janela.minSize = new Vector2(560, 320);
            janela.Varrer();
            janela.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Manutencao dos pacotes Cassinax", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Mantem a versao superior de cada API e remove sobras de layouts anteriores.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Verificar agora", GUILayout.Height(24))) Varrer();
                using (new EditorGUI.DisabledScope(_marcados.Count == 0))
                {
                    if (GUILayout.Button("Aplicar marcados (" + _marcados.Count + ")", GUILayout.Height(24)))
                        Aplicar();
                }
            }

            EditorGUILayout.Space(6);

            if (_modulos != null && _modulos.Count > 0)
            {
                EditorGUILayout.LabelField("APIs com modulo instalado:", EditorStyles.miniBoldLabel);
                foreach (IAutoFixModule m in _modulos)
                    EditorGUILayout.LabelField("    " + m.DisplayName + "  v" + m.PackagedVersion, EditorStyles.miniLabel);
                EditorGUILayout.Space(6);
            }

            if (!_varreu)
            {
                EditorGUILayout.HelpBox("Clique em Verificar agora.", MessageType.Info);
                return;
            }

            if (_achados == null || _achados.Count == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma pendencia encontrada.", MessageType.Info);
                return;
            }

            int bloqueantes = _achados.Count(a => a.Severity == AutoFixSeverity.Blocking);
            if (bloqueantes > 0)
            {
                EditorGUILayout.HelpBox(
                    bloqueantes + " item(ns) impedem a compilacao, normalmente por classe duplicada.",
                    MessageType.Error);
            }

            _rolagem = EditorGUILayout.BeginScrollView(_rolagem);
            foreach (AutoFixFinding achado in _achados)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool removivel = achado.Action == AutoFixAction.Delete;
                        using (new EditorGUI.DisabledScope(!removivel))
                        {
                            bool marcado = _marcados.Contains(achado);
                            bool novo = EditorGUILayout.ToggleLeft(
                                Rotulo(achado), marcado && removivel, EstiloDe(achado.Severity));
                            if (removivel && novo != marcado)
                            {
                                if (novo) _marcados.Add(achado); else _marcados.Remove(achado);
                            }
                        }
                        if (GUILayout.Button("Mostrar", EditorStyles.miniButton, GUILayout.Width(64)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(achado.AssetPath);
                            if (asset != null) EditorGUIUtility.PingObject(asset);
                        }
                    }
                    EditorGUILayout.LabelField("    " + achado.Reason, EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static string Rotulo(AutoFixFinding achado)
        {
            string versao = achado.FoundVersion == null ? "" : "  [v" + achado.FoundVersion;
            if (versao.Length > 0)
                versao += achado.KeptVersion == null ? "]" : " -> mantem v" + achado.KeptVersion + "]";
            return (achado.Action == AutoFixAction.Report ? "(manual) " : "") + achado.AssetPath + versao;
        }

        private static GUIStyle EstiloDe(AutoFixSeverity severidade) =>
            severidade == AutoFixSeverity.Blocking ? EditorStyles.boldLabel : EditorStyles.label;

        private void Varrer()
        {
            _modulos = AutoFixEngine.DiscoverModules();
            _achados = AutoFixEngine.Scan(_modulos);
            _marcados.Clear();
            // Pre-marca o que impede a compilacao: e o caso em que nao ha escolha razoavel.
            foreach (AutoFixFinding a in _achados.Where(a => a.Action == AutoFixAction.Delete &&
                                                            a.Severity == AutoFixSeverity.Blocking))
                _marcados.Add(a);
            _varreu = true;
            Repaint();
        }

        private void Aplicar()
        {
            var aprovados = _marcados.ToList();
            string lista = string.Join("\n", aprovados.Take(12).Select(a => "  " + a.AssetPath));
            if (aprovados.Count > 12) lista += "\n  ... e mais " + (aprovados.Count - 12);

            if (!EditorUtility.DisplayDialog("Auto Fix",
                "Remover " + aprovados.Count + " caminho(s)?\n\n" + lista +
                "\n\nA remocao passa pelo AssetDatabase e pode ser desfeita pelo controle de versao.",
                "Remover", "Cancelar"))
            {
                return;
            }

            AutoFixEngine.Apply(aprovados);
            Varrer();
        }
    }
}
#endif
