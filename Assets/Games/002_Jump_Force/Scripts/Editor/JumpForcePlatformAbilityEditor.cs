using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Lumera.JumpForce.Editor
{
    [CustomEditor(typeof(JumpForcePlatformAbility)), CanEditMultipleObjects]
    public sealed class JumpForcePlatformAbilityEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var script = new PropertyField(serializedObject.FindProperty("m_Script"));
            script.SetEnabled(false);
            root.Add(script);

            var tipo = serializedObject.FindProperty("tipo");
            root.Add(new PropertyField(tipo));
            var invisivel = Campos("intervaloVisibilidade", "avisoPiscando", "intervaloPiscada");
            var nuvem = Campos("esperaNuvem", "velocidadeDescida", "velocidadeRetorno",
                "descidaMaxima", "forcaVertical", "escalaMinimaNuvem", "visualNuvem");
            var instavel = Campos("intervaloQueda", "tempoReconstrucao", "partes", "margemDesativacao");
            root.Add(invisivel);
            root.Add(nuvem);
            root.Add(instavel);
            var tiposDiferentes = new HelpBox("Selecione um mesmo tipo para editar os parametros da habilidade.",
                HelpBoxMessageType.Info);
            root.Add(tiposDiferentes);
            root.Add(Campos("jogador", "cameraJogo"));

            void AtualizarVisibilidade()
            {
                bool misturado = tipo.hasMultipleDifferentValues;
                var selecionado = (JumpForcePlatformType)tipo.enumValueIndex;
                invisivel.style.display = !misturado && selecionado == JumpForcePlatformType.Invisivel
                    ? DisplayStyle.Flex : DisplayStyle.None;
                nuvem.style.display = !misturado && selecionado == JumpForcePlatformType.Nuvem
                    ? DisplayStyle.Flex : DisplayStyle.None;
                instavel.style.display = !misturado && selecionado == JumpForcePlatformType.Instavel
                    ? DisplayStyle.Flex : DisplayStyle.None;
                tiposDiferentes.style.display = misturado ? DisplayStyle.Flex : DisplayStyle.None;
            }

            AtualizarVisibilidade();
            root.TrackPropertyValue(tipo, _ => AtualizarVisibilidade());
            return root;
        }

        VisualElement Campos(params string[] nomes)
        {
            var grupo = new VisualElement();
            foreach (var nome in nomes)
                grupo.Add(new PropertyField(serializedObject.FindProperty(nome)));
            return grupo;
        }
    }
}
