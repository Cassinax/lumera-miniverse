using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Acontecimentos da partida. Jogador e elementos avisam; os controladores da cena (audio, feedback,
    // plataforma) reagem sem depender uns dos outros.
    public static class JumpForceEventos
    {
        public static event Action Pulo;
        // Velocidade vertical no impacto, em m/s (positiva).
        public static event Action<float> Pouso;
        public static event Action Trampolim;
        public static event Action Vento;
        public static event Action Moeda;
        public static event Action Morte;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Limpar()
        {
            Pulo = null; Pouso = null; Trampolim = null; Vento = null; Moeda = null; Morte = null;
        }

        public static void AvisarPulo() => Pulo?.Invoke();
        public static void AvisarPouso(float impacto) => Pouso?.Invoke(impacto);
        public static void AvisarTrampolim() => Trampolim?.Invoke();
        public static void AvisarVento() => Vento?.Invoke();
        public static void AvisarMoeda() => Moeda?.Invoke();
        public static void AvisarMorte() => Morte?.Invoke();
    }
}
