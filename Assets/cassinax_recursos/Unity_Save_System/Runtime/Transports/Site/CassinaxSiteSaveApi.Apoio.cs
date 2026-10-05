// Cassinax Unity System Save - v1.0.0
// Validacoes, normalizacoes, hash e cache da configuracao remota.
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
        //------------------------------------------------------------- Utilitarios

        private bool ValidarContextoParaEscrita(out string error)
        {
            if (!_contexto.UsuarioLogado)
            {
                error = "Usuario nao esta logado no site.";
                return false;
            }

            if (_contexto.JogoId <= 0)
            {
                error = "ID do jogo indisponivel. Verifique window.JOGO_ID na pagina.";
                return false;
            }

            error = null;
            return true;
        }

        private bool ValidarContextoParaLeitura(out string error)
        {
            return ValidarContextoParaEscrita(out error);
        }

        private bool TryNormalizarSlot(string slotId, out string slot, out string error)
        {
            slot = string.IsNullOrWhiteSpace(slotId) ? "main" : slotId.Trim();
            error = null;

            if (SlotValidoRegex.IsMatch(slot))
                return true;

            error = $"Slot '{slot}' invalido. Use ate 80 caracteres entre A-Z a-z 0-9 . _ : -";
            slot = null;
            return false;
        }

        private string MontarUrl(string rotaOuUrl)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                return rotaOuUrl;

            return _baseUrl.TrimEnd('/') + rotaOuUrl;
        }

        private static string CalcularSha256(string texto)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(texto ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));

                return sb.ToString();
            }
        }

        private static string NormalizarIdioma(string idioma)
        {
            string normalizado = (idioma ?? string.Empty).Trim().ToLowerInvariant();

            foreach (string suportado in IdiomasSuportados)
            {
                if (normalizado == suportado)
                    return suportado;
            }

            return "pt";
        }

        private string DescreverErro(UnityWebRequest req)
        {
            string corpo = req.downloadHandler != null ? req.downloadHandler.text : null;

            if (!string.IsNullOrEmpty(corpo) &&
                SaveJsonMinimo.TryLerString(corpo, "erro", out string erroApi) &&
                !string.IsNullOrEmpty(erroApi))
            {
                return $"HTTP {req.responseCode}: {erroApi}";
            }

            return $"HTTP {req.responseCode}: {req.error}";
        }

        private static string Resumir(string texto)
        {
            texto = (texto ?? string.Empty).Replace("\n", " ").Replace("\r", " ");
            return texto.Length <= 200 ? texto : texto.Substring(0, 200) + "...";
        }

        private void Responder(Action<CloudSaveResult> onComplete, CloudSaveResult resultado)
        {
            if (_logDetalhado)
            {
                Debug.Log(resultado.Success
                    ? $"[CassinaxSiteSaveApi] OK ({resultado.StatusCode})."
                    : $"[CassinaxSiteSaveApi] Falha ({resultado.StatusCode}).");
            }

            onComplete?.Invoke(resultado);
        }

        //------------------------------------------------------------- Cache da config remota

        private string ObterETagSalva(string gameKey)
        {
            return PlayerPrefs.GetString(PREF_ETAG + gameKey, string.Empty);
        }

        private string ObterConfigSalva(string gameKey)
        {
            if (_configGameKeyEmMemoria == gameKey && !string.IsNullOrEmpty(_configJsonEmMemoria))
                return _configJsonEmMemoria;

            return PlayerPrefs.GetString(PREF_CONFIG + gameKey, string.Empty);
        }

        private void SalvarConfig(string gameKey, string json, string etag)
        {
            _configGameKeyEmMemoria = gameKey;
            _configJsonEmMemoria = json;
            _configETagEmMemoria = etag;

            PlayerPrefs.SetString(PREF_CONFIG + gameKey, json ?? string.Empty);
            PlayerPrefs.SetString(PREF_ETAG + gameKey, etag ?? string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>ETag da ultima configuracao remota recebida nesta sessao.</summary>
        public string ConfigETag => _configETagEmMemoria;
    }
}
