// Cassinax Auto Fix - v1.0.0
// Modulo de tratamento do Cassinax Unity System Save.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;

namespace cassinax.autofix
{
    /// <summary>
    /// Manutencao do pacote de save.
    ///
    /// O layout mudou na v1.0.0: os scripts sairam de <c>Scripts/</c> para
    /// <c>Runtime/</c>, com subpastas por assunto, e o template passou a se chamar
    /// <c>SaveAdapterExemplo</c>. Um projeto que vinha da v0.x e reimporta fica com as duas
    /// copias e nao compila, porque as classes aparecem duas vezes.
    ///
    /// Este modulo encontra as copias antigas e propoe a remocao, sempre preservando a
    /// versao superior. O adapter escrito pelo projeto nunca entra na lista: ele vive fora
    /// de <c>cassinax_recursos</c>, nao tem a assinatura do pacote, e e justamente a peca
    /// que precisa sobreviver a qualquer atualizacao.
    /// </summary>
    public sealed class SaveSystemAutoFixModule : IAutoFixModule
    {
        /// <summary>Cabecalho presente em todo arquivo distribuido pelo pacote.</summary>
        private const string Assinatura = "Cassinax Unity System Save";

        private const string Raiz = "Assets/cassinax_recursos/Unity_Save_System";

        /// <summary>
        /// Arquivos que identificam uma instalacao do pacote. Comparar a versao de cada um
        /// deles resolve o caso de uma instalacao parcial, em que so parte dos arquivos foi
        /// substituida na importacao.
        /// </summary>
        private static readonly string[] Ancoras =
        {
            "SaveCore.cs", "SaveSystem.cs", "SavePayloadChunks.cs",
            "SaveJsonMinimo.cs", "FileSaveStorage.cs", "PlayerPrefsSaveStorage.cs"
        };

        /// <summary>
        /// Pastas e arquivos que o pacote ocupou em versoes anteriores, dentro da fronteira
        /// de atuacao. Hoje nenhum deles faz parte da distribuicao, entao o que sobrar ali
        /// e resto de uma versao antiga.
        /// </summary>
        private static readonly string[] CaminhosLegados =
        {
            "Assets/cassinax_recursos/Scripts/Save System",
            "Assets/cassinax_recursos/Docs/Cassinax Unity System Save",
            Raiz + "/Scripts",
            Raiz + "/Exemplos",
            Raiz + "/Docs",
            Raiz + "/Plugins"
        };

        /// <summary>
        /// Caminhos que versoes anteriores ocupavam FORA de <c>cassinax_recursos</c>.
        /// O motor nao remove fora da fronteira, entao estes sao apenas relatados para
        /// remocao manual. O template antigo morava junto dos scripts do projeto, e ali
        /// pode haver codigo autoral no meio.
        /// </summary>
        private static readonly string[] LegadosForaDaFronteira =
        {
            "Assets/Scripts/Exemplos/Controladores",
            "Assets/Plugins/WebGL/CassinaxContexto.jslib"
        };

        public string ApiId => "cassinax.unity-system-save";
        public string DisplayName => "Cassinax Unity System Save";
        public string PackagedVersion => "1.0.0";
        public string CanonicalRoot => Raiz;

        public IEnumerable<AutoFixFinding> Scan()
        {
            var achados = new List<AutoFixFinding>();
            achados.AddRange(VarrerDuplicatas());
            achados.AddRange(VarrerLegados());
            achados.AddRange(VarrerForaDaFronteira());
            achados.AddRange(VarrerExemplo());
            return achados;
        }

        /// <summary>
        /// Relata restos de versoes antigas que ficaram fora de <c>cassinax_recursos</c>.
        /// Nao propoe remocao: ali o motor nao mexe, e a pasta pode conter codigo do projeto.
        /// </summary>
        private IEnumerable<AutoFixFinding> VarrerForaDaFronteira()
        {
            foreach (string legado in LegadosForaDaFronteira)
            {
                if (!AutoFixEngine.Exists(legado)) continue;
                yield return new AutoFixFinding(ApiId, legado, AutoFixAction.Report, AutoFixSeverity.Warning,
                    "Resto de uma versao anterior fora de " + AutoFixEngine.FronteiraDeAtuacao +
                    ". O Auto Fix nao remove fora dessa pasta: confira o conteudo e apague a mao.");
            }
        }

        /// <summary>
        /// Para cada arquivo-ancora, encontra todas as copias assinadas no projeto e propoe
        /// remover as de versao inferior.
        /// </summary>
        private IEnumerable<AutoFixFinding> VarrerDuplicatas()
        {
            foreach (string ancora in Ancoras)
            {
                var copias = AutoFixEngine.FindSignedAssets(ancora, Assinatura);
                if (copias.Count < 2) continue;

                var versoes = copias.ToDictionary(c => c, AutoFixEngine.ReadVersion, StringComparer.Ordinal);
                string vencedor = Escolher(versoes);
                AutoFixVersion versaoVencedora = versoes[vencedor];

                foreach (string copia in copias)
                {
                    if (copia == vencedor) continue;
                    bool removivel = AutoFixEngine.DentroDaFronteira(copia);
                    yield return new AutoFixFinding(ApiId, copia,
                        removivel ? AutoFixAction.Delete : AutoFixAction.Report,
                        AutoFixSeverity.Blocking,
                        removivel
                            ? "Copia antiga do pacote. Duas copias da mesma classe impedem a compilacao."
                            : "Copia antiga do pacote fora de " + AutoFixEngine.FronteiraDeAtuacao +
                              ". Impede a compilacao, mas precisa ser removida a mao.",
                        versoes[copia].ToString(), versaoVencedora.ToString());
                }
            }
        }

        /// <summary>
        /// Escolhe qual copia fica: a de maior versao. Empatando, fica a que esta na pasta
        /// canonica desta versao, porque e onde a importacao atual gravou.
        /// </summary>
        private string Escolher(Dictionary<string, AutoFixVersion> versoes)
        {
            string melhor = null;
            foreach (var par in versoes)
            {
                if (melhor == null) { melhor = par.Key; continue; }
                int comparacao = par.Value.CompareTo(versoes[melhor]);
                if (comparacao > 0) { melhor = par.Key; continue; }
                if (comparacao == 0 &&
                    par.Key.StartsWith(Raiz, StringComparison.Ordinal) &&
                    !melhor.StartsWith(Raiz, StringComparison.Ordinal))
                {
                    melhor = par.Key;
                }
            }
            return melhor;
        }

        /// <summary>
        /// Caminhos de versoes anteriores. Uma pasta so e proposta quando tudo que sobrou
        /// dentro dela pertence ao pacote; havendo arquivo de terceiro, o relato pede
        /// decisao manual em vez de remover.
        /// </summary>
        private IEnumerable<AutoFixFinding> VarrerLegados()
        {
            foreach (string legado in CaminhosLegados)
            {
                if (!AutoFixEngine.Exists(legado)) continue;

                var arquivos = UnityEditor.AssetDatabase.FindAssets("", new[] { legado })
                    .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                    .Where(p => !string.IsNullOrEmpty(p) && !UnityEditor.AssetDatabase.IsValidFolder(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (arquivos.Count == 0)
                {
                    yield return new AutoFixFinding(ApiId, legado, AutoFixAction.Delete, AutoFixSeverity.Info,
                        "Pasta vazia de um layout anterior do pacote.");
                    continue;
                }

                var deTerceiros = arquivos.Where(a => !AutoFixEngine.ContainsSignature(a, Assinatura)).ToList();
                if (deTerceiros.Count > 0)
                {
                    yield return new AutoFixFinding(ApiId, legado, AutoFixAction.Report, AutoFixSeverity.Warning,
                        "Caminho de versao antiga com " + deTerceiros.Count +
                        " arquivo(s) que nao sao do pacote. Mova o que for seu e remova o resto a mao.");
                    continue;
                }

                foreach (string arquivo in arquivos)
                {
                    yield return new AutoFixFinding(ApiId, arquivo, AutoFixAction.Delete, AutoFixSeverity.Blocking,
                        "Arquivo do pacote em caminho de versao anterior.",
                        AutoFixEngine.ReadVersion(arquivo).ToString(), PackagedVersion);
                }
            }
        }

        /// <summary>
        /// O exemplo de adapter serve para ser lido e copiado, nao para rodar. Quando o
        /// projeto ja tem o proprio adapter, o exemplo virou peso morto e pode sair.
        /// </summary>
        private IEnumerable<AutoFixFinding> VarrerExemplo()
        {
            var exemplos = AutoFixEngine.FindSignedAssets("SaveAdapterExemplo.cs", Assinatura);
            if (exemplos.Count == 0) yield break;

            bool projetoTemAdapter = AdapterDoProjetoExiste();
            foreach (string exemplo in exemplos)
            {
                yield return projetoTemAdapter && AutoFixEngine.DentroDaFronteira(exemplo)
                    ? new AutoFixFinding(ApiId, exemplo, AutoFixAction.Delete, AutoFixSeverity.Info,
                        "O projeto ja tem o proprio adapter. O exemplo pode sair.")
                    : new AutoFixFinding(ApiId, exemplo, AutoFixAction.Report, AutoFixSeverity.Info,
                        "Exemplo de adapter: serve para ser lido e copiado, nao para rodar. Crie o adapter " +
                        "do projeto fora de " + AutoFixEngine.FronteiraDeAtuacao + " e remova o exemplo.");
            }
        }

        /// <summary>
        /// Procura um adapter escrito pelo projeto: um MonoBehaviour que usa a API, fora de
        /// <c>cassinax_recursos</c> e sem a assinatura do pacote.
        /// </summary>
        private static bool AdapterDoProjetoExiste()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("SaveAdapter t:MonoScript"))
            {
                string caminho = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(caminho)) continue;
                if (caminho.IndexOf("/cassinax_recursos/", StringComparison.Ordinal) >= 0) continue;
                if (AutoFixEngine.ContainsSignature(caminho, Assinatura)) continue;
                return true;
            }
            return false;
        }
    }
}
#endif
