using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    [Serializable]
    class RespostaDescoberta
    {
        public string servico;
        public string protocolo;
        public string nome;
        public int porta;
    }

    /// <summary>
    /// Wi-Fi automatico: envia CASSINAX_LINK_PROCURA em broadcast UDP e coleta as respostas dos apps.
    /// Bloqueia pelo tempo de escuta: use fora da thread principal.
    /// </summary>
    public static class Descoberta
    {
        public const string Procura = "CASSINAX_LINK_PROCURA";

        public static List<AparelhoEncontrado> Procurar(int tempoMs = 0)
        {
            if (tempoMs <= 0) tempoMs = ConfiguracoesEngineLink.Atual.procuraWifiMs;
            var encontrados = new Dictionary<string, AparelhoEncontrado>();
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                udp.EnableBroadcast = true;
                var pergunta = Encoding.UTF8.GetBytes(Procura);
                var redes = RedesLocais();
                var destinos = Destinos(redes);
                // Muitos roteadores e aparelhos (ex.: Samsung) descartam broadcast no Wi-Fi.
                // Por isso tambem perguntamos direto a cada endereco das redes /24 ou menores.
                var varredura = Varredura(redes);
                var limite = DateTime.UtcNow.AddMilliseconds(tempoMs);
                var reenvio = DateTime.MinValue;
                var rodada = 0;
                while (DateTime.UtcNow < limite)
                {
                    // Reenvia algumas vezes: UDP pode se perder em Wi-Fi.
                    if (DateTime.UtcNow >= reenvio && rodada < 3)
                    {
                        Enviar(udp, pergunta, destinos);
                        Enviar(udp, pergunta, varredura);
                        reenvio = DateTime.UtcNow.AddMilliseconds(500);
                        rodada++;
                    }
                    var restante = (int)Math.Max(1, Math.Min(250, (limite - DateTime.UtcNow).TotalMilliseconds));
                    udp.Client.ReceiveTimeout = restante;
                    try
                    {
                        var origem = new IPEndPoint(IPAddress.Any, 0);
                        var dados = udp.Receive(ref origem);
                        var aparelho = Interpretar(dados, origem);
                        if (aparelho != null) encontrados[aparelho.Endereco + ":" + aparelho.Porta] = aparelho;
                    }
                    catch (SocketException)
                    {
                        // Tempo de espera desta rodada esgotado.
                    }
                }
            }
            return new List<AparelhoEncontrado>(encontrados.Values);
        }

        static AparelhoEncontrado Interpretar(byte[] dados, IPEndPoint origem)
        {
            try
            {
                var resposta = JsonUtility.FromJson<RespostaDescoberta>(Encoding.UTF8.GetString(dados));
                if (resposta == null || resposta.servico != "cassinax-link" || resposta.porta <= 0) return null;
                var nome = string.IsNullOrEmpty(resposta.nome) ? origem.Address.ToString() : resposta.nome;
                return new AparelhoEncontrado(nome, origem.Address.ToString(), resposta.porta, MeioConexao.WifiAutomatico);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        struct RedeLocal
        {
            public uint Ip;
            public uint Mascara;
        }

        /// <summary>Redes IPv4 ativas deste computador (ignora loopback e enderecos automaticos 169.254).</summary>
        static List<RedeLocal> RedesLocais()
        {
            var redes = new List<RedeLocal>();
            try
            {
                foreach (var interfaceRede in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (interfaceRede.OperationalStatus != OperationalStatus.Up ||
                        interfaceRede.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var endereco in interfaceRede.GetIPProperties().UnicastAddresses)
                    {
                        if (endereco.Address.AddressFamily != AddressFamily.InterNetwork || endereco.IPv4Mask == null) continue;
                        var ip = ParaNumero(endereco.Address);
                        if ((ip >> 16) == 0xA9FE) continue; // 169.254.x.x
                        redes.Add(new RedeLocal { Ip = ip, Mascara = ParaNumero(endereco.IPv4Mask) });
                    }
                }
            }
            catch (Exception)
            {
                // Algumas plataformas nao informam mascara; o broadcast geral continua.
            }
            return redes;
        }

        /// <summary>Broadcast geral e broadcast de cada rede (alguns roteadores so repassam este).</summary>
        static List<IPEndPoint> Destinos(List<RedeLocal> redes)
        {
            var destinos = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, InfoEngineLink.PortaDescoberta) };
            foreach (var rede in redes)
            {
                destinos.Add(new IPEndPoint(ParaEndereco(rede.Ip | ~rede.Mascara), InfoEngineLink.PortaDescoberta));
            }
            return destinos;
        }

        /// <summary>Todos os enderecos de redes com ate 254 hosts (mascara /24 ou menor).</summary>
        static List<IPEndPoint> Varredura(List<RedeLocal> redes)
        {
            var destinos = new List<IPEndPoint>();
            foreach (var rede in redes)
            {
                var hosts = ~rede.Mascara;
                if (hosts == 0 || hosts > 255) continue;
                var baseRede = rede.Ip & rede.Mascara;
                for (uint h = 1; h < hosts; h++)
                {
                    var alvo = baseRede | h;
                    if (alvo != rede.Ip) destinos.Add(new IPEndPoint(ParaEndereco(alvo), InfoEngineLink.PortaDescoberta));
                }
            }
            return destinos;
        }

        static void Enviar(UdpClient udp, byte[] dados, List<IPEndPoint> destinos)
        {
            foreach (var destino in destinos)
            {
                try { udp.Send(dados, dados.Length, destino); }
                catch (SocketException) { }
            }
        }

        static uint ParaNumero(IPAddress endereco)
        {
            var b = endereco.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        static IPAddress ParaEndereco(uint numero)
        {
            return new IPAddress(new[] { (byte)(numero >> 24), (byte)(numero >> 16), (byte)(numero >> 8), (byte)numero });
        }
    }
}
