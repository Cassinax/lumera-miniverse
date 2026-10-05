// Cassinax Unity System Save - v1.0.0
// Requisicoes HTTP, tentativas e backoff exponencial com sorteio.
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
        //------------------------------------------------------------- HTTP

        private UnityWebRequest CriarGet(string url)
        {
            UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = Mathf.Max(1, _timeoutSegundos);
            return req;
        }

        private UnityWebRequest CriarPost(string rota, string corpoJson)
        {
            var req = new UnityWebRequest(MontarUrl(rota), UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(corpoJson));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, _timeoutSegundos);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-CSRF-Token", _contexto.CsrfToken ?? string.Empty);
            return req;
        }

        /// <summary>
        /// Executa a requisicao repetindo apenas falhas transitorias: erro de conexao e HTTP 5xx.
        /// Erros de regra (400, 401, 413, 422) nao sao repetidos.
        /// </summary>
        private IEnumerator EnviarComTentativas(Func<UnityWebRequest> criarRequisicao, Action<CloudSaveResult> onComplete)
        {
            int maximo = Mathf.Max(1, _tentativas);
            CloudSaveResult ultimo = CloudSaveResult.Fail("Requisicao nao executada.");

            for (int tentativa = 1; tentativa <= maximo; tentativa++)
            {
                using (UnityWebRequest req = criarRequisicao())
                {
                    if (_logDetalhado)
                        Debug.Log($"[CassinaxSiteSaveApi] HTTP attempt {tentativa}/{maximo}");

                    yield return req.SendWebRequest();

                    long status = req.responseCode;
                    bool conexaoOk = req.result == UnityWebRequest.Result.Success;

                    if (conexaoOk)
                    {
                        onComplete?.Invoke(new CloudSaveResult
                        {
                            Success = true,
                            Payload = req.downloadHandler != null ? req.downloadHandler.text : string.Empty,
                            StatusCode = status
                        });
                        yield break;
                    }

                    ultimo = CloudSaveResult.Fail(DescreverErro(req), status);

                    bool transitorio = req.result == UnityWebRequest.Result.ConnectionError || status >= 500;
                    if (!transitorio || tentativa == maximo)
                        break;
                }

                yield return new WaitForSecondsRealtime(CalcularEspera(tentativa));
            }

            onComplete?.Invoke(ultimo);
        }

        /// <summary>
        /// Espera antes da proxima tentativa.
        ///
        /// Com backoff exponencial, a janela dobra a cada tentativa e o valor final e
        /// sorteado dentro dela. Dobrar reduz a pressao sobre um servidor que ja esta
        /// sobrecarregado; o sorteio impede que varios jogadores que falharam no mesmo
        /// instante voltem juntos e repitam a sobrecarga.
        /// </summary>
        private float CalcularEspera(int tentativa)
        {
            float baseEspera = Mathf.Max(0f, _esperaEntreTentativas);

            if (!_backoffExponencial || baseEspera <= 0f)
                return baseEspera;

            float teto = Mathf.Max(baseEspera, _esperaMaximaEntreTentativas);
            float janela = Mathf.Min(baseEspera * Mathf.Pow(2f, tentativa - 1), teto);

            return UnityEngine.Random.Range(janela * 0.5f, janela);
        }

    }
}
