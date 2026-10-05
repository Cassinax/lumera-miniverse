using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Cassinax.EngineLink.Editor")]

namespace Cassinax.EngineLink
{
    /// <summary>Toque recebido do aparelho, no formato de UnityEngine.Touch.</summary>
    public struct EngineLinkTouch
    {
        public int fingerId;
        /// <summary>Pixels do Game view, origem no canto inferior esquerdo (igual a Input.touches).</summary>
        public Vector2 position;
        public Vector2 deltaPosition;
        public TouchPhase phase;
        public float pressure;
    }

    /// <summary>
    /// Entrada do aparelho conectado pelo Engine Link, para projetos que usam o Input Manager
    /// antigo (que nao aceita toques injetados). Projetos com Input System recebem os toques
    /// como uma Touchscreen normal e nao precisam desta classe.
    ///
    /// Sem Engine Link ativo (builds, ou sem conexao) as propriedades devolvem o UnityEngine.Input
    /// quando o Input Manager antigo estiver habilitado, entao o mesmo codigo serve nos dois casos.
    /// </summary>
    public static class EngineLinkInput
    {
        static readonly object Trava = new object();
        static readonly Queue<EngineLinkTouch> Pendentes = new Queue<EngineLinkTouch>();
        static readonly List<EngineLinkTouch> Atuais = new List<EngineLinkTouch>();
        static readonly Dictionary<int, Vector2> UltimaPosicao = new Dictionary<int, Vector2>();
        static Vector3 aceleracao;

        /// <summary>Verdadeiro enquanto um aparelho estiver conectado pelo Engine Link (somente no Editor).</summary>
        public static bool Active { get; internal set; }

        public static int touchCount
        {
            get
            {
                if (Active) return Atuais.Count;
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.touchCount;
#else
                return 0;
#endif
            }
        }

        public static EngineLinkTouch GetTouch(int index)
        {
            if (Active) return Atuais[index];
#if ENABLE_LEGACY_INPUT_MANAGER
            var toque = Input.GetTouch(index);
            return new EngineLinkTouch
            {
                fingerId = toque.fingerId, position = toque.position, deltaPosition = toque.deltaPosition,
                phase = toque.phase, pressure = toque.pressure,
            };
#else
            throw new System.IndexOutOfRangeException("No Engine Link touches.");
#endif
        }

        /// <summary>Aceleracao em g, mesma convencao de Input.acceleration (aparelho deitado: z = -1).</summary>
        public static Vector3 acceleration
        {
            get
            {
                if (Active) return aceleracao;
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.acceleration;
#else
                return Vector3.zero;
#endif
            }
        }

        // ---- Usado pela parte de Editor ----

        /// <summary>Enfileira um toque (posicao em pixels do Game view). Pode ser chamado de qualquer thread.</summary>
        internal static void EnfileirarToque(int id, Vector2 posicao, TouchPhase fase, float pressao)
        {
            lock (Trava) Pendentes.Enqueue(new EngineLinkTouch { fingerId = id, position = posicao, phase = fase, pressure = pressao });
        }

        internal static void DefinirAceleracao(Vector3 valor)
        {
            aceleracao = valor;
        }

        /// <summary>
        /// Chamado uma vez por quadro, antes dos scripts do jogo: aplica no maximo uma mudanca
        /// de fase por dedo, para que Began e Ended durem exatamente um quadro (como no aparelho).
        /// </summary>
        internal static void AvancarQuadro()
        {
            // Toques que terminaram no quadro anterior saem; os demais viram Stationary se nao mudarem.
            for (var i = Atuais.Count - 1; i >= 0; i--)
            {
                var toque = Atuais[i];
                if (toque.phase == TouchPhase.Ended || toque.phase == TouchPhase.Canceled)
                {
                    UltimaPosicao.Remove(toque.fingerId);
                    Atuais.RemoveAt(i);
                    continue;
                }
                toque.phase = TouchPhase.Stationary;
                toque.deltaPosition = Vector2.zero;
                Atuais[i] = toque;
            }

            var mudaramNesteQuadro = new HashSet<int>();
            lock (Trava)
            {
                var adiados = new List<EngineLinkTouch>();
                while (Pendentes.Count > 0)
                {
                    var evento = Pendentes.Dequeue();
                    if (mudaramNesteQuadro.Contains(evento.fingerId) && evento.phase != TouchPhase.Moved)
                    {
                        adiados.Add(evento); // inicio/fim no mesmo quadro: fim fica para o proximo
                        continue;
                    }
                    Aplicar(evento);
                    mudaramNesteQuadro.Add(evento.fingerId);
                }
                foreach (var evento in adiados) Pendentes.Enqueue(evento);
            }
        }

        static void Aplicar(EngineLinkTouch evento)
        {
            Vector2 anterior;
            evento.deltaPosition = UltimaPosicao.TryGetValue(evento.fingerId, out anterior) ? evento.position - anterior : Vector2.zero;
            UltimaPosicao[evento.fingerId] = evento.position;
            var indice = Atuais.FindIndex(t => t.fingerId == evento.fingerId);
            if (indice < 0)
            {
                if (evento.phase == TouchPhase.Moved || evento.phase == TouchPhase.Stationary) evento.phase = TouchPhase.Began;
                Atuais.Add(evento);
            }
            else
            {
                // Um Moved seguido de outro Moved no mesmo quadro acumula o deslocamento.
                if (evento.phase == TouchPhase.Moved && Atuais[indice].phase == TouchPhase.Moved)
                {
                    evento.deltaPosition += Atuais[indice].deltaPosition;
                }
                if (Atuais[indice].phase == TouchPhase.Began && evento.phase == TouchPhase.Moved) evento.phase = TouchPhase.Began;
                Atuais[indice] = evento;
            }
        }

        /// <summary>Limpa tudo ao desconectar ou sair do Play Mode.</summary>
        internal static void Limpar()
        {
            lock (Trava) Pendentes.Clear();
            Atuais.Clear();
            UltimaPosicao.Clear();
            aceleracao = Vector3.zero;
        }
    }
}
