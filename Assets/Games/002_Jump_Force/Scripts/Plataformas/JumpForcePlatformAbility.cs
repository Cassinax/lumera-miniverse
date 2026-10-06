using UnityEngine;

namespace Lumera.JumpForce
{
    public enum JumpForcePlatformType { Padrao, Gelo, Invisivel, Nuvem, Instavel }

    // A habilidade pertence ao apoio; o motor e os interativos continuam independentes.
    [DefaultExecutionOrder(-250), DisallowMultipleComponent]
    public sealed class JumpForcePlatformAbility : MonoBehaviour
    {
        public JumpForcePlatformType tipo;
        [Header("Invisivel")]
        public Vector2 intervaloVisibilidade = new(4, 6);
        [Min(0)] public float avisoPiscando = 1;
        [Min(0.02f)] public float intervaloPiscada = 0.12f;
        [Header("Nuvem")]
        [Min(0)] public float esperaNuvem = 3;
        [Min(0.01f)] public float velocidadeDescida = 0.4f;
        [Min(0.01f)] public float velocidadeRetorno = 0.6f;
        [Min(0.1f)] public float descidaMaxima = 3;
        [Min(1)] public float forcaVertical = 2400;
        [Range(0.05f, 1)] public float escalaMinimaNuvem = 0.25f;
        public Transform visualNuvem;
        [Header("Instavel")]
        [Min(0.05f)] public float intervaloQueda = 3;
        public Rigidbody[] partes = System.Array.Empty<Rigidbody>();
        [Tooltip("Segundos entre a ultima peca se soltar e a plataforma se remontar.")]
        [Min(0)] public float tempoReconstrucao = 5;
        [Range(0, 0.5f)] public float margemDesativacao = 0.05f;
        [Header("Referencias opcionais para objetos colocados na cena")]
        public JumpForcePlayer jogador;
        public JumpForceCamera cameraJogo;

        JumpForceTrailNode estado;
        JumpForcePlatformMotion motor;
        Rigidbody corpo;
        JumpForceSpawnedElement elemento;
        Renderer[] visuais;
        Vector3 escalaNuvem;
        Vector3[] posicoes;
        Quaternion[] rotacoes;
        Transform[] pais;
        BoxCollider[] colisores;
        ConfigurableJoint[] juntas;
        int caidas;
        float tempoQueda, tempoNuvem, deslocamento, reconstrucaoEm;
        bool contando, visivel = true;
        float proximaTroca, tempoVisibilidade;
        System.Random sorteio;
        bool preparado;
        public JumpForceTrailNode Estado => estado;
        public bool Quebrada => tipo == JumpForcePlatformType.Instavel && caidas == (1 << partes.Length) - 1;

        public bool ParteMontada(JumpForcePlatform superficie) =>
            tipo == JumpForcePlatformType.Instavel && superficie && superficie.Body &&
            superficie.Body != corpo && superficie.transform.IsChildOf(transform);

        public Vector3 VelocidadeBase => corpo ? corpo.linearVelocity : Vector3.zero;

        void Awake() => Preparar();
        void Start()
        {
            if (!jogador) jogador = FindAnyObjectByType<JumpForcePlayer>();
            if (!cameraJogo) cameraJogo = FindAnyObjectByType<JumpForceCamera>();
            if (estado == null) Reiniciar(null, jogador, cameraJogo);
        }
        void Preparar()
        {
            if (preparado) return;
            preparado = true;
            motor = GetComponent<JumpForcePlatformMotion>();
            corpo = GetComponent<Rigidbody>();
            visuais = GetComponentsInChildren<Renderer>(true);
            if (visualNuvem) escalaNuvem = visualNuvem.localScale;
            posicoes = new Vector3[partes.Length];
            rotacoes = new Quaternion[partes.Length];
            pais = new Transform[partes.Length];
            colisores = new BoxCollider[partes.Length];
            juntas = new ConfigurableJoint[partes.Length];
            foreach (var superficie in GetComponentsInChildren<JumpForcePlatform>(true))
            {
                superficie.proprietario = this;
                superficie.escorregadia = tipo == JumpForcePlatformType.Gelo;
            }
            for (int i = 0; i < partes.Length; i++)
            {
                posicoes[i] = partes[i].transform.localPosition;
                rotacoes[i] = partes[i].transform.localRotation;
                pais[i] = partes[i].transform.parent;
                colisores[i] = partes[i].GetComponent<BoxCollider>();
                juntas[i] = partes[i].GetComponent<ConfigurableJoint>();
            }
        }
        public void Reiniciar(JumpForceTrailNode node, JumpForcePlayer player, JumpForceCamera camera)
        {
            Preparar();
            estado = node;
            elemento = GetComponent<JumpForceSpawnedElement>();
            jogador = player;
            cameraJogo = camera;
            sorteio = new System.Random(node != null ? node.motionSeed : GetEntityId().GetHashCode());
            caidas = node != null ? node.brokenPieces : 0;
            tempoQueda = node != null ? node.breakTimer : 0;
            contando = node != null && node.breakStarted;
            reconstrucaoEm = node != null ? node.reassembleAt : 0;
            deslocamento = node != null ? node.cloudOffset : 0;
            tempoNuvem = node != null ? node.cloudTimer : 0;
            visivel = true;
            tempoVisibilidade = 0;
            SortearIntervalo();
            for (int i = 0; i < partes.Length; i++)
            {
                var p = partes[i];
                p.isKinematic = false;
                p.useGravity = false;
                p.linearVelocity = Vector3.zero;
                p.angularVelocity = Vector3.zero;
                p.constraints = RigidbodyConstraints.FreezeRotation;
                p.transform.SetParent(pais[i], false);
                p.transform.localPosition = posicoes[i];
                p.transform.localRotation = rotacoes[i];
                p.position = p.transform.position;
                p.rotation = p.transform.rotation;
                var junta = juntas[i];
                if (junta)
                {
                    junta.connectedBody = corpo;
                    junta.autoConfigureConnectedAnchor = false;
                    junta.anchor = Vector3.zero;
                    junta.connectedAnchor = posicoes[i];
                    junta.xMotion = junta.yMotion = junta.zMotion = ConfigurableJointMotion.Locked;
                    junta.angularXMotion = junta.angularYMotion = junta.angularZMotion = ConfigurableJointMotion.Locked;
                }
                p.gameObject.SetActive((caidas & (1 << i)) == 0);
            }
            if (visualNuvem) visualNuvem.localScale = escalaNuvem;
            Mostrar(true);
        }
        void SortearIntervalo()
        {
            float minimo = Mathf.Max(avisoPiscando, Mathf.Min(intervaloVisibilidade.x, intervaloVisibilidade.y));
            float maximo = Mathf.Max(minimo, Mathf.Max(intervaloVisibilidade.x, intervaloVisibilidade.y));
            proximaTroca = Mathf.Lerp(minimo, maximo, (float)sorteio.NextDouble());
        }
        bool Apoiado => jogador && jogador.Grounded && jogador.Support && jogador.Support.proprietario == this;
        void Update()
        {
            if (tipo != JumpForcePlatformType.Invisivel || sorteio == null || (jogador && (!jogador.GameplayEnabled || jogador.Dead))) return;
            tempoVisibilidade += Time.deltaTime;
            if (tempoVisibilidade >= proximaTroca)
            {
                tempoVisibilidade = 0;
                visivel = !visivel;
                SortearIntervalo();
            }
            float restante = proximaTroca - tempoVisibilidade;
            bool mostrar = restante <= avisoPiscando
                ? (Mathf.FloorToInt(restante / Mathf.Max(0.02f, intervaloPiscada)) & 1) == 0
                : visivel;
            Mostrar(mostrar);
        }
        void Mostrar(bool mostrar)
        {
            // O culling da camera usa enabled; a habilidade usa forceRenderingOff, sem conflitos.
            foreach (var r in visuais) if (r) r.forceRenderingOff = !mostrar;
        }
        void FixedUpdate()
        {
            if (!preparado || sorteio == null || (jogador && (!jogador.GameplayEnabled || jogador.Dead))) return;
            float dt = Time.fixedDeltaTime;
            if (tipo == JumpForcePlatformType.Nuvem && motor)
            {
                bool apoiado = Apoiado;
                tempoNuvem = apoiado ? tempoNuvem + dt : 0;
                float alvo = apoiado && tempoNuvem >= esperaNuvem ? -descidaMaxima : apoiado ? deslocamento : 0;
                deslocamento = Mathf.MoveTowards(deslocamento, alvo, (apoiado ? velocidadeDescida : velocidadeRetorno) * dt);
                motor.ConfigurarAltitude(deslocamento, Mathf.Max(velocidadeDescida, velocidadeRetorno), forcaVertical);
                if (visualNuvem)
                {
                    float quedaReal = Mathf.Max(0, motor.Centro.y - corpo.position.y);
                    visualNuvem.localScale = escalaNuvem * Mathf.Lerp(1, escalaMinimaNuvem, Mathf.Clamp01(quedaReal / descidaMaxima));
                }
                if (estado != null) { estado.cloudOffset = deslocamento; estado.cloudTimer = tempoNuvem; }
            }
            if (tipo != JumpForcePlatformType.Instavel) return;
            if (Quebrada && reconstrucaoEm > 0 && Time.time >= reconstrucaoEm)
            {
                if (estado != null) estado.ReassembleIfDue(Time.time);
                Reiniciar(estado, jogador, cameraJogo);
                if (elemento) elemento.RestaurarConteudo();
                return;
            }
            contando |= Apoiado;
            if (contando && !Quebrada)
            {
                tempoQueda += dt;
                if (tempoQueda >= Mathf.Max(0.05f, intervaloQueda))
                {
                    tempoQueda -= Mathf.Max(0.05f, intervaloQueda);
                    SoltarParte();
                    if (Quebrada)
                    {
                        reconstrucaoEm = Time.time + Mathf.Max(0, tempoReconstrucao);
                        if (estado != null) estado.reassembleAt = reconstrucaoEm;
                    }
                }
            }
            for (int i = 0; i < partes.Length; i++)
            {
                var p = partes[i];
                if (!p.gameObject.activeSelf) continue;
                if ((caidas & (1 << i)) != 0 && ForaDaTela(colisores[i].bounds)) p.gameObject.SetActive(false);
            }
            if (estado != null) { estado.breakStarted = contando; estado.breakTimer = tempoQueda; estado.brokenPieces = caidas; }
            if (Quebrada)
            {
                if (estado != null) estado.destroyed = true;
                if (elemento) elemento.DesativarConteudo();
                // O corpo vazio mantem o temporizador. Fora da janela, o mapa guarda o prazo.
                // Pecas caidas continuam sendo desativadas individualmente.
            }
        }
        void SoltarParte()
        {
            int restantes = 0;
            for (int i = 0; i < partes.Length; i++) if ((caidas & (1 << i)) == 0) restantes++;
            int escolhido = sorteio.Next(restantes);
            for (int i = 0; i < partes.Length; i++)
            {
                if ((caidas & (1 << i)) != 0) continue;
                if (escolhido-- != 0) continue;
                caidas |= 1 << i;
                var p = partes[i];
                var junta = juntas[i];
                if (junta)
                {
                    junta.xMotion = junta.yMotion = junta.zMotion = ConfigurableJointMotion.Free;
                    junta.angularXMotion = junta.angularYMotion = junta.angularZMotion = ConfigurableJointMotion.Free;
                    junta.connectedBody = null;
                }
                p.transform.SetParent(transform.parent, true);
                p.isKinematic = false;
                p.useGravity = true;
                p.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
                p.linearVelocity = Vector3.zero;
                p.WakeUp();
                break;
            }
        }
        bool ForaDaTela(Bounds bounds)
        {
            var camera = cameraJogo ? cameraJogo.Visao : null;
            if (!camera) return false;
            Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var sinal = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                var ponto = camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, sinal));
                min = Vector3.Min(min, ponto); max = Vector3.Max(max, ponto);
            }
            return max.z < camera.nearClipPlane || max.y < -margemDesativacao || min.y > 1 + margemDesativacao ||
                max.x < -margemDesativacao || min.x > 1 + margemDesativacao;
        }
        public Bounds IncluirPartes(Bounds bounds)
        {
            if (tipo == JumpForcePlatformType.Instavel)
                for (int i = 0; i < partes.Length; i++)
                    if (partes[i] && partes[i].gameObject.activeInHierarchy && colisores[i]) bounds.Encapsulate(colisores[i].bounds);
            return bounds;
        }
        public void ResetarParaPool()
        {
            // Executado depois de SetActive(false); a Unity proibe reparentear dentro de OnDisable.
            for (int i = 0; i < partes.Length; i++)
            {
                if (!partes[i]) continue;
                partes[i].transform.SetParent(pais[i], false);
                partes[i].transform.localPosition = posicoes[i];
                partes[i].transform.localRotation = rotacoes[i];
            }
            // O registro anterior ja recebeu o estado durante os passos fisicos.
            estado = null;
            jogador = null;
            cameraJogo = null;
            sorteio = null;
            contando = false;
            caidas = 0;
            tempoQueda = tempoNuvem = deslocamento = tempoVisibilidade = reconstrucaoEm = 0;
            visivel = true;
            if (visualNuvem) visualNuvem.localScale = escalaNuvem;
            Mostrar(true);
        }
        void OnDisable()
        {
            if (!preparado) return;
            Mostrar(true);
            for (int i = 0; i < partes.Length; i++)
            {
                if (!partes[i]) continue;
                if (!partes[i].isKinematic)
                {
                    partes[i].linearVelocity = Vector3.zero;
                    partes[i].angularVelocity = Vector3.zero;
                }
                partes[i].isKinematic = true;
                partes[i].useGravity = false;
                if (partes[i].transform.parent != pais[i]) partes[i].gameObject.SetActive(false);
            }
        }
        void OnDestroy()
        {
            // Pecas soltas estao fora da hierarquia; nao devem sobreviver ao dono.
            foreach (var parte in partes)
                if (parte && !parte.transform.IsChildOf(transform)) Destroy(parte.gameObject);
        }
    }
}
