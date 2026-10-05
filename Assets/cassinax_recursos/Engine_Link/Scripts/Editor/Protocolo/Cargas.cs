using System;
using System.Collections.Generic;
using System.IO;

namespace Cassinax.EngineLink.Editor
{
    /*
     * Conteudos binarios (little-endian), iguais aos do app (documento 08).
     * BinaryReader/BinaryWriter do .NET sao sempre little-endian.
     */

    public enum FaseToque
    {
        Inicio = 0,
        Movimento = 1,
        Parado = 2,
        Fim = 3,
        Cancelado = 4,
    }

    /// <summary>Posicao normalizada (0..1) relativa ao quadro exibido no aparelho.</summary>
    public struct PontoToque
    {
        public int Id;
        public FaseToque Fase;
        public float X;
        public float Y;
        public float Pressao;
    }

    public sealed class Toque
    {
        public long Carimbo;
        public readonly List<PontoToque> Pontos = new List<PontoToque>();

        public Mensagem Codificar()
        {
            return Cargas.Escrever(TipoMensagem.Toque, w =>
            {
                w.Write(Carimbo);
                w.Write((byte)Pontos.Count);
                foreach (var p in Pontos)
                {
                    w.Write(p.Id);
                    w.Write((byte)p.Fase);
                    w.Write(p.X);
                    w.Write(p.Y);
                    w.Write(p.Pressao);
                }
            });
        }

        public static Toque Decodificar(Mensagem mensagem)
        {
            return Cargas.Ler(mensagem, TipoMensagem.Toque, r =>
            {
                var toque = new Toque { Carimbo = r.ReadInt64() };
                int quantidade = r.ReadByte();
                for (var i = 0; i < quantidade; i++)
                {
                    var ponto = new PontoToque { Id = r.ReadInt32() };
                    int fase = r.ReadByte();
                    if (fase > (int)FaseToque.Cancelado) throw new ProtocoloException("Fase de toque invalida: " + fase);
                    ponto.Fase = (FaseToque)fase;
                    ponto.X = r.ReadSingle();
                    ponto.Y = r.ReadSingle();
                    ponto.Pressao = r.ReadSingle();
                    toque.Pontos.Add(ponto);
                }
                return toque;
            });
        }
    }

    /// <summary>Tipo = Sensor.TYPE_* do Android (1 acelerometro, 4 giroscopio, 9 gravidade...).</summary>
    public sealed class LeituraSensor
    {
        public int Tipo;
        public long Carimbo;
        public float[] Valores = new float[0];

        public Mensagem Codificar()
        {
            return Cargas.Escrever(TipoMensagem.Sensor, w =>
            {
                w.Write((ushort)Tipo);
                w.Write(Carimbo);
                w.Write((byte)Valores.Length);
                foreach (var v in Valores) w.Write(v);
            });
        }

        public static LeituraSensor Decodificar(Mensagem mensagem)
        {
            return Cargas.Ler(mensagem, TipoMensagem.Sensor, r =>
            {
                var leitura = new LeituraSensor { Tipo = r.ReadUInt16(), Carimbo = r.ReadInt64() };
                leitura.Valores = new float[r.ReadByte()];
                for (var i = 0; i < leitura.Valores.Length; i++) leitura.Valores[i] = r.ReadSingle();
                return leitura;
            });
        }
    }

    /// <summary>Codigo = KeyEvent.KEYCODE_* do Android.</summary>
    public sealed class Tecla
    {
        public int Codigo;
        public bool Pressionada;
        public int Meta;
        public long Carimbo;

        public static Tecla Decodificar(Mensagem mensagem)
        {
            return Cargas.Ler(mensagem, TipoMensagem.Tecla, r =>
            {
                var tecla = new Tecla { Codigo = r.ReadInt32() };
                int acao = r.ReadByte();
                if (acao > 1) throw new ProtocoloException("Acao de tecla invalida: " + acao);
                tecla.Pressionada = acao == 0;
                tecla.Meta = r.ReadInt32();
                tecla.Carimbo = r.ReadInt64();
                return tecla;
            });
        }
    }

    /// <summary>Imagem do Game view enviada ao app.</summary>
    public sealed class QuadroImagem
    {
        public const int FormatoJpeg = 1;

        public int Id;
        public int Largura;
        public int Altura;
        public int Formato = FormatoJpeg;
        public long Carimbo;
        public byte[] Imagem = new byte[0];

        public Mensagem Codificar()
        {
            if (Largura < 1 || Largura > 0xFFFF || Altura < 1 || Altura > 0xFFFF)
            {
                throw new ArgumentException("Dimensoes invalidas: " + Largura + "x" + Altura);
            }
            return Cargas.Escrever(TipoMensagem.Quadro, w =>
            {
                w.Write(Id);
                w.Write((ushort)Largura);
                w.Write((ushort)Altura);
                w.Write((byte)Formato);
                w.Write(Carimbo);
                w.Write(Imagem);
            });
        }
    }

    /// <summary>
    /// ESTADO_JOGO: uint8 com bits (1 = Play Mode ativo, 2 = pausado). Com o Play desligado o app
    /// limpa a imagem e ignora quadros que ainda estavam a caminho.
    /// </summary>
    public static class EstadoJogo
    {
        public static Mensagem Codificar(bool jogando, bool pausado)
        {
            var bits = (jogando ? 1 : 0) | (pausado ? 2 : 0);
            return new Mensagem(TipoMensagem.EstadoJogo, new[] { (byte)bits });
        }
    }

    /// <summary>PING/PONG: 8 bytes com o carimbo do emissor; PONG devolve o conteudo do PING.</summary>
    public static class Ping
    {
        public static Mensagem Codificar(long carimbo)
        {
            return Cargas.Escrever(TipoMensagem.Ping, w => w.Write(carimbo));
        }

        public static Mensagem Responder(Mensagem ping)
        {
            if (ping.Tipo != TipoMensagem.Ping) throw new ProtocoloException("Esperado PING");
            return new Mensagem(TipoMensagem.Pong, ping.Conteudo);
        }

        public static long Carimbo(Mensagem mensagem)
        {
            return Cargas.Ler(mensagem, mensagem.Tipo, r => r.ReadInt64());
        }
    }

    public static class Cargas
    {
        public static Mensagem Escrever(int tipo, Action<BinaryWriter> escrever)
        {
            using (var memoria = new MemoryStream())
            using (var w = new BinaryWriter(memoria))
            {
                escrever(w);
                w.Flush();
                return new Mensagem(tipo, memoria.ToArray());
            }
        }

        public static T Ler<T>(Mensagem mensagem, int tipoEsperado, Func<BinaryReader, T> ler)
        {
            if (mensagem.Tipo != tipoEsperado)
            {
                throw new ProtocoloException("Esperado " + TipoMensagem.Nome(tipoEsperado) + ", recebido " + TipoMensagem.Nome(mensagem.Tipo));
            }
            using (var memoria = new MemoryStream(mensagem.Conteudo))
            using (var r = new BinaryReader(memoria))
            {
                T resultado;
                try
                {
                    resultado = ler(r);
                }
                catch (EndOfStreamException e)
                {
                    throw new ProtocoloException("Conteudo truncado em " + TipoMensagem.Nome(mensagem.Tipo), e);
                }
                if (memoria.Position != memoria.Length)
                {
                    throw new ProtocoloException("Bytes sobrando em " + TipoMensagem.Nome(mensagem.Tipo));
                }
                return resultado;
            }
        }
    }
}
