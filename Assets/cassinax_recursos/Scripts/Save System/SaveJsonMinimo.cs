// Cassinax Unity System Save
// Version: v0.10.0
// Status: integration-pilot

using System;
using System.Globalization;
using System.Text;

namespace cassinax.savesystem
{
    /// <summary>
    /// Leitor/escritor JSON minimo para as respostas das APIs do site.
    /// Existe para nao obrigar o pacote a depender de Newtonsoft.Json.
    ///
    /// Nao e um parser JSON completo: le apenas valores escalares por nome de chave,
    /// respeitando escapes de string. Suficiente para respostas como
    /// {"sucesso":true,"dados":{"payload":"...","structureVersion":3}}.
    /// </summary>
    public static class SaveJsonMinimo
    {
        /// <summary>Escapa uma string para uso dentro de JSON, sem as aspas externas.</summary>
        public static string Escapar(string valor)
        {
            if (string.IsNullOrEmpty(valor))
                return string.Empty;

            var sb = new StringBuilder(valor.Length + 16);

            foreach (char c in valor)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>Le uma string pelo nome da chave. Retorna false se a chave nao existir ou nao for string.</summary>
        public static bool TryLerString(string json, string chave, out string valor)
        {
            valor = null;

            int inicio = EncontrarInicioDoValor(json, chave);
            if (inicio < 0 || json[inicio] != '"')
                return false;

            return TryLerStringLiteral(json, inicio, out valor, out _);
        }

        /// <summary>Le um bool pelo nome da chave. Aceita true/false e as strings "true"/"1".</summary>
        public static bool TryLerBool(string json, string chave, out bool valor)
        {
            valor = false;

            int inicio = EncontrarInicioDoValor(json, chave);
            if (inicio < 0)
                return false;

            if (json[inicio] == '"')
            {
                if (!TryLerStringLiteral(json, inicio, out string texto, out _))
                    return false;

                texto = (texto ?? string.Empty).Trim();
                valor = texto.Equals("true", StringComparison.OrdinalIgnoreCase) || texto == "1";
                return true;
            }

            if (CompararLiteral(json, inicio, "true"))
            {
                valor = true;
                return true;
            }

            if (CompararLiteral(json, inicio, "false"))
            {
                valor = false;
                return true;
            }

            if (TryLerNumero(json, inicio, out double numero))
            {
                valor = Math.Abs(numero) > double.Epsilon;
                return true;
            }

            return false;
        }

        /// <summary>Le um inteiro pelo nome da chave. Aceita numero puro ou numero como string.</summary>
        public static bool TryLerInt(string json, string chave, out int valor)
        {
            valor = 0;

            int inicio = EncontrarInicioDoValor(json, chave);
            if (inicio < 0)
                return false;

            if (json[inicio] == '"')
            {
                return TryLerStringLiteral(json, inicio, out string texto, out _) &&
                       int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out valor);
            }

            if (!TryLerNumero(json, inicio, out double numero))
                return false;

            valor = (int)numero;
            return true;
        }

        /// <summary>Retorna true quando a chave existe e o valor e null.</summary>
        public static bool ValorEhNulo(string json, string chave)
        {
            int inicio = EncontrarInicioDoValor(json, chave);
            return inicio >= 0 && CompararLiteral(json, inicio, "null");
        }

        /// <summary>Retorna true quando a chave existe no JSON, com qualquer valor.</summary>
        public static bool ContemChave(string json, string chave)
        {
            return EncontrarInicioDoValor(json, chave) >= 0;
        }

        //------------------------------------------------------------- Interno

        /// <summary>
        /// Localiza o primeiro caractere do valor associado a "chave".
        /// Ignora ocorrencias da chave que estejam dentro de valores string.
        /// </summary>
        private static int EncontrarInicioDoValor(string json, string chave)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(chave))
                return -1;

            int i = 0;

            while (i < json.Length)
            {
                char c = json[i];

                if (c != '"')
                {
                    i++;
                    continue;
                }

                if (!TryLerStringLiteral(json, i, out string nome, out int depoisDaString))
                    return -1;

                int j = PularEspacos(json, depoisDaString);

                // Só é nome de chave se o próximo token for ':'.
                if (j >= json.Length || json[j] != ':')
                {
                    i = depoisDaString;
                    continue;
                }

                j = PularEspacos(json, j + 1);

                if (nome == chave)
                    return j < json.Length ? j : -1;

                i = depoisDaString;
            }

            return -1;
        }

        private static bool TryLerStringLiteral(string json, int indiceDaAspaInicial, out string valor, out int indiceDepois)
        {
            valor = null;
            indiceDepois = indiceDaAspaInicial;

            if (indiceDaAspaInicial < 0 || indiceDaAspaInicial >= json.Length || json[indiceDaAspaInicial] != '"')
                return false;

            var sb = new StringBuilder();
            int i = indiceDaAspaInicial + 1;

            while (i < json.Length)
            {
                char c = json[i];

                if (c == '"')
                {
                    valor = sb.ToString();
                    indiceDepois = i + 1;
                    return true;
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                i++;
                if (i >= json.Length)
                    return false;

                char escape = json[i];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= json.Length)
                            return false;

                        string hex = json.Substring(i + 1, 4);
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codigo))
                            return false;

                        sb.Append((char)codigo);
                        i += 4;
                        break;
                    default:
                        sb.Append(escape);
                        break;
                }

                i++;
            }

            return false;
        }

        private static bool TryLerNumero(string json, int inicio, out double valor)
        {
            valor = 0;
            int fim = inicio;

            while (fim < json.Length)
            {
                char c = json[fim];
                bool parteDeNumero = char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';

                if (!parteDeNumero)
                    break;

                fim++;
            }

            return fim > inicio &&
                   double.TryParse(
                       json.Substring(inicio, fim - inicio),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out valor);
        }

        private static bool CompararLiteral(string json, int inicio, string literal)
        {
            return inicio + literal.Length <= json.Length &&
                   string.CompareOrdinal(json, inicio, literal, 0, literal.Length) == 0;
        }

        private static int PularEspacos(string json, int indice)
        {
            while (indice < json.Length && char.IsWhiteSpace(json[indice]))
                indice++;

            return indice;
        }
    }
}
