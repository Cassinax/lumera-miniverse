namespace Cassinax.EngineLink
{
    /// <summary>
    /// Informacoes publicas do pacote Engine Link.
    /// O pacote atua no Editor; em builds do jogo nada aqui se conecta a aparelhos.
    /// </summary>
    public static class InfoEngineLink
    {
        /// <summary>Versao do pacote.</summary>
        public const string Versao = "0.1.0";

        /// <summary>Versao do protocolo falado com o app (maior.menor).</summary>
        public const int ProtocoloMaior = 1;
        public const int ProtocoloMenor = 0;
        public const string Protocolo = "1.0";

        /// <summary>Porta TCP padrao do app no aparelho.</summary>
        public const int PortaPadrao = 47100;

        /// <summary>Porta UDP usada para encontrar aparelhos na rede.</summary>
        public const int PortaDescoberta = 47101;

        /// <summary>Verdadeiro somente no Editor, onde o Engine Link funciona.</summary>
        public static bool Disponivel
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
    }
}
