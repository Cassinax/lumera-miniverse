// Cassinax Unity System Save - v1.0.0
//
// EXEMPLO. Transporte de progresso para uma nuvem de plataforma que guarda bytes opacos,
// como o Saved Games do Google Play. Leia, adapte e mantenha no seu projeto.
//
// Por que um envelope proprio em vez do payload do pacote: uma nuvem de plataforma
// costuma ter limite de tamanho por snapshot e resolver conflito no momento da abertura,
// devolvendo duas copias para o cliente escolher. Para decidir sem baixar e descriptografar
// tudo, o envelope carrega do lado de fora apenas o que a decisao precisa: conta, revisao,
// revisao anterior e epoca. O conteudo continua criptografado e assinado.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace cassinax.savesystem.samples
{
    /// <summary>Uma entrada de progresso dentro do snapshot.</summary>
    [Serializable]
    public sealed class PlayGamesEntry
    {
        public string chave;
        public string valor;
    }

    /// <summary>
    /// Snapshot que viaja para a nuvem da plataforma.
    ///
    /// <c>revisao</c> identifica esta versao; <c>anterior</c> aponta a revisao de onde ela
    /// saiu, permitindo resolver por linhagem; <c>epoca</c> sobe a cada reset confirmado,
    /// para que um aparelho offline nao ressuscite progresso apagado.
    /// </summary>
    [Serializable]
    public sealed class PlayGamesSnapshot
    {
        public int formato = 1;
        public string jogo;
        public int estrutura;
        public string conta;
        public string revisao;
        public string anterior;
        public string epoca;
        public bool apagado;
        public List<PlayGamesEntry> dados = new List<PlayGamesEntry>();
    }

    /// <summary>Casca gravada na nuvem: metadados claros e conteudo criptografado.</summary>
    [Serializable]
    public sealed class PlayGamesEnvelope
    {
        public int formato = 1;
        public int core;
        public string criptografado;
        public string assinatura;
    }

    /// <summary>O que fazer depois de comparar local e remoto.</summary>
    public enum PlayGamesDecision
    {
        /// <summary>Mesmo conteudo dos dois lados. Nada a fazer.</summary>
        Iguais,
        /// <summary>O local descende do remoto: enviar.</summary>
        EnviarLocal,
        /// <summary>O remoto descende do local: receber.</summary>
        ReceberNuvem,
        /// <summary>Ramos divergentes. Somente o jogador decide.</summary>
        Perguntar
    }

    /// <summary>
    /// Regras de integridade e precedencia do transporte.
    ///
    /// Duas regras que a experiencia impoe e que este exemplo respeita:
    ///
    /// 1. **Nao somar economia.** Juntar moedas ou itens de duas copias divergentes
    ///    duplica valor. Divergencia real vai para o jogador.
    /// 2. **Nao decidir por relogio nem por maior saldo.** Relogio de aparelho nao e
    ///    autoridade e "maior saldo" premia quem burlar. A precedencia vem de linhagem de
    ///    revisao e de epoca.
    /// </summary>
    public static class PlayGamesRules
    {
        /// <summary>SHA-256 em hexadecimal minusculo.</summary>
        public static string Hash(string texto)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(texto ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Impressao canonica do conjunto de entradas.
        ///
        /// Cada campo entra precedido do proprio comprimento. Sem isso, um valor que
        /// contenha o separador produziria a mesma impressao de um par diferente de
        /// chave e valor, e duas copias distintas passariam por iguais.
        /// </summary>
        public static string Fingerprint(IEnumerable<PlayGamesEntry> entradas)
        {
            if (entradas == null) return Hash(string.Empty);
            var sb = new StringBuilder();
            foreach (var e in entradas.Where(e => e != null)
                                      .OrderBy(e => e.chave, StringComparer.Ordinal))
            {
                string chave = e.chave ?? string.Empty;
                string valor = e.valor ?? string.Empty;
                sb.Append(chave.Length).Append(':').Append(chave);
                sb.Append(valor.Length).Append(':').Append(valor);
            }
            return Hash(sb.ToString());
        }

        /// <summary>
        /// Valida um snapshot recebido antes de qualquer uso. Rejeita formato estranho,
        /// outro jogo, outro schema, outra conta, identificadores malformados, chave
        /// duplicada e tamanho fora do limite.
        /// </summary>
        public static bool Valid(PlayGamesSnapshot s, string gameId, int schema, string account,
            Func<string, bool> isProgressKey, int maxEntries = 50000)
        {
            if (s == null || s.formato != 1 || s.jogo != gameId || s.estrutura != schema) return false;
            if (string.IsNullOrEmpty(account) || s.conta != account) return false;
            if (!Guid.TryParseExact(s.revisao, "N", out _)) return false;
            if (!Guid.TryParseExact(s.epoca, "N", out _)) return false;
            if (!string.IsNullOrEmpty(s.anterior) && !Guid.TryParseExact(s.anterior, "N", out _)) return false;
            if (s.dados == null || s.dados.Count > maxEntries) return false;
            if (s.apagado) return s.dados.Count == 0;

            var vistas = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in s.dados)
            {
                if (e == null || e.valor == null) return false;
                if (isProgressKey != null && !isProgressKey(e.chave)) return false;
                if (!vistas.Add(e.chave)) return false;
            }
            return true;
        }

        /// <summary>
        /// Decide o que fazer comparando o estado local com o snapshot remoto.
        ///
        /// <paramref name="localOwner"/> e a conta dona do save local;
        /// <paramref name="baseRevision"/> e <paramref name="baseFingerprint"/> descrevem a
        /// ultima sincronizacao confirmada, e e o que permite saber qual lado avancou.
        /// </summary>
        public static PlayGamesDecision Decide(string localOwner, string account, string localEpoch,
            string baseRevision, string baseFingerprint, string localFingerprint, PlayGamesSnapshot remote)
        {
            if (remote == null) return PlayGamesDecision.Perguntar;

            // Conta diferente nunca recebe upload do progresso da conta anterior.
            if (localOwner != account) return PlayGamesDecision.Perguntar;

            // Epoca nova significa reset confirmado: o local precisa acompanhar.
            if (localEpoch != remote.epoca) return PlayGamesDecision.ReceberNuvem;

            if (remote.apagado)
            {
                if (baseRevision != remote.revisao) return PlayGamesDecision.ReceberNuvem;
                return localFingerprint == baseFingerprint
                    ? PlayGamesDecision.Iguais
                    : PlayGamesDecision.EnviarLocal;
            }

            if (localFingerprint == Fingerprint(remote.dados)) return PlayGamesDecision.Iguais;

            // So o local mudou desde a ultima sincronizacao.
            if (!string.IsNullOrEmpty(baseRevision) && baseRevision == remote.revisao)
                return PlayGamesDecision.EnviarLocal;

            // So o remoto mudou desde a ultima sincronizacao.
            if (!string.IsNullOrEmpty(baseFingerprint) && localFingerprint == baseFingerprint)
                return PlayGamesDecision.ReceberNuvem;

            // Os dois lados avancaram. Nao existe escolha automatica honesta aqui.
            return PlayGamesDecision.Perguntar;
        }
    }
}
