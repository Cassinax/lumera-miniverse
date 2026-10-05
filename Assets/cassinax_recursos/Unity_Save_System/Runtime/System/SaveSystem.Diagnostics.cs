// Cassinax Unity System Save - v1.0.0
// Relatorio de escopo: o que viaja para a nuvem e o que fica no aparelho.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace cassinax.savesystem
{
    /// <summary>
    /// Fotografia de como as chaves confirmadas foram classificadas por escopo.
    ///
    /// Existe por causa de um risco concreto: chave sem regra explicita cai em
    /// <see cref="SaveScope.LocalPreference"/> pela politica de chave desconhecida. Isso e
    /// proposital para idioma e volumes, mas se o jogo esquecer de declarar um prefixo de
    /// progresso, esse progresso deixa de viajar para a nuvem **sem nenhum erro**. Nada
    /// quebra, nada avisa, e a perda so aparece quando o jogador troca de aparelho.
    ///
    /// Este relatorio nao adivinha intencao: ele mostra o que ficou local para o
    /// desenvolvedor conferir contra a propria tabela de "o que viaja".
    /// </summary>
    public sealed class SaveScopeReport
    {
        internal SaveScopeReport(Dictionary<SaveScope, List<string>> porEscopo)
        {
            PorEscopo = porEscopo;
        }

        /// <summary>Chaves agrupadas por escopo resultante.</summary>
        public Dictionary<SaveScope, List<string>> PorEscopo { get; }

        /// <summary>Chaves que nao viajam para a nuvem.</summary>
        public List<string> SomenteLocais =>
            PorEscopo.TryGetValue(SaveScope.LocalPreference, out var l) ? l : new List<string>();

        /// <summary>Chaves que viajam como progresso.</summary>
        public List<string> Progresso =>
            PorEscopo.TryGetValue(SaveScope.Progress, out var l) ? l : new List<string>();

        /// <summary>Total de chaves confirmadas analisadas.</summary>
        public int Total => PorEscopo.Values.Sum(l => l.Count);

        /// <summary>Comandos distintos cujas chaves ficaram todas em escopo local.</summary>
        public List<string> ComandosSomenteLocais
        {
            get
            {
                var locais = new HashSet<string>(SomenteLocais.Select(ComandoDe), StringComparer.Ordinal);
                foreach (var par in PorEscopo)
                {
                    if (par.Key == SaveScope.LocalPreference) continue;
                    foreach (string chave in par.Value) locais.Remove(ComandoDe(chave));
                }
                locais.Remove(string.Empty);
                return locais.OrderBy(c => c, StringComparer.Ordinal).ToList();
            }
        }

        private static string ComandoDe(string chave)
        {
            if (string.IsNullOrEmpty(chave) || chave[0] != '[') return string.Empty;
            int fim = chave.IndexOf("],", StringComparison.Ordinal);
            return fim > 1 ? chave.Substring(1, fim - 1) : string.Empty;
        }

        /// <summary>
        /// Resumo legivel para log ou painel de debug. Mostra contagens e os comandos que
        /// ficaram inteiramente locais, sem despejar valores do save.
        /// </summary>
        public string Resumo()
        {
            var sb = new StringBuilder();
            sb.Append("Escopo de ").Append(Total).AppendLine(" chaves confirmadas:");
            foreach (SaveScope escopo in new[] { SaveScope.Progress, SaveScope.LocalPreference,
                SaveScope.Identity, SaveScope.Metadata, SaveScope.Audit })
            {
                int n = PorEscopo.TryGetValue(escopo, out var l) ? l.Count : 0;
                sb.Append("  ").Append(escopo).Append(": ").Append(n).AppendLine();
            }
            var comandos = ComandosSomenteLocais;
            if (comandos.Count > 0)
            {
                sb.AppendLine("Comandos que NAO viajam para a nuvem (confira se algum deveria):");
                foreach (string c in comandos) sb.Append("  [").Append(c).AppendLine("]");
            }
            return sb.ToString();
        }
    }

    public partial class SaveSystem
    {
        /// <summary>
        /// Classifica as chaves confirmadas pelo escopo em vigor e devolve o relatorio.
        /// Nao altera o save, nao grava e nao muda a revisao.
        /// </summary>
        public SaveScopeReport BuildScopeReport()
        {
            var porEscopo = new Dictionary<SaveScope, List<string>>();
            foreach (string chave in _core.GetAllKeys().OrderBy(k => k, StringComparer.Ordinal))
            {
                SaveScope escopo = ScopeOf(chave);
                if (!porEscopo.TryGetValue(escopo, out var lista))
                {
                    lista = new List<string>();
                    porEscopo[escopo] = lista;
                }
                lista.Add(chave);
            }
            return new SaveScopeReport(porEscopo);
        }
    }
}
