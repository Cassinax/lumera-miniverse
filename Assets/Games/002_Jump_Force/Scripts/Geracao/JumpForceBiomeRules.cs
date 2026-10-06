using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    [Serializable]
    public sealed class JumpForceRouteSection
    {
        public string nome;
        public JumpForcePlatformType[] plataformas = Array.Empty<JumpForcePlatformType>();
    }

    // Somente valores: o mapa nao depende dos materiais, prefabs ou objetos da cena.
    [Serializable]
    public sealed class JumpForceBiomeRules
    {
        public string nome;
        [Min(0)] public int primeiroNivel;
        public JumpForcePlatformType plataformaNova;
        [Min(0)] public int niveisIntroducao = 6;
        [Min(1)] public int intervaloApresentacao = 2;
        [Min(3)] public int niveisPorConjunto = 10;
        [Min(1)] public int niveisDescanso = 2;
        public JumpForceRouteSection[] conjuntos = Array.Empty<JumpForceRouteSection>();

        public static JumpForceBiomeRules[] Padrao() => new[]
        {
            new JumpForceBiomeRules { nome = "Campina", primeiroNivel = 0,
                plataformaNova = JumpForcePlatformType.Padrao,
                conjuntos = new[] { Secao("Aprender o salto", "PPPPPPPPPP") } },
            new JumpForceBiomeRules { nome = "Ceus", primeiroNivel = 30,
                plataformaNova = JumpForcePlatformType.Nuvem,
                conjuntos = new[] { Secao("Nuvens em sequencia", "PPNPPNNPPP"),
                    Secao("Nuvens intercaladas", "PNPNPPPNPP"), Secao("Travessia de nuvens", "PPNNPPPNPP") } }
        };

        static JumpForceRouteSection Secao(string nome, string sequencia)
        {
            var tipos = new JumpForcePlatformType[sequencia.Length];
            for (int i = 0; i < tipos.Length; i++)
                tipos[i] = sequencia[i] == 'N' ? JumpForcePlatformType.Nuvem : JumpForcePlatformType.Padrao;
            return new JumpForceRouteSection { nome = nome, plataformas = tipos };
        }

        public static int Indice(JumpForceBiomeRules[] biomas, int nivel)
        {
            int resultado = -1, inicio = -1;
            if (biomas == null) return resultado;
            for (int i = 0; i < biomas.Length; i++)
                if (biomas[i] != null && biomas[i].primeiroNivel <= nivel && biomas[i].primeiroNivel > inicio)
                { resultado = i; inicio = biomas[i].primeiroNivel; }
            return resultado;
        }

        public static JumpForceBiomePlan Planejar(JumpForceBiomeRules[] biomas, int semente, int nivel)
        {
            int indice = Indice(biomas, nivel);
            if (indice < 0) return new JumpForceBiomePlan(-1, -1, 0, JumpForcePlatformType.Padrao, false, false);
            var bioma = biomas[indice];
            int local = Mathf.Max(0, nivel - bioma.primeiroNivel);
            if (local < bioma.niveisIntroducao)
            {
                bool apresentar = local > 0 && local % Mathf.Max(1, bioma.intervaloApresentacao) == 0;
                return new JumpForceBiomePlan(indice, -1, local,
                    apresentar ? bioma.plataformaNova : JumpForcePlatformType.Padrao, true, false);
            }
            int tamanho = Mathf.Max(3, bioma.niveisPorConjunto);
            int bloco = (local - bioma.niveisIntroducao) / tamanho;
            int passo = (local - bioma.niveisIntroducao) % tamanho;
            // O bloco conserva o plano durante consultas futuras, reparos e reciclagem.
            uint hash = unchecked((uint)semente ^ (uint)indice * 2246822519u ^ (uint)bloco * 3266489917u);
            hash ^= hash >> 16;
            int conjunto = bioma.conjuntos == null || bioma.conjuntos.Length == 0 ? -1 : (int)(hash % bioma.conjuntos.Length);
            var tipos = conjunto >= 0 ? bioma.conjuntos[conjunto]?.plataformas : null;
            bool descanso = passo >= tamanho - Mathf.Clamp(bioma.niveisDescanso, 1, tamanho);
            var tipo = !descanso && tipos != null && passo < tipos.Length ? tipos[passo] : JumpForcePlatformType.Padrao;
            bool liberada = tipo == JumpForcePlatformType.Padrao;
            foreach (var anterior in biomas)
                liberada |= anterior != null && anterior.primeiroNivel <= nivel && anterior.plataformaNova == tipo;
            return new JumpForceBiomePlan(indice, conjunto, passo,
                liberada ? tipo : JumpForcePlatformType.Padrao, false, descanso);
        }
    }

    public readonly struct JumpForceBiomePlan
    {
        public readonly int bioma, conjunto, passo;
        public readonly JumpForcePlatformType plataforma;
        public readonly bool introducao, descanso;
        public bool ApoioSeguro => introducao || descanso;
        public bool PermiteInterativo => !ApoioSeguro && plataforma == JumpForcePlatformType.Padrao;
        public JumpForceBiomePlan(int bioma, int conjunto, int passo, JumpForcePlatformType plataforma, bool introducao, bool descanso)
        { this.bioma = bioma; this.conjunto = conjunto; this.passo = passo; this.plataforma = plataforma;
            this.introducao = introducao; this.descanso = descanso; }
    }
}
