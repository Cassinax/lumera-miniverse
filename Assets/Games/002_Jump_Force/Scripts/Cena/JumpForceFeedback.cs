using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Controladores_Cena/Feedback: tremor leve da camera e vibracao do aparelho (pela opcao de vibracao da
    // plataforma) no pouso forte e no trampolim.
    [DisallowMultipleComponent]
    public sealed class JumpForceFeedback : MonoBehaviour
    {
        [Serializable]
        public struct Resposta
        {
            [Tooltip("Deslocamento maximo da camera, em metros.")]
            [Min(0)] public float tremor;
            [Tooltip("Duracao do tremor, em segundos.")]
            [Min(0)] public float duracaoTremor;
            [Tooltip("Duracao da vibracao, em milissegundos. 0 = sem vibracao.")]
            [Min(0)] public int vibracaoMs;
            [Tooltip("Intensidade da vibracao, de 1 a 255.")]
            [Range(1, 255)] public int intensidadeVibracao;
        }

        [SerializeField] JumpForceCamera cameraJogo;
        [Tooltip("Velocidade de queda, em m/s, a partir da qual o pouso e forte.")]
        [SerializeField, Min(0)] float impactoForte = 12;
        [SerializeField] Resposta pousoForte = new Resposta { tremor = 0.08f, duracaoTremor = 0.18f, vibracaoMs = 30, intensidadeVibracao = 90 };
        [SerializeField] Resposta trampolim = new Resposta { tremor = 0.15f, duracaoTremor = 0.3f, vibracaoMs = 60, intensidadeVibracao = 160 };

        void OnEnable()
        {
            JumpForceEventos.Pouso += AoPousar;
            JumpForceEventos.Trampolim += AoTrampolim;
        }

        void OnDisable()
        {
            JumpForceEventos.Pouso -= AoPousar;
            JumpForceEventos.Trampolim -= AoTrampolim;
        }

        void AoPousar(float impacto)
        {
            if (impacto >= impactoForte) Responder(pousoForte);
        }

        void AoTrampolim() => Responder(trampolim);

        void Responder(Resposta resposta)
        {
            if (cameraJogo) cameraJogo.Tremer(resposta.tremor, resposta.duracaoTremor);
            var vibracao = ObjetoMestre.Instancia ? ObjetoMestre.Instancia.Vibracao : null;
            if (vibracao && resposta.vibracaoMs > 0) vibracao.Vibrar(resposta.vibracaoMs, resposta.intensidadeVibracao);
        }
    }
}
