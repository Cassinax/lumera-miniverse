using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Controladores_Cena/Audio_Cena: sons do Jump Force. So guarda a lista e pede a reproducao ao
    // ControladorAudio do Objeto Mestre (musica com fade, um efeito por vez). Clipes vazios sao ignorados.
    [DisallowMultipleComponent]
    public sealed class JumpForceAudio : MonoBehaviour
    {
        [Serializable]
        public struct Som
        {
            public AudioClip clip;
            [Range(0, 1)] public float volume;
        }

        [Header("Musica")]
        [SerializeField] AudioClip musica;
        [Tooltip("Fade de entrada e saida da musica desta cena, em segundos.")]
        [SerializeField, Min(0)] float fadeMusica = 1.5f;

        [Header("Efeitos")]
        [SerializeField] Som pulo = Padrao();
        [SerializeField] Som pouso = Padrao();
        [SerializeField] Som trampolim = Padrao();
        [SerializeField] Som vento = Padrao();
        [SerializeField] Som moeda = Padrao();
        [SerializeField] Som morte = Padrao();
        [SerializeField] Som recorde = Padrao();
        [Tooltip("Impacto minimo, em m/s, para tocar o som de pouso (evita som ao nascer ou em degraus).")]
        [SerializeField, Min(0)] float impactoMinimoPouso = 2;

        static Som Padrao() => new Som { volume = 1 };
        static ControladorAudio Audio => ObjetoMestre.Instancia ? ObjetoMestre.Instancia.Audio : null;

        void OnEnable()
        {
            JumpForceEventos.Pulo += AoPular;
            JumpForceEventos.Pouso += AoPousar;
            JumpForceEventos.Trampolim += AoTrampolim;
            JumpForceEventos.Vento += AoVento;
            JumpForceEventos.Moeda += AoMoeda;
            JumpForceEventos.Morte += AoMorrer;
        }

        void OnDisable()
        {
            JumpForceEventos.Pulo -= AoPular;
            JumpForceEventos.Pouso -= AoPousar;
            JumpForceEventos.Trampolim -= AoTrampolim;
            JumpForceEventos.Vento -= AoVento;
            JumpForceEventos.Moeda -= AoMoeda;
            JumpForceEventos.Morte -= AoMorrer;
        }

        void Start()
        {
            if (Audio && musica) Audio.TocarMusica(musica, fadeMusica);
        }

        // A musica desta cena nao segue para o Menu.
        void OnDestroy()
        {
            if (Audio && musica && Audio.MusicaAtual == musica) Audio.PararMusica(fadeMusica);
        }

        public void TocarRecorde() => Tocar(recorde);

        void AoPular() => Tocar(pulo);
        void AoPousar(float impacto) { if (impacto >= impactoMinimoPouso) Tocar(pouso); }
        void AoTrampolim() => Tocar(trampolim);
        void AoVento() => Tocar(vento);
        void AoMoeda() => Tocar(moeda);
        void AoMorrer() => Tocar(morte);

        static void Tocar(Som som)
        {
            if (Audio && som.clip) Audio.TocarEfeito(som.clip, som.volume);
        }
    }
}
