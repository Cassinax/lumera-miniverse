// Cassinax Unity System Save - v1.0.0
// Operacoes publicas de save, login e config; rotinas de transporte.
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
        //------------------------------------------------------------- IAsyncCloudSaveAdapter

        /// <summary>Envia o payload completo do save para o slot informado.</summary>
        public void UploadSave(string slotId, string payload, Action<CloudSaveResult> onComplete)
        {
            UploadSave(slotId, payload, 0, 0, onComplete);
        }

        /// <summary>
        /// Envia o payload completo informando tambem as versoes de estrutura e de Core,
        /// que o site guarda junto do save para permitir migracao no cliente.
        /// </summary>
        public void UploadSave(string slotId, string payload, int structureVersion, int coreVersion, Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(UploadSaveRotina(slotId, payload, structureVersion, coreVersion, onComplete));
        }

        /// <summary>Baixa o payload do slot. Sucesso com Payload vazio significa "sem save no site".</summary>
        public void DownloadSave(string slotId, Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(DownloadSaveRotina(slotId, onComplete));
        }

        /// <summary>Apaga o save de um slot. Use somente com confirmacao explicita do jogador.</summary>
        public void DeleteSave(string slotId, Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(DeleteSaveRotina(slotId, false, onComplete));
        }

        /// <summary>Apaga todos os slots do jogo. Use somente com confirmacao explicita do jogador.</summary>
        public void DeleteAllSlots(Action<CloudSaveResult> onComplete)
        {
            StartCoroutine(DeleteSaveRotina(null, true, onComplete));
        }

        /// <summary>Consulta se a sessao do site continua logada.</summary>
        public void VerificarLogin(Action<bool> onComplete)
        {
            StartCoroutine(VerificarLoginRotina(onComplete));
        }

        /// <summary>
        /// Consulta a configuracao remota do jogo. Devolve o JSON cru para o projeto
        /// interpretar, mais o ETag usado no cache.
        /// </summary>
        public void ObterConfigRemota(Action<CassinaxGameConfigResult> onComplete)
        {
            StartCoroutine(ObterConfigRemotaRotina(GameConfigKey, onComplete));
        }

        //------------------------------------------------------------- Rotinas

        private IEnumerator UploadSaveRotina(string slotId, string payload, int structureVersion, int coreVersion, Action<CloudSaveResult> onComplete)
        {
            AtualizarContexto();

            if (!ValidarContextoParaEscrita(out string erroContexto))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroContexto));
                yield break;
            }

            if (!TryNormalizarSlot(slotId, out string slot, out string erroSlot))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroSlot));
                yield break;
            }

            payload = payload ?? string.Empty;
            int totalBytes = Encoding.UTF8.GetByteCount(payload);

            if (totalBytes > LIMITE_BYTES_POR_SLOT)
            {
                Responder(onComplete, CloudSaveResult.Fail(
                    $"Save de {totalBytes} bytes excede o limite de {LIMITE_BYTES_POR_SLOT} bytes por slot do site."));
                yield break;
            }

            yield return GarantirCsrf();

            if (string.IsNullOrEmpty(_contexto.CsrfToken))
            {
                Responder(onComplete, CloudSaveResult.Fail("Token CSRF indisponivel."));
                yield break;
            }

            string hash = CalcularSha256(payload);
            string corpo;

            if (totalBytes > _bytesParaUsarChunked)
            {
                if (!TryMontarCorpoChunked(slot, payload, hash, totalBytes, structureVersion, coreVersion, out corpo, out string erroChunk))
                {
                    Responder(onComplete, CloudSaveResult.Fail(erroChunk));
                    yield break;
                }
            }
            else
            {
                corpo = MontarCorpoSimples(slot, payload, hash, totalBytes, structureVersion, coreVersion);
            }

            yield return EnviarComTentativas(
                () => CriarPost(ROTA_SALVAR, corpo),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        Responder(onComplete, resultado);
                        return;
                    }

                    Responder(onComplete, InterpretarSucesso(resultado, payload));
                });
        }

        private IEnumerator DownloadSaveRotina(string slotId, Action<CloudSaveResult> onComplete)
        {
            AtualizarContexto();

            if (!ValidarContextoParaLeitura(out string erroContexto))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroContexto));
                yield break;
            }

            if (!TryNormalizarSlot(slotId, out string slot, out string erroSlot))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroSlot));
                yield break;
            }

            string url = MontarUrl($"{ROTA_CARREGAR}?jogo_id={_contexto.JogoId}&slot={UnityWebRequest.EscapeURL(slot)}");

            yield return EnviarComTentativas(
                () => CriarGet(url),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        Responder(onComplete, resultado);
                        return;
                    }

                    Responder(onComplete, InterpretarCarregamento(resultado));
                });
        }

        private IEnumerator DeleteSaveRotina(string slotId, bool todosSlots, Action<CloudSaveResult> onComplete)
        {
            AtualizarContexto();

            if (!ValidarContextoParaEscrita(out string erroContexto))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroContexto));
                yield break;
            }

            string slot = "main";
            if (!todosSlots && !TryNormalizarSlot(slotId, out slot, out string erroSlot))
            {
                Responder(onComplete, CloudSaveResult.Fail(erroSlot));
                yield break;
            }

            yield return GarantirCsrf();

            if (string.IsNullOrEmpty(_contexto.CsrfToken))
            {
                Responder(onComplete, CloudSaveResult.Fail("Token CSRF indisponivel."));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"jogo_id\":").Append(_contexto.JogoId).Append(',');
            sb.Append("\"csrf_token\":\"").Append(SaveJsonMinimo.Escapar(_contexto.CsrfToken)).Append('"');

            if (todosSlots)
                sb.Append(",\"todos_slots\":true");
            else
                sb.Append(",\"slot\":\"").Append(SaveJsonMinimo.Escapar(slot)).Append('"');

            sb.Append('}');

            string corpo = sb.ToString();

            yield return EnviarComTentativas(
                () => CriarPost(ROTA_APAGAR, corpo),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        Responder(onComplete, resultado);
                        return;
                    }

                    Responder(onComplete, InterpretarSucesso(resultado, null));
                });
        }

        private IEnumerator VerificarLoginRotina(Action<bool> onComplete)
        {
            AtualizarContexto();

            yield return EnviarComTentativas(
                () => CriarGet(MontarUrl(ROTA_CHECK_LOGIN)),
                resultado =>
                {
                    if (!resultado.Success)
                    {
                        onComplete?.Invoke(false);
                        return;
                    }

                    string json = resultado.Payload ?? string.Empty;
                    bool logado;

                    if (!SaveJsonMinimo.TryLerBool(json, "logado", out logado) &&
                        !SaveJsonMinimo.TryLerBool(json, "usuario_logado", out logado) &&
                        !SaveJsonMinimo.TryLerBool(json, "logged_in", out logado))
                    {
                        SaveJsonMinimo.TryLerBool(json, "sucesso", out logado);
                    }

                    onComplete?.Invoke(logado);
                });
        }

        private IEnumerator ObterConfigRemotaRotina(string gameKey, Action<CassinaxGameConfigResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                onComplete?.Invoke(new CassinaxGameConfigResult
                {
                    Success = false,
                    Error = "gameKey da configuracao remota nao definida."
                });
                yield break;
            }

            string etagConhecida = ObterETagSalva(gameKey);
            string url = MontarUrl(ROTA_GAME_CONFIG + UnityWebRequest.EscapeURL(gameKey));

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(1, _timeoutSegundos);

                if (!string.IsNullOrEmpty(etagConhecida))
                    req.SetRequestHeader("If-None-Match", etagConhecida);

                yield return req.SendWebRequest();

                long status = req.responseCode;

                if (status == 304)
                {
                    onComplete?.Invoke(new CassinaxGameConfigResult
                    {
                        Success = true,
                        NotModified = true,
                        Json = ObterConfigSalva(gameKey),
                        ETag = etagConhecida,
                        StatusCode = status
                    });
                    yield break;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Config remota indisponivel nao deve derrubar o jogo:
                    // devolve o ultimo JSON conhecido quando existir.
                    string cache = ObterConfigSalva(gameKey);

                    onComplete?.Invoke(new CassinaxGameConfigResult
                    {
                        Success = !string.IsNullOrEmpty(cache),
                        NotModified = !string.IsNullOrEmpty(cache),
                        Json = cache,
                        ETag = etagConhecida,
                        Error = DescreverErro(req),
                        StatusCode = status
                    });
                    yield break;
                }

                string json = req.downloadHandler.text;
                string etag = req.GetResponseHeader("ETag");

                if (string.IsNullOrEmpty(etag))
                    etag = req.GetResponseHeader("X-Cassinax-Config-ETag");

                SalvarConfig(gameKey, json, etag);

                if (_logDetalhado)
                    Debug.Log("[CassinaxSiteSaveApi] ConfigUpdated");

                onComplete?.Invoke(new CassinaxGameConfigResult
                {
                    Success = true,
                    NotModified = false,
                    Json = json,
                    ETag = etag,
                    StatusCode = status
                });
            }
        }

        private IEnumerator GarantirCsrf()
        {
            if (!string.IsNullOrEmpty(_contexto.CsrfToken))
                yield break;

            using (UnityWebRequest req = UnityWebRequest.Get(MontarUrl(ROTA_CSRF)))
            {
                req.timeout = Mathf.Max(1, _timeoutSegundos);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[CassinaxSiteSaveApi] CsrfUnavailable HTTP {req.responseCode}");
                    yield break;
                }

                string json = req.downloadHandler.text ?? string.Empty;

                if (SaveJsonMinimo.TryLerString(json, "csrf_token", out string token) ||
                    SaveJsonMinimo.TryLerString(json, "csrfToken", out token) ||
                    SaveJsonMinimo.TryLerString(json, "token", out token))
                {
                    _csrfTokenBuscado = token;
                    _contexto.CsrfToken = token;
                }
            }
        }

    }
}
