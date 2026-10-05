using System;
using System.IO;
using System.Text;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>Numeros dos tipos de mensagem (uint16). Iguais aos do app.</summary>
    public static class TipoMensagem
    {
        public const int Hello = 0x0001;
        public const int HelloOk = 0x0002;
        public const int HelloRecusado = 0x0003;
        public const int Ping = 0x0004;
        public const int Pong = 0x0005;
        public const int Tchau = 0x0006;

        public const int Toque = 0x0100;
        public const int Sensor = 0x0101;
        public const int Tecla = 0x0102;
        public const int Controle = 0x0103;
        public const int Tela = 0x0104;
        public const int Config = 0x0105;
        /// <summary>Opcoes da sessao (imagem e tempos), nos dois sentidos; a engine guarda e confirma.</summary>
        public const int Opcoes = 0x0106;

        public const int Quadro = 0x0200;
        public const int Diagnostico = 0x0201;
        public const int Log = 0x0202;
        public const int Vibrar = 0x0203;
        public const int Avisos = 0x0204;
        public const int EstadoJogo = 0x0205;

        // Ferramentas para agentes: pedido/resposta com id.
        public const int Comando = 0x0300;
        public const int Resposta = 0x0301;

        public static string Nome(int tipo)
        {
            switch (tipo)
            {
                case Hello: return "HELLO";
                case HelloOk: return "HELLO_OK";
                case HelloRecusado: return "HELLO_RECUSADO";
                case Ping: return "PING";
                case Pong: return "PONG";
                case Tchau: return "TCHAU";
                case Toque: return "TOQUE";
                case Sensor: return "SENSOR";
                case Tecla: return "TECLA";
                case Controle: return "CONTROLE";
                case Tela: return "TELA";
                case Config: return "CONFIG";
                case Opcoes: return "OPCOES";
                case Quadro: return "QUADRO";
                case Diagnostico: return "DIAGNOSTICO";
                case Log: return "LOG";
                case Vibrar: return "VIBRAR";
                case Avisos: return "AVISOS";
                case EstadoJogo: return "ESTADO_JOGO";
                case Comando: return "COMANDO";
                case Resposta: return "RESPOSTA";
                default: return string.Format("DESCONHECIDO(0x{0:X4})", tipo);
            }
        }
    }

    public class ProtocoloException : Exception
    {
        public ProtocoloException(string mensagem, Exception causa = null) : base(mensagem, causa) { }
    }

    /// <summary>
    /// Mensagem do protocolo. No fio: tamanho (uint32 LE) + tipo (uint16 LE) + flags (uint8) + conteudo;
    /// tamanho conta tipo, flags e conteudo.
    /// </summary>
    public sealed class Mensagem
    {
        public const int TamanhoMaximo = 16 * 1024 * 1024;
        public const int CabecalhoInterno = 3;

        public readonly int Tipo;
        public readonly int Flags;
        public readonly byte[] Conteudo;

        public Mensagem(int tipo, byte[] conteudo = null, int flags = 0)
        {
            if (tipo < 0 || tipo > 0xFFFF) throw new ArgumentOutOfRangeException("tipo");
            if (flags < 0 || flags > 0xFF) throw new ArgumentOutOfRangeException("flags");
            Conteudo = conteudo ?? new byte[0];
            if (Conteudo.Length > TamanhoMaximo - CabecalhoInterno) throw new ArgumentException("Conteudo excede o tamanho maximo");
            Tipo = tipo;
            Flags = flags;
        }

        public static Mensagem DeTexto(int tipo, string texto)
        {
            return new Mensagem(tipo, Encoding.UTF8.GetBytes(texto ?? ""));
        }

        public string Texto()
        {
            return Encoding.UTF8.GetString(Conteudo);
        }

        public override string ToString()
        {
            return "Mensagem(" + TipoMensagem.Nome(Tipo) + ", " + Conteudo.Length + " bytes)";
        }
    }

    /// <summary>Leitura e escrita de mensagens. Um leitor e um escritor por stream.</summary>
    public static class CodificadorMensagens
    {
        public static void Escrever(Stream saida, Mensagem mensagem)
        {
            var tamanho = Mensagem.CabecalhoInterno + mensagem.Conteudo.Length;
            var cabecalho = new byte[7];
            cabecalho[0] = (byte)tamanho;
            cabecalho[1] = (byte)(tamanho >> 8);
            cabecalho[2] = (byte)(tamanho >> 16);
            cabecalho[3] = (byte)(tamanho >> 24);
            cabecalho[4] = (byte)mensagem.Tipo;
            cabecalho[5] = (byte)(mensagem.Tipo >> 8);
            cabecalho[6] = (byte)mensagem.Flags;
            saida.Write(cabecalho, 0, cabecalho.Length);
            saida.Write(mensagem.Conteudo, 0, mensagem.Conteudo.Length);
            saida.Flush();
        }

        /// <summary>Retorna null se o stream terminou limpo entre mensagens.</summary>
        public static Mensagem Ler(Stream entrada)
        {
            var primeiro = entrada.ReadByte();
            if (primeiro < 0) return null;
            var resto = LerExato(entrada, 3);
            var tamanho = (uint)primeiro | ((uint)resto[0] << 8) | ((uint)resto[1] << 16) | ((uint)resto[2] << 24);
            if (tamanho < Mensagem.CabecalhoInterno || tamanho > Mensagem.TamanhoMaximo)
            {
                throw new ProtocoloException("Tamanho de mensagem invalido: " + tamanho);
            }
            var corpo = LerExato(entrada, (int)tamanho);
            var tipo = corpo[0] | (corpo[1] << 8);
            var conteudo = new byte[corpo.Length - Mensagem.CabecalhoInterno];
            Buffer.BlockCopy(corpo, Mensagem.CabecalhoInterno, conteudo, 0, conteudo.Length);
            return new Mensagem(tipo, conteudo, corpo[2]);
        }

        static byte[] LerExato(Stream entrada, int quantidade)
        {
            var buffer = new byte[quantidade];
            var lidos = 0;
            while (lidos < quantidade)
            {
                var n = entrada.Read(buffer, lidos, quantidade - lidos);
                if (n <= 0) throw new EndOfStreamException("Conexao encerrada no meio de uma mensagem");
                lidos += n;
            }
            return buffer;
        }
    }
}
