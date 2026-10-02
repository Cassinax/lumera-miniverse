// Cassinax Unity System Save
// Version: v0.10.0
// Status: integration-pilot

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace cassinax.savesystem
{
    /// <summary>
    /// Utilitario fixo da API para dividir/remontar payloads quando o provedor
    /// de nuvem limita o tamanho de cada item salvo.
    /// </summary>
    public static class SavePayloadChunks
    {
        public const int MaxParts = 4096;
        public const int MaxTransferChars = 8 * 1024 * 1024;
        private const string FormatVersion = "CUSSv1";
        private static readonly Regex TextPartRegex = new Regex(
            @"\[(\d{1,6})-(\d{1,6})\](.*?)\[fim-?(\d{1,6})\]",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        public struct TextPartsResult
        {
            public bool IsComplete;
            public int TotalParts;
            public List<int> ReceivedParts;
            public List<int> MissingParts;
            public List<string> Errors;
            public string Payload;
        }

        public static Dictionary<string, string> Split(string payload, int maxCharsPorItem, string prefixo, Func<string, string> hashProvider)
        {
            if (maxCharsPorItem <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCharsPorItem), "O limite por item deve ser maior que zero.");

            if (hashProvider == null)
                throw new ArgumentNullException(nameof(hashProvider));

            payload = payload ?? string.Empty;
            if (payload.Length > MaxTransferChars) throw new ArgumentException("Payload limit.");
            prefixo = string.IsNullOrWhiteSpace(prefixo) ? "main" : prefixo;

            string hash = hashProvider(payload);
            int totalChunks = (int)Math.Ceiling(payload.Length / (double)maxCharsPorItem);
            if (totalChunks > MaxParts) throw new ArgumentException("Part limit.");
            var itens = new Dictionary<string, string>();
            string meta = $"{FormatVersion}|{totalChunks}|{payload.Length}|{hash}";

            if (meta.Length > maxCharsPorItem)
                throw new InvalidOperationException("O limite por item e pequeno demais para armazenar os metadados do save.");

            itens[$"{prefixo}.meta"] = meta;

            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * maxCharsPorItem;
                int length = Math.Min(maxCharsPorItem, payload.Length - start);
                itens[$"{prefixo}.{i:0000}"] = payload.Substring(start, length);
            }

            return itens;
        }

        public static bool TryJoin(
            IDictionary<string, string> itens,
            string prefixo,
            Func<string, string> hashProvider,
            out string payload,
            out string error)
        {
            payload = string.Empty;
            error = string.Empty;

            if (hashProvider == null)
            {
                error = "Hash provider ausente.";
                return false;
            }

            prefixo = string.IsNullOrWhiteSpace(prefixo) ? "main" : prefixo;

            if (itens == null || !itens.TryGetValue($"{prefixo}.meta", out string meta))
            {
                error = "Metadados do save em partes nao encontrados.";
                return false;
            }

            if (meta == null || meta.Length > 256) { error = "Invalid metadata size."; return false; }
            string[] metaParts = meta.Split('|');
            if (metaParts.Length != 4 || metaParts[0] != FormatVersion)
            {
                error = "Metadados do save em partes invalidos.";
                return false;
            }

            if (!int.TryParse(metaParts[1], out int totalChunks) || totalChunks < 0 || totalChunks > MaxParts)
            {
                error = "Quantidade de partes invalida.";
                return false;
            }

            if (!int.TryParse(metaParts[2], out int expectedLength) || expectedLength < 0 || expectedLength > MaxTransferChars)
            {
                error = "Tamanho esperado invalido.";
                return false;
            }

            string expectedHash = metaParts[3];
            var builder = new StringBuilder(expectedLength);

            for (int i = 0; i < totalChunks; i++)
            {
                string key = $"{prefixo}.{i:0000}";
                if (!itens.TryGetValue(key, out string chunk))
                {
                    error = $"Parte ausente do save em nuvem: {key}";
                    return false;
                }

                if (chunk == null || (long)builder.Length + chunk.Length > expectedLength)
                { error = "Part size limit."; return false; }
                builder.Append(chunk);
            }

            payload = builder.ToString();
            if (payload.Length != expectedLength)
            {
                error = "Tamanho remontado do save em nuvem nao confere.";
                return false;
            }

            string actualHash = hashProvider(payload);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "Hash do save em nuvem remontado nao confere.";
                return false;
            }

            return true;
        }

        public static List<string> SplitForTextTransfer(string payload, int maxCharsPorParte)
        {
            if (maxCharsPorParte <= 32)
                throw new ArgumentOutOfRangeException(nameof(maxCharsPorParte), "O limite por parte deve ser maior que zero.");

            payload = payload ?? string.Empty;
            if (payload.Length > MaxTransferChars) throw new ArgumentException("Payload limit.");
            // Reserve the maximum envelope size, so the supplied limit includes both markers.
            maxCharsPorParte -= 32;
            int totalParts = Math.Max(1, (int)Math.Ceiling(payload.Length / (double)maxCharsPorParte));
            if (totalParts > MaxParts) throw new ArgumentException("Part limit.");
            var partes = new List<string>(totalParts);
            int digits = Math.Max(3, totalParts.ToString().Length);

            for (int i = 1; i <= totalParts; i++)
            {
                int start = (i - 1) * maxCharsPorParte;
                int length = Math.Min(maxCharsPorParte, Math.Max(0, payload.Length - start));
                string conteudo = length > 0 ? payload.Substring(start, length) : string.Empty;
                partes.Add($"[{i.ToString().PadLeft(digits, '0')}-{totalParts}]{conteudo}[fim-{i}]");
            }

            return partes;
        }

        public static TextPartsResult AnalyzeTextParts(IEnumerable<string> partes)
        {
            var result = new TextPartsResult
            {
                IsComplete = false,
                TotalParts = 0,
                ReceivedParts = new List<int>(),
                MissingParts = new List<int>(),
                Errors = new List<string>(),
                Payload = string.Empty
            };

            var chunks = new Dictionary<int, string>();

            if (partes == null)
            {
                result.Errors.Add("Nenhuma parte informada.");
                return result;
            }

            long transferred = 0;
            int inputs = 0;
            foreach (string parteRaw in partes)
            {
                if (++inputs > MaxParts || (transferred += parteRaw?.Length ?? 0) > MaxTransferChars)
                { result.Errors.Add("Transfer limit."); return result; }
                string parte = (parteRaw ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(parte))
                    continue;

                MatchCollection matches = TextPartRegex.Matches(parte);
                if (matches.Count == 0)
                {
                    result.Errors.Add("Parte em formato invalido.");
                    continue;
                }

                int consumed = 0;
                foreach (Match match in matches)
                {
                    if (!string.IsNullOrWhiteSpace(parte.Substring(consumed, match.Index - consumed)))
                        result.Errors.Add("Unexpected content between parts.");
                    consumed = match.Index + match.Length;
                    int index = int.Parse(match.Groups[1].Value);
                    int total = int.Parse(match.Groups[2].Value);
                    string conteudo = match.Groups[3].Value;
                    int endIndex = int.Parse(match.Groups[4].Value);

                    if (index != endIndex)
                    {
                        result.Errors.Add($"Marcador final nao confere na parte {index}.");
                        continue;
                    }

                    if (index <= 0 || total <= 0 || total > MaxParts || index > total)
                    {
                        result.Errors.Add($"Indice invalido na parte {index} de {total}.");
                        continue;
                    }

                    if (result.TotalParts == 0)
                    {
                        result.TotalParts = total;
                    }
                    else if (result.TotalParts != total)
                    {
                        result.Errors.Add($"Total de partes inconsistente: esperado {result.TotalParts}, recebido {total}.");
                        continue;
                    }

                    if (chunks.TryGetValue(index, out var previous) && previous != conteudo)
                        result.Errors.Add("Conflicting duplicate part.");
                    else chunks[index] = conteudo;
                }
                if (!string.IsNullOrWhiteSpace(parte.Substring(consumed))) result.Errors.Add("Truncated or unexpected trailing content.");
            }

            result.ReceivedParts = chunks.Keys.OrderBy(i => i).ToList();

            if (result.TotalParts <= 0)
            {
                result.Errors.Add("Metadados de partes ausentes.");
                return result;
            }

            for (int i = 1; i <= result.TotalParts; i++)
            {
                if (!chunks.ContainsKey(i))
                    result.MissingParts.Add(i);
            }

            result.IsComplete = result.MissingParts.Count == 0 && result.Errors.Count == 0;
            if (!result.IsComplete)
                return result;

            var builder = new StringBuilder();
            for (int i = 1; i <= result.TotalParts; i++)
                builder.Append(chunks[i]);

            result.Payload = builder.ToString();
            return result;
        }

        public static bool TryJoinTextParts(IEnumerable<string> partes, out string payload, out List<int> missingParts, out string error)
        {
            TextPartsResult result = AnalyzeTextParts(partes);
            payload = result.Payload ?? string.Empty;
            missingParts = result.MissingParts ?? new List<int>();
            error = result.Errors != null && result.Errors.Count > 0
                ? string.Join(" ", result.Errors)
                : string.Empty;

            return result.IsComplete;
        }
    }
}
