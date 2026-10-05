// Cassinax Auto Fix - v1.0.0
// Leitura e comparacao das versoes declaradas nos arquivos dos pacotes.
#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace cassinax.autofix
{
    /// <summary>
    /// Versao de pacote no formato <c>maior.menor.correcao</c>.
    ///
    /// O motor compara versoes para decidir qual instalacao fica. Comparar texto nao serve:
    /// "0.10.0" e menor que "0.9.0" em ordem alfabetica e maior em ordem numerica, e a
    /// resposta certa e a numerica.
    /// </summary>
    public struct AutoFixVersion : IComparable<AutoFixVersion>
    {
        private static readonly Regex Declarada = new Regex(
            @"v(?<maior>\d+)\.(?<menor>\d+)\.(?<correcao>\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public int Maior { get; private set; }
        public int Menor { get; private set; }
        public int Correcao { get; private set; }

        /// <summary>Falso quando a versao nao pode ser lida do arquivo.</summary>
        public bool Conhecida { get; private set; }

        public static AutoFixVersion Desconhecida => new AutoFixVersion();

        public AutoFixVersion(int maior, int menor, int correcao)
        {
            Maior = maior; Menor = menor; Correcao = correcao; Conhecida = true;
        }

        /// <summary>
        /// Le a primeira versao declarada no texto, no formato <c>vX.Y.Z</c>.
        /// Devolve <see cref="Desconhecida"/> quando nao encontra.
        /// </summary>
        public static AutoFixVersion Ler(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return Desconhecida;
            Match m = Declarada.Match(texto);
            if (!m.Success) return Desconhecida;
            return new AutoFixVersion(
                int.Parse(m.Groups["maior"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["menor"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["correcao"].Value, CultureInfo.InvariantCulture));
        }

        public int CompareTo(AutoFixVersion outra)
        {
            // Versao desconhecida perde de qualquer versao conhecida: na duvida, quem
            // se identifica tem precedencia sobre um arquivo sem assinatura de versao.
            if (!Conhecida && !outra.Conhecida) return 0;
            if (!Conhecida) return -1;
            if (!outra.Conhecida) return 1;
            if (Maior != outra.Maior) return Maior.CompareTo(outra.Maior);
            if (Menor != outra.Menor) return Menor.CompareTo(outra.Menor);
            return Correcao.CompareTo(outra.Correcao);
        }

        public bool MaiorQue(AutoFixVersion outra) => CompareTo(outra) > 0;

        public override string ToString() =>
            Conhecida ? Maior + "." + Menor + "." + Correcao : "desconhecida";
    }
}
#endif
