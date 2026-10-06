using UnityEngine;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent]
    public sealed class JumpForceBiomeController : MonoBehaviour
    {
        public JumpForceSpawner spawner;
        [Tooltip("Na mesma ordem de Settings > Biomas no Spawner.")]
        public Material[] skyboxes = System.Array.Empty<Material>();
        [Min(0)] public float segundosTransicao = 2;
        public int BiomaAtual { get; private set; } = -1;
        public int NivelMaximo { get; private set; }
        public bool EmTransicao => destino;

        Material original, atribuido, mistura, destino;
        float tempo;
        static readonly int ProximaTextura = Shader.PropertyToID("_NextTex");
        static readonly int ProximaCor = Shader.PropertyToID("_NextTint");
        static readonly int ProximaExposicao = Shader.PropertyToID("_NextExposure");
        static readonly int ProximaRotacao = Shader.PropertyToID("_NextRotation");
        static readonly int Proporcao = Shader.PropertyToID("_Blend");
        static readonly int Textura = Shader.PropertyToID("_MainTex");
        static readonly int Cor = Shader.PropertyToID("_Tint");
        static readonly int Exposicao = Shader.PropertyToID("_Exposure");
        static readonly int Rotacao = Shader.PropertyToID("_Rotation");

        void OnEnable() { original = RenderSettings.skybox; Resetar(); }

        public void Resetar()
        {
            destino = null;
            tempo = 0;
            NivelMaximo = 0;
            BiomaAtual = spawner && spawner.settings.useBiomes
                ? JumpForceBiomeRules.Indice(spawner.settings.biomes, 0) : -1;
            Definir(Ceu(BiomaAtual) ? Ceu(BiomaAtual) : original);
        }

        Material Ceu(int indice) => skyboxes != null && indice >= 0 && indice < skyboxes.Length ? skyboxes[indice] : null;
        void Definir(Material material) { RenderSettings.skybox = material; atribuido = material; }

        void LateUpdate()
        {
            if (!spawner || !spawner.player || !spawner.player.GameplayEnabled || spawner.player.Dead) return;
            if (spawner.settings.useBiomes)
            {
                NivelMaximo = Mathf.Max(NivelMaximo, spawner.CurrentLevel);
                int indice = JumpForceBiomeRules.Indice(spawner.settings.biomes, NivelMaximo);
                if (indice != BiomaAtual) Trocar(indice);
            }
            if (!destino) return;
            tempo += Time.deltaTime;
            float proporcao = segundosTransicao <= 0 ? 1 : Mathf.Clamp01(tempo / segundosTransicao);
            mistura.SetFloat(Proporcao, Mathf.SmoothStep(0, 1, proporcao));
            if (proporcao >= 1) { Definir(destino); destino = null; }
        }

        void Trocar(int indice)
        {
            var proximo = Ceu(indice);
            var anterior = Ceu(BiomaAtual) ? Ceu(BiomaAtual) : RenderSettings.skybox;
            BiomaAtual = indice;
            destino = null;
            if (!proximo) return;
            if (!anterior || anterior.shader != proximo.shader || !anterior.HasProperty(ProximaTextura) || segundosTransicao <= 0)
            { Definir(proximo); return; }
            if (!mistura) mistura = new Material(anterior) { name = "JumpForce_Skybox_Transicao", hideFlags = HideFlags.DontSave };
            mistura.shader = anterior.shader;
            mistura.CopyPropertiesFromMaterial(anterior);
            mistura.SetTexture(ProximaTextura, proximo.GetTexture(Textura));
            mistura.SetColor(ProximaCor, proximo.GetColor(Cor));
            mistura.SetFloat(ProximaExposicao, proximo.GetFloat(Exposicao));
            mistura.SetFloat(ProximaRotacao, proximo.GetFloat(Rotacao));
            mistura.SetFloat(Proporcao, 0);
            tempo = 0;
            destino = proximo;
            Definir(mistura);
        }

        void OnDisable()
        {
            if (RenderSettings.skybox == atribuido) RenderSettings.skybox = original;
            destino = null;
            if (mistura)
            {
                if (Application.isPlaying) Destroy(mistura);
                else DestroyImmediate(mistura);
            }
            mistura = null;
        }
    }
}
