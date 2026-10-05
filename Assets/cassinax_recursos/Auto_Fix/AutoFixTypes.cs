// Cassinax Auto Fix - v1.0.0
// Tipos do motor de manutencao de pacotes Cassinax.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace cassinax.autofix
{
    /// <summary>O que o motor pretende fazer com um achado.</summary>
    public enum AutoFixAction
    {
        /// <summary>Somente informar. O motor nunca mexe.</summary>
        Report,
        /// <summary>Remover arquivo ou pasta obsoleta do pacote.</summary>
        Delete
    }

    /// <summary>Quanto o achado atrapalha o projeto.</summary>
    public enum AutoFixSeverity
    {
        /// <summary>Observacao. Nada quebrado.</summary>
        Info,
        /// <summary>Convem resolver. Ex: sobra de versao antiga sem conflito de tipo.</summary>
        Warning,
        /// <summary>Quebra a compilacao ou o comportamento. Ex: classe duplicada.</summary>
        Blocking
    }

    /// <summary>
    /// Uma coisa encontrada por um modulo. Carrega o caminho exato e a justificativa,
    /// porque o usuario precisa poder julgar cada remocao antes de aprovar.
    /// </summary>
    public sealed class AutoFixFinding
    {
        public AutoFixFinding(string apiId, string assetPath, AutoFixAction action,
            AutoFixSeverity severity, string reason, string foundVersion = null, string keptVersion = null)
        {
            ApiId = apiId;
            AssetPath = assetPath;
            Action = action;
            Severity = severity;
            Reason = reason;
            FoundVersion = foundVersion;
            KeptVersion = keptVersion;
        }

        /// <summary>API dona do achado.</summary>
        public string ApiId { get; }

        /// <summary>Caminho relativo ao projeto, no formato do AssetDatabase.</summary>
        public string AssetPath { get; }

        public AutoFixAction Action { get; }
        public AutoFixSeverity Severity { get; }

        /// <summary>Explicacao em uma linha, mostrada ao usuario.</summary>
        public string Reason { get; }

        /// <summary>Versao encontrada neste caminho, quando foi possivel ler.</summary>
        public string FoundVersion { get; }

        /// <summary>Versao que sera preservada, quando o achado e uma duplicata.</summary>
        public string KeptVersion { get; }

        public override string ToString()
        {
            string versoes = string.IsNullOrEmpty(FoundVersion) ? "" :
                string.IsNullOrEmpty(KeptVersion) ? " (v" + FoundVersion + ")"
                    : " (v" + FoundVersion + " perde para v" + KeptVersion + ")";
            return Action + " " + AssetPath + versoes + " - " + Reason;
        }
    }

    /// <summary>
    /// Contrato de tratamento de uma API. Cada pacote Cassinax traz o seu, porque o layout,
    /// os caminhos legados e a forma de descobrir a versao instalada mudam de API para API.
    ///
    /// Um modulo apenas **relata**. Quem apaga e o motor, depois da aprovacao do usuario.
    /// </summary>
    public interface IAutoFixModule
    {
        /// <summary>Identificador estavel da API. Ex: <c>cassinax.unity-system-save</c>.</summary>
        string ApiId { get; }

        /// <summary>Nome legivel, usado na janela.</summary>
        string DisplayName { get; }

        /// <summary>Versao que acompanha o pacote recem-importado.</summary>
        string PackagedVersion { get; }

        /// <summary>Pasta canonica desta versao, relativa ao projeto.</summary>
        string CanonicalRoot { get; }

        /// <summary>
        /// Procura instalacoes antigas, duplicatas e restos. Nao altera nada.
        /// Lista vazia significa projeto em ordem para esta API.
        /// </summary>
        IEnumerable<AutoFixFinding> Scan();
    }
}
#endif
