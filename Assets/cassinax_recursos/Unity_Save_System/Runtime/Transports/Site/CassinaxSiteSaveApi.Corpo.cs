// Cassinax Unity System Save - v1.0.0
// Montagem do corpo JSON, simples e no modo chunked do portal.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
namespace cassinax.savesystem
{
    public partial class CassinaxSiteSaveApi
    {
        //------------------------------------------------------------- Montagem do corpo

        private string MontarCorpoSimples(string slot, string payload, string hash, int totalBytes, int structureVersion, int coreVersion)
        {
            var sb = new StringBuilder(payload.Length + 512);

            sb.Append('{');
            sb.Append("\"jogo_id\":").Append(_contexto.JogoId).Append(',');
            sb.Append("\"slot\":\"").Append(SaveJsonMinimo.Escapar(slot)).Append("\",");
            sb.Append("\"csrf_token\":\"").Append(SaveJsonMinimo.Escapar(_contexto.CsrfToken)).Append("\",");
            sb.Append("\"dados\":{");
            sb.Append("\"payload\":\"").Append(SaveJsonMinimo.Escapar(payload)).Append("\",");
            sb.Append("\"payload_hash\":\"").Append(hash).Append("\",");
            sb.Append("\"payload_bytes\":").Append(totalBytes.ToString(CultureInfo.InvariantCulture));
            AnexarVersoes(sb, structureVersion, coreVersion);
            sb.Append('}');
            sb.Append('}');

            return sb.ToString();
        }

        private bool TryMontarCorpoChunked(
            string slot,
            string payload,
            string hash,
            int totalBytes,
            int structureVersion,
            int coreVersion,
            out string corpo,
            out string error)
        {
            corpo = null;
            error = null;

            // O payload exportado e Base64 com prefixo ASCII, entao caractere equivale a byte.
            int maxCharsPorChunk = Mathf.Min(_bytesParaUsarChunked, LIMITE_BYTES_POR_CHUNK);
            if (maxCharsPorChunk <= 0)
            {
                error = "Limite de bytes por chunk invalido.";
                return false;
            }

            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(payload.Length / (float)maxCharsPorChunk));

            if (totalChunks > MAXIMO_DE_CHUNKS)
            {
                error = $"Save exigiria {totalChunks} chunks, acima do maximo de {MAXIMO_DE_CHUNKS} do site.";
                return false;
            }

            var sb = new StringBuilder(payload.Length + 1024);

            sb.Append('{');
            sb.Append("\"jogo_id\":").Append(_contexto.JogoId).Append(',');
            sb.Append("\"slot\":\"").Append(SaveJsonMinimo.Escapar(slot)).Append("\",");
            sb.Append("\"csrf_token\":\"").Append(SaveJsonMinimo.Escapar(_contexto.CsrfToken)).Append("\",");
            sb.Append("\"dados\":{");
            sb.Append("\"chunked\":true,");
            sb.Append("\"chunks\":[");

            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * maxCharsPorChunk;
                int length = Mathf.Min(maxCharsPorChunk, payload.Length - start);

                if (i > 0)
                    sb.Append(',');

                sb.Append("{\"index\":").Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"payload\":\"").Append(SaveJsonMinimo.Escapar(payload.Substring(start, length)));
                sb.Append("\"}");
            }

            sb.Append("],");
            sb.Append("\"meta\":{\"totalChunks\":").Append(totalChunks.ToString(CultureInfo.InvariantCulture)).Append("},");
            sb.Append("\"payload_hash\":\"").Append(hash).Append("\",");
            sb.Append("\"payload_bytes\":").Append(totalBytes.ToString(CultureInfo.InvariantCulture));
            AnexarVersoes(sb, structureVersion, coreVersion);
            sb.Append('}');
            sb.Append('}');

            corpo = sb.ToString();
            return true;
        }

        private void AnexarVersoes(StringBuilder sb, int structureVersion, int coreVersion)
        {
            if (structureVersion > 0)
                sb.Append(",\"structureVersion\":").Append(structureVersion.ToString(CultureInfo.InvariantCulture));

            if (coreVersion > 0)
                sb.Append(",\"coreVersion\":").Append(coreVersion.ToString(CultureInfo.InvariantCulture));
        }

    }
}
