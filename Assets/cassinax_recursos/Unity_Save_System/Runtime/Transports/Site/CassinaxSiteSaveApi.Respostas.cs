// Cassinax Unity System Save - v1.0.0
// Leitura das respostas do portal.
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
        //------------------------------------------------------------- Interpretacao das respostas

        /// <summary>Le sucesso/erro de uma resposta de salvar ou apagar.</summary>
        private CloudSaveResult InterpretarSucesso(CloudSaveResult bruto, string payloadEnviado)
        {
            string json = bruto.Payload ?? string.Empty;

            if (!SaveJsonMinimo.TryLerBool(json, "sucesso", out bool sucesso))
            {
                return CloudSaveResult.Fail(
                    $"Resposta do site em formato inesperado: {Resumir(json)}", bruto.StatusCode);
            }

            if (!sucesso)
            {
                SaveJsonMinimo.TryLerString(json, "erro", out string erro);
                return CloudSaveResult.Fail(
                    string.IsNullOrEmpty(erro) ? "O site rejeitou a operacao." : erro, bruto.StatusCode);
            }

            return CloudSaveResult.Ok(payloadEnviado, bruto.StatusCode);
        }

        /// <summary>Le o payload de uma resposta de carregar.</summary>
        private CloudSaveResult InterpretarCarregamento(CloudSaveResult bruto)
        {
            string json = bruto.Payload ?? string.Empty;

            if (!SaveJsonMinimo.TryLerBool(json, "sucesso", out bool sucesso))
            {
                return CloudSaveResult.Fail(
                    $"Resposta do site em formato inesperado: {Resumir(json)}", bruto.StatusCode);
            }

            if (!sucesso)
            {
                SaveJsonMinimo.TryLerString(json, "erro", out string erro);
                return CloudSaveResult.Fail(
                    string.IsNullOrEmpty(erro) ? "O site rejeitou a leitura do save." : erro, bruto.StatusCode);
            }

            // Sem save gravado ainda: sucesso com payload vazio.
            if (SaveJsonMinimo.ValorEhNulo(json, "dados") || !SaveJsonMinimo.ContemChave(json, "dados"))
                return CloudSaveResult.Ok(null, bruto.StatusCode);

            if (!SaveJsonMinimo.TryLerString(json, "payload", out string payload) || string.IsNullOrEmpty(payload))
            {
                return CloudSaveResult.Fail(
                    "O save no site nao tem 'dados.payload'. Ele provavelmente foi gravado por outro cliente.",
                    bruto.StatusCode);
            }

            if (_validarHashAoCarregar &&
                SaveJsonMinimo.TryLerString(json, "payload_hash", out string hashRemoto) &&
                !string.IsNullOrEmpty(hashRemoto))
            {
                string hashLocal = CalcularSha256(payload);
                if (!string.Equals(hashLocal, hashRemoto, StringComparison.OrdinalIgnoreCase))
                {
                    return CloudSaveResult.Fail(
                        "Hash do payload baixado nao confere. O save pode ter sido truncado.", bruto.StatusCode);
                }
            }

            var resultado = CloudSaveResult.Ok(payload, bruto.StatusCode);
            SaveJsonMinimo.TryLerInt(json, "structureVersion", out resultado.StructureVersion);
            SaveJsonMinimo.TryLerInt(json, "coreVersion", out resultado.CoreVersion);

            return resultado;
        }

    }
}
