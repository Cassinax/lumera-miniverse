#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cassinax.EngineLink
{
    /// <summary>
    /// Captura a imagem final do jogo (com UI) no fim de cada quadro do Play Mode, reduzida
    /// e lida da GPU sem travar o jogo. Somente no Editor: criado e configurado pela parte
    /// de Editor do Engine Link; nao precisa ser adicionado a cenas.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class CapturadorQuadros : MonoBehaviour
    {
        /// <summary>RGBA32, linhas de baixo para cima (como Texture2D), largura e altura.</summary>
        public static Action<byte[], int, int> QuadroPronto;

        /// <summary>Decide se este quadro deve ser capturado (FPS, quadro pendente, conexao).</summary>
        public static Func<bool> PodeCapturar;

        /// <summary>Tamanho maximo da imagem enviada; a proporcao do Game view e mantida.</summary>
        public static int LarguraMaxima = 1280;
        public static int AlturaMaxima = 1280;

        RenderTexture tela;
        RenderTexture reduzida;
        bool aguardandoLeitura;

        IEnumerator Start()
        {
            var fimDoQuadro = new WaitForEndOfFrame();
            while (true)
            {
                yield return fimDoQuadro;
                Capturar();
            }
        }

        void Capturar()
        {
            if (aguardandoLeitura || QuadroPronto == null) return;
            if (PodeCapturar != null && !PodeCapturar()) return;

            // No Editor, Screen.width/height e a resolucao atual do Game view (dinamica).
            int largura = Screen.width, altura = Screen.height;
            if (largura <= 0 || altura <= 0) return;

            Garantir(ref tela, largura, altura);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(tela);

            var escala = Mathf.Min(1f, Mathf.Min((float)LarguraMaxima / largura, (float)AlturaMaxima / altura));
            int finalLargura = Mathf.Max(1, Mathf.RoundToInt(largura * escala));
            int finalAltura = Mathf.Max(1, Mathf.RoundToInt(altura * escala));
            Garantir(ref reduzida, finalLargura, finalAltura);

            // Em APIs com UV no topo (Direct3D, Vulkan, Metal) a captura vem de cabeca para baixo.
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(tela, reduzida, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(tela, reduzida);

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                aguardandoLeitura = true;
                AsyncGPUReadback.Request(reduzida, 0, TextureFormat.RGBA32, pedido =>
                {
                    aguardandoLeitura = false;
                    var destino = QuadroPronto;
                    if (pedido.hasError || destino == null) return;
                    destino(pedido.GetData<byte>().ToArray(), finalLargura, finalAltura);
                });
            }
            else
            {
                // Alternativa sincrona para placas/APIs sem leitura assincrona.
                var anterior = RenderTexture.active;
                RenderTexture.active = reduzida;
                var textura = new Texture2D(finalLargura, finalAltura, TextureFormat.RGBA32, false);
                textura.ReadPixels(new Rect(0, 0, finalLargura, finalAltura), 0, 0);
                textura.Apply();
                RenderTexture.active = anterior;
                QuadroPronto(textura.GetRawTextureData(), finalLargura, finalAltura);
                Destroy(textura);
            }
        }

        static void Garantir(ref RenderTexture textura, int largura, int altura)
        {
            if (textura != null && textura.width == largura && textura.height == altura) return;
            if (textura != null) textura.Release();
            textura = new RenderTexture(largura, altura, 0, RenderTextureFormat.ARGB32) { name = "EngineLink Captura" };
            textura.Create();
        }

        void OnDestroy()
        {
            if (tela != null) tela.Release();
            if (reduzida != null) reduzida.Release();
        }
    }
}
#endif
