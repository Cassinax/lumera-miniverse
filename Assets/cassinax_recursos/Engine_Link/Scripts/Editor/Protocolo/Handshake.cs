using System;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>HELLO enviado pela engine. Campos vazios equivalem a ausentes no app.</summary>
    [Serializable]
    public class HelloEngine
    {
        public string protocolo = InfoEngineLink.Protocolo;
        public string pacote = InfoEngineLink.Versao;
        public string engine = "";
        public string nomeEngine = "Unity";
        public string projeto = "";
        public string codigo = "";
        public string token = "";
        /// <summary>Tempo limite da sessao (ms) escolhido pelo dev; o app usa o mesmo valor.</summary>
        public int tempoLimiteMs;
    }

    [Serializable]
    public class AparelhoRemoto
    {
        public string nome;
        public string fabricante;
        public string modelo;
        public string android;
        public int api;
        public string abis;
    }

    [Serializable]
    public class TelaRemota
    {
        public int largura;
        public int altura;
        public int dpi;
        public string orientacao;
    }

    /// <summary>HELLO_OK do app. O preset e lido por outras partes a partir do texto bruto.</summary>
    [Serializable]
    public class RespostaHelloOk
    {
        public string protocolo;
        public string app;
        public AparelhoRemoto aparelho;
        public TelaRemota tela;
        public string[] recursos;
        /// <summary>Preenchido so quando o pareamento por codigo criou a confianca.</summary>
        public string token;
    }

    [Serializable]
    public class RecusaHello
    {
        public string protocolo;
        /// <summary>versao, codigo, ocupado ou formato.</summary>
        public string motivo;
        public string mensagem;
    }

    [Serializable]
    public class Tchau
    {
        public string motivo;
    }

    public static class Handshake
    {
        /// <summary>versaoEngine e projeto devem ser lidos na thread principal (Application.*).</summary>
        public static Mensagem CriarHello(string codigo, string token, string versaoEngine, string projeto, int tempoLimiteMs = 0)
        {
            var hello = new HelloEngine
            {
                codigo = codigo ?? "",
                token = token ?? "",
                engine = versaoEngine ?? "",
                projeto = projeto ?? "",
                tempoLimiteMs = tempoLimiteMs,
            };
            return Mensagem.DeTexto(TipoMensagem.Hello, JsonUtility.ToJson(hello));
        }

        public static RespostaHelloOk LerHelloOk(Mensagem mensagem)
        {
            return Ler<RespostaHelloOk>(mensagem, TipoMensagem.HelloOk);
        }

        public static RecusaHello LerRecusa(Mensagem mensagem)
        {
            return Ler<RecusaHello>(mensagem, TipoMensagem.HelloRecusado);
        }

        public static Mensagem CriarTchau(string motivo)
        {
            return Mensagem.DeTexto(TipoMensagem.Tchau, JsonUtility.ToJson(new Tchau { motivo = motivo }));
        }

        public static string LerMotivoTchau(Mensagem mensagem)
        {
            try
            {
                var tchau = JsonUtility.FromJson<Tchau>(mensagem.Texto());
                return tchau != null && !string.IsNullOrEmpty(tchau.motivo) ? tchau.motivo : "";
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        /// <summary>Versao maior do protocolo informado pelo app; -1 se invalida.</summary>
        public static int VersaoMaior(string protocolo)
        {
            if (string.IsNullOrEmpty(protocolo)) return -1;
            var partes = protocolo.Split('.');
            int maior;
            return int.TryParse(partes[0], out maior) ? maior : -1;
        }

        /// <summary>Texto amigavel para a recusa do app.</summary>
        public static string Explicar(RecusaHello recusa)
        {
            switch (recusa.motivo)
            {
                case "versao": return "Incompatible app version. Update the app and the package. (" + recusa.mensagem + ")";
                case "codigo": return "Wrong or missing pairing code. Type the code shown in the app.";
                case "ocupado": return "The device is already connected to another editor.";
                default: return "The app refused the connection: " + recusa.mensagem;
            }
        }

        static T Ler<T>(Mensagem mensagem, int tipo) where T : class
        {
            if (mensagem.Tipo != tipo) throw new ProtocoloException("Esperado " + TipoMensagem.Nome(tipo));
            try
            {
                var valor = JsonUtility.FromJson<T>(mensagem.Texto());
                if (valor == null) throw new ProtocoloException(TipoMensagem.Nome(tipo) + " vazio");
                return valor;
            }
            catch (ArgumentException e)
            {
                throw new ProtocoloException(TipoMensagem.Nome(tipo) + " invalido: " + e.Message, e);
            }
        }
    }
}
