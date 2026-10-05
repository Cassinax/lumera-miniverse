// Cassinax Unity System Save - v1.0.0
// Chaves por jogo, para um app que hospeda varios jogos no mesmo save.
using System;
using System.Collections.Generic;
using System.Linq;

namespace cassinax.savesystem
{
    public partial class SaveSystem
    {
        /// <summary>Prefixo do comando usado pelas chaves de cada jogo hospedado.</summary>
        public const string GamePrefix = "JOGO_";

        /// <summary>
        /// Comando normalizado de um jogo hospedado. O id vem do catalogo do app e e
        /// fixo depois de publicado, porque ele entra na chave gravada no save.
        /// </summary>
        public static string GameCommand(string gameId) =>
            GamePrefix + (gameId ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>
        /// Chave de um dado pertencente a um jogo hospedado: <c>[JOGO_&lt;ID&gt;],[campo]</c>.
        ///
        /// Serve a apps que reunem varios jogos sob uma carteira comum: a carteira fica em
        /// <c>[SLG0002],</c> e cada jogo grava sob o proprio comando. Assim da para saber
        /// quais jogos tem dados e apagar os de um sem tocar nos outros.
        /// </summary>
        public static string GameKey(string gameId, string field) =>
            "[" + GameCommand(gameId) + "],[" + field + "]";

        /// <summary>Verdadeiro quando a chave pertence a algum jogo hospedado.</summary>
        public static bool IsGameKey(string key) =>
            key != null && key.StartsWith("[" + GamePrefix, StringComparison.Ordinal);

        /// <summary>
        /// Id do jogo dono da chave, em maiusculas, ou string vazia quando a chave nao e de jogo.
        /// </summary>
        public static string GameIdOf(string key)
        {
            if (!IsGameKey(key)) return string.Empty;
            int fim = key.IndexOf("],", StringComparison.Ordinal);
            if (fim <= 0) return string.Empty;
            return key.Substring(GamePrefix.Length + 1, fim - GamePrefix.Length - 1);
        }

        /// <summary>
        /// Escopo pronto para apps multi-jogo: trata <c>[JOGO_*],</c> como progresso
        /// sincronizavel, mantendo o resto do comportamento de <see cref="DefaultScope"/>.
        ///
        /// Passe em <see cref="Configure"/> quando o progresso de cada jogo hospedado
        /// precisar viajar para a nuvem. Sem isso, essas chaves caem em
        /// <see cref="SaveScope.LocalPreference"/> pela regra de chave desconhecida e
        /// ficam somente no aparelho.
        /// </summary>
        public static SaveScope MultiGameScope(string key) =>
            IsGameKey(key) ? SaveScope.Progress : DefaultScope(key);

        /// <summary>Chaves confirmadas que pertencem ao jogo informado.</summary>
        public List<string> KeysOfGame(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return new List<string>();
            string prefixo = "[" + GameCommand(gameId) + "],";
            return _core.GetAllKeys().Where(k => k.StartsWith(prefixo, StringComparison.Ordinal)).ToList();
        }

        /// <summary>Verdadeiro quando existe algum dado confirmado do jogo informado.</summary>
        public bool HasGameData(string gameId) => KeysOfGame(gameId).Count > 0;

        /// <summary>
        /// Ids dos jogos hospedados que possuem dados confirmados, em ordem alfabetica.
        /// Use para montar a lista de saves do app.
        /// </summary>
        public List<string> GamesWithData() =>
            _core.GetAllKeys().Where(IsGameKey).Select(GameIdOf).Where(id => id.Length > 0)
                 .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Enfileira a remocao de todos os dados de um jogo hospedado e devolve quantas
        /// chaves foram enfileiradas. A carteira comum e as preferencias do aparelho nao
        /// sao tocadas. Processe a fila para efetivar.
        /// </summary>
        public int EnqueueRemoveGameData(string gameId, PriorityLevel priority = PriorityLevel.Alto)
        {
            var chaves = KeysOfGame(gameId);
            foreach (string chave in chaves) EnqueueRemove(chave, priority);
            return chaves.Count;
        }
    }
}
