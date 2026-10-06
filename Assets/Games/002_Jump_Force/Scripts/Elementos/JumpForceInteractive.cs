using UnityEngine;

namespace Lumera.JumpForce
{
    // Raiz independente: basta ser filha do corpo da plataforma, com posicao local zero.
    [DisallowMultipleComponent]
    public sealed class JumpForceInteractive : MonoBehaviour
    {
        public JumpForceSpecial tipo;
        JumpForceGirador girador;
        JumpForcePlatform apoio;
        Renderer[] visuais;
        bool[] habilitados;
        Transform[] partes;
        Quaternion[] rotacoes;
        Transform pool;
        bool preparado;
        public bool Reservado { get; private set; }
        public Bounds CorpoLocal { get; private set; }

        public void Preparar()
        {
            if (preparado) return;
            preparado = true;
            girador = GetComponent<JumpForceGirador>();
            visuais = GetComponentsInChildren<Renderer>(true);
            habilitados = new bool[visuais.Length];
            for (int i = 0; i < visuais.Length; i++) habilitados[i] = visuais[i].enabled;
            partes = GetComponentsInChildren<Transform>(true);
            rotacoes = new Quaternion[partes.Length];
            for (int i = 0; i < partes.Length; i++) rotacoes[i] = partes[i].localRotation;
            bool primeiro = true;
            Bounds local = default;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                var bounds = JumpForceSpawnedElement.ColliderBounds(c);
                for (int i = 0; i < 8; i++)
                {
                    var sinal = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    var ponto = transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents, sinal));
                    if (primeiro) { local = new Bounds(ponto, Vector3.zero); primeiro = false; }
                    else local.Encapsulate(ponto);
                }
            }
            CorpoLocal = local;
        }
        void Start()
        {
            Preparar();
            if (!Reservado) ConfigurarApoio();
        }
        void ConfigurarApoio()
        {
            apoio = transform.parent ? transform.parent.GetComponent<JumpForcePlatform>() : null;
            if (apoio && tipo == JumpForceSpecial.Fan) apoio.instantJump = girador && Mathf.Abs(girador.VelocidadeRotacao.y) > 0.01f;
            // Topo do ventilador e um apoio normal e nao aciona a habilidade da base.
            foreach (var superficie in GetComponentsInChildren<JumpForcePlatform>(true))
                superficie.proprietario = null;
        }
        public void Montar(Transform parent, JumpForceTrailNode node, Transform origemPool)
        {
            Preparar();
            Reservado = true; pool = origemPool;
            transform.SetParent(parent, false);
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;
            for (int i = 0; i < partes.Length; i++) partes[i].localRotation = rotacoes[i];
            if (tipo == JumpForceSpecial.Fan)
            {
                transform.localRotation = Quaternion.Euler(0, node.fanYaw, 0);
                if (girador) girador.DefinirVelocidadeY(node.fanSpinY);
            }
            SetVisible(true);
            gameObject.SetActive(true);
            ConfigurarApoio();
        }
        public void SetVisible(bool visible)
        {
            Preparar();
            for (int i = 0; i < visuais.Length; i++) if (visuais[i]) visuais[i].enabled = visible && habilitados[i];
        }
        public void Liberar()
        {
            if (apoio && tipo == JumpForceSpecial.Fan) apoio.instantJump = false;
            gameObject.SetActive(false);
            transform.SetParent(pool, false);
            transform.localPosition = Vector3.zero;
            for (int i = 0; i < partes.Length; i++) if (partes[i]) partes[i].localRotation = rotacoes[i];
            if (girador && tipo == JumpForceSpecial.Fan) girador.DefinirVelocidadeY(0);
            SetVisible(true);
            Reservado = false;
            apoio = null;
        }
    }
}
