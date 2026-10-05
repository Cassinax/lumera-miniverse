using UnityEngine;

namespace Lumera.JumpForce
{
    // Motor de ida e volta por forca, compartilhado por plataformas e pilares moveis (Rigidbody dinamico).
    // Perfil de velocidade: cruzeiro ate perto do extremo e freio curto, que chega ao ponto final parado;
    // la o sentido inverte. A forca pedida e a que corrige a velocidade em um passo, limitada pela forca do
    // motor (acelerando) ou do freio (desacelerando): sem tranco de liga/desliga, e a massa da plataforma e o
    // peso de quem esta em cima participam da fisica.
    public static class JumpForceMotorFisico
    {
        // Velocidade minima de chegada, para o freio terminar em tempo finito.
        const float VelocidadeChegada = 0.1f;
        // O freio planeja com parte da forca, para sobrar margem com carga (o jogador em cima).
        const float MargemFreio = 0.6f;

        // sentido: +1 ou -1 ao longo do eixo; inverte ao chegar a cada extremo.
        public static void Aplicar(Rigidbody body, Vector3 centro, Vector3 eixo, float amplitude, float velocidadeMaxima,
            float forcaMotor, float forcaFreio, float tolerancia, ref float sentido)
        {
            if (!body || body.isKinematic) return;
            float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            float velocidadeNoEixo = Vector3.Dot(body.linearVelocity, eixo);
            if (amplitude <= 0.0001f)
            {
                body.AddForce(-eixo * velocidadeNoEixo, ForceMode.VelocityChange);
                return;
            }

            float restante = Vector3.Dot(centro + eixo * (amplitude * sentido) - body.position, eixo) * sentido;
            if (restante <= tolerancia)
            {
                sentido = -sentido;
                restante = Vector3.Dot(centro + eixo * (amplitude * sentido) - body.position, eixo) * sentido;
            }

            float desaceleracao = Mathf.Max(0.01f, forcaFreio / body.mass * MargemFreio);
            float alvo = Mathf.Min(Mathf.Max(0.1f, velocidadeMaxima),
                Mathf.Max(VelocidadeChegada, Mathf.Sqrt(2 * desaceleracao * Mathf.Max(0, restante)))) * sentido;
            float erro = alvo - velocidadeNoEixo;
            // Reduzir a velocidade no sentido em que ja se move e frenagem; o resto e o motor.
            bool freando = velocidadeNoEixo * erro < 0;
            float limite = freando ? forcaFreio : forcaMotor;
            float forca = Mathf.Clamp(erro * body.mass / dt, -limite, limite);
            body.AddForce(eixo * forca, ForceMode.Force);
        }

        // Trava a rotacao, o Z e o eixo do plano que nao faz parte do movimento. Parado: trava tudo.
        public static RigidbodyConstraints Travas(Vector3 eixo, float amplitude)
        {
            var travas = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            if (amplitude <= 0.0001f) return travas | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY;
            return travas | (Mathf.Abs(eixo.x) >= Mathf.Abs(eixo.y) ? RigidbodyConstraints.FreezePositionY : RigidbodyConstraints.FreezePositionX);
        }
    }
}
