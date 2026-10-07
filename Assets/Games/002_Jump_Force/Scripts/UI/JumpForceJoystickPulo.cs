using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent]
    public sealed class JumpForceJoystickPulo :
        MonoBehaviour,
        IPointerDownHandler,
        IDragHandler,
        IPointerUpHandler
    {
        public JumpForcePlayer player;

        [Tooltip("Button_Pular: cursor que se move dentro da textura.")]
        [SerializeField] RectTransform botao;

        [Tooltip("Metade inferior é espelhada para cima. Para uma textura de meia-lua, deixe desligado.")]
        [SerializeField] bool espelharMetadeDeBaixo = false;

        [Tooltip("Alpha mínimo para considerar que existe pixel nessa posição.")]
        [SerializeField, Range(0.001f, 1f)] float alphaMinimo = 0.05f;

        RectTransform area;
        Image imagem;

        Vector3 centro;

        int dedo = int.MinValue;

        Sprite spriteAtual;
        Texture2D textura;
        byte[] pixels;
        int larguraMascara, alturaMascara;
        Rect textureRect;

        Vector2 ultimaDirecao;
        float ultimoRaio;
        bool cacheRaioValido;

        JumpForceInput Input => player ? player.input : null;

        void Awake()
        {
            area = (RectTransform)transform;
            imagem = GetComponent<Image>();

            if (botao)
                centro = botao.localPosition;

            if (Input != null)
                Input.PadMirror = espelharMetadeDeBaixo;

            PrepararTextura();
        }

        void PrepararTextura()
        {
            cacheRaioValido = false;

            if (!imagem || !imagem.sprite)
            {
                textura = null;
                pixels = null;
                return;
            }

            spriteAtual = imagem.sprite;
            textura = spriteAtual.texture;

            if (!textura)
            {
                pixels = null;
                return;
            }

            textureRect = spriteAtual.textureRect;
            PrepararMascaraAlpha();
        }

        void PrepararMascaraAlpha()
        {
            // O atlas pode ficar sem Read/Write. Guardamos so um byte de alpha por pixel do sprite.
            larguraMascara = Mathf.Max(1, Mathf.RoundToInt(textureRect.width));
            alturaMascara = Mathf.Max(1, Mathf.RoundToInt(textureRect.height));
            var anterior = RenderTexture.active;
            var alvo = RenderTexture.GetTemporary(larguraMascara, alturaMascara, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D leitura = null;
            try
            {
                var escala = new Vector2(textureRect.width / textura.width, textureRect.height / textura.height);
                var deslocamento = new Vector2(textureRect.x / textura.width, textureRect.y / textura.height);
                Graphics.Blit(textura, alvo, escala, deslocamento);
                RenderTexture.active = alvo;
                leitura = new Texture2D(larguraMascara, alturaMascara, TextureFormat.RGBA32, false, true);
                leitura.ReadPixels(new Rect(0, 0, larguraMascara, alturaMascara), 0, 0, false);
                var dados = leitura.GetRawTextureData<byte>();
                pixels = new byte[larguraMascara * alturaMascara];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = dados[i * 4 + 3];
            }
            finally
            {
                RenderTexture.active = anterior;
                RenderTexture.ReleaseTemporary(alvo);
                if (leitura)
                {
                    if (Application.isPlaying) Destroy(leitura);
                    else DestroyImmediate(leitura);
                }
            }
        }

        void OnDisable()
        {
            if (dedo != int.MinValue)
            {
                Input?.SetAimPad(Vector2.zero, false);
                Input?.SetJumpSource(this, false);
            }

            dedo = int.MinValue;

            if (botao)
                botao.localPosition = centro;
        }

        public void OnPointerDown(PointerEventData dados)
        {
            if (dedo != int.MinValue || !player || !player.JoystickPuloDisponivel)
                return;

            dedo = dados.pointerId;

            Input?.SetJumpSource(this, true);

            Mover(dados, true);
        }

        public void OnDrag(PointerEventData dados)
        {
            if (dados.pointerId == dedo)
                Mover(dados, true);
        }

        public void OnPointerUp(PointerEventData dados)
        {
            if (dados.pointerId != dedo)
                return;

            Mover(dados, false);

            Input?.SetJumpSource(this, false);

            dedo = int.MinValue;
        }

        void Mover(PointerEventData dados, bool segurando)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    area,
                    dados.position,
                    dados.pressEventCamera,
                    out Vector2 local))
                return;

            Vector2 delta = local - (Vector2)centro;

            Vector2 direcao = DirecaoPermitida(delta);

            if (direcao.sqrMagnitude < 0.000001f)
            {
                Input?.SetAimPad(Vector2.zero, segurando);
                return;
            }

            float distanciaDedo = delta.magnitude;

            float distanciaBorda = RaioAteBorda(direcao);

            if (distanciaBorda <= 0.001f)
            {
                Input?.SetAimPad(Vector2.zero, segurando);
                return;
            }

            float intensidade = Mathf.Clamp01(
                distanciaDedo / distanciaBorda
            );

            Vector2 valor =
                direcao * intensidade;

            Input?.SetAimPad(valor, segurando);
        }

        Vector2 DirecaoPermitida(Vector2 direcao)
        {
            if (direcao.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            if (espelharMetadeDeBaixo)
            {
                direcao.y = Mathf.Abs(direcao.y);
            }
            else
            {
                direcao.y = Mathf.Max(0f, direcao.y);
            }

            if (direcao.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            return direcao.normalized;
        }

        float RaioAteBorda(Vector2 direcao)
        {
            direcao = DirecaoPermitida(direcao);

            if (direcao == Vector2.zero)
                return 0f;

            if (
                cacheRaioValido &&
                Vector2.Dot(direcao, ultimaDirecao) > 0.99999f
            )
            {
                return ultimoRaio;
            }

            float resultado = CalcularRaioAteBorda(direcao);

            ultimaDirecao = direcao;
            ultimoRaio = resultado;
            cacheRaioValido = true;

            return resultado;
        }

        float CalcularRaioAteBorda(Vector2 direcao)
        {
            if (pixels == null || textura == null || !imagem)
                return 0f;

            Rect desenho = RectDesenho();

            float distanciaMaxima =
                DistanciaAteLimiteDoRect(direcao, desenho);

            if (distanciaMaxima <= 0f)
                return 0f;

            int amostras = Mathf.Clamp(
                Mathf.CeilToInt(
                    Mathf.Max(
                        textureRect.width,
                        textureRect.height
                    )
                ),
                64,
                2048
            );

            bool encontrouPixel = false;
            float ultimaPosicaoValida = 0f;

            for (int i = 0; i <= amostras; i++)
            {
                float distancia =
                    distanciaMaxima * i / amostras;

                Vector2 ponto =
                    (Vector2)centro +
                    direcao * distancia;

                bool temPixel =
                    TemPixel(ponto, desenho);

                if (temPixel)
                {
                    encontrouPixel = true;
                    ultimaPosicaoValida = distancia;
                }
                else if (encontrouPixel)
                {
                    // Saiu da área visível da textura.
                    break;
                }
            }

            return ultimaPosicaoValida;
        }

        bool TemPixel(Vector2 local, Rect desenho)
        {
            if (
                local.x < desenho.xMin ||
                local.x > desenho.xMax ||
                local.y < desenho.yMin ||
                local.y > desenho.yMax
            )
                return false;

            float u =
                Mathf.InverseLerp(
                    desenho.xMin,
                    desenho.xMax,
                    local.x
                );

            float v =
                Mathf.InverseLerp(
                    desenho.yMin,
                    desenho.yMax,
                    local.y
                );

            int x = Mathf.Clamp(Mathf.FloorToInt(u * larguraMascara), 0, larguraMascara - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * alturaMascara), 0, alturaMascara - 1);
            return pixels[y * larguraMascara + x] / 255f >= alphaMinimo;
        }

        Rect RectDesenho()
        {
            Rect rect = area.rect;

            if (
                !imagem ||
                !imagem.sprite ||
                !imagem.preserveAspect
            )
                return rect;

            float spriteWidth =
                imagem.sprite.rect.width;

            float spriteHeight =
                imagem.sprite.rect.height;

            if (
                spriteWidth <= 0f ||
                spriteHeight <= 0f
            )
                return rect;

            float spriteAspect =
                spriteWidth / spriteHeight;

            float rectAspect =
                rect.width / rect.height;

            if (spriteAspect > rectAspect)
            {
                float altura =
                    rect.width / spriteAspect;

                float centroY = rect.center.y;

                rect.yMin = centroY - altura * 0.5f;
                rect.yMax = centroY + altura * 0.5f;
            }
            else
            {
                float largura =
                    rect.height * spriteAspect;

                float centroX = rect.center.x;

                rect.xMin = centroX - largura * 0.5f;
                rect.xMax = centroX + largura * 0.5f;
            }

            return rect;
        }

        float DistanciaAteLimiteDoRect(
            Vector2 direcao,
            Rect rect)
        {
            float tx = float.PositiveInfinity;
            float ty = float.PositiveInfinity;

            if (direcao.x > 0.0001f)
            {
                tx =
                    (rect.xMax - centro.x) /
                    direcao.x;
            }
            else if (direcao.x < -0.0001f)
            {
                tx =
                    (rect.xMin - centro.x) /
                    direcao.x;
            }

            if (direcao.y > 0.0001f)
            {
                ty =
                    (rect.yMax - centro.y) /
                    direcao.y;
            }
            else if (direcao.y < -0.0001f)
            {
                ty =
                    (rect.yMin - centro.y) /
                    direcao.y;
            }

            return Mathf.Max(
                0f,
                Mathf.Min(tx, ty)
            );
        }

        void LateUpdate()
        {
            if (!area || !imagem || !botao)
                return;

            if (imagem.sprite != spriteAtual)
                PrepararTextura();

            JumpForceInput input = Input;

            if (input == null)
                return;

            input.PadMirror =
                espelharMetadeDeBaixo;

            Vector2 aim = input.AimPad;

            float intensidade =
                Mathf.Clamp01(aim.magnitude);

            if (intensidade <= 0.0001f)
            {
                botao.localPosition = centro;
                return;
            }

            Vector2 direcao =
                DirecaoPermitida(aim);

            float distanciaBorda =
                RaioAteBorda(direcao);

            botao.localPosition =
                centro +
                (Vector3)(
                    direcao *
                    distanciaBorda *
                    intensidade
                );
        }
    }
}