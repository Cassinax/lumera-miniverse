using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Cassinax.EngineLink.Editor
{
    /// <summary>Aparelho listado por "adb devices -l".</summary>
    public struct AparelhoAdb
    {
        public string Serial;
        /// <summary>device, unauthorized, offline...</summary>
        public string Estado;
        public string Modelo;
    }

    /// <summary>Acesso ao adb do SDK Android usado pela Unity (USB e depuracao sem fio).</summary>
    public static class Adb
    {
        /// <summary>Caminho manual do adb (opcional), por usuario.</summary>
        public const string ChaveCaminho = "Cassinax.EngineLink.Adb";

        /// <summary>Deve ser chamado na thread principal (le preferencias do Editor).</summary>
        public static string Localizar()
        {
            var manual = EditorPrefs.GetString(ChaveCaminho, "");
            if (!string.IsNullOrEmpty(manual) && File.Exists(manual)) return manual;
            var executavel = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
            foreach (var sdk in CandidatosSdk())
            {
                if (string.IsNullOrEmpty(sdk)) continue;
                var caminho = Path.Combine(Path.Combine(sdk, "platform-tools"), executavel);
                if (File.Exists(caminho)) return caminho;
            }
            return null;
        }

        static IEnumerable<string> CandidatosSdk()
        {
            // Unity 2019.3+: UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath (modulo Android).
            var tipo = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            if (tipo != null)
            {
                var propriedade = tipo.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.Static);
                if (propriedade != null) yield return propriedade.GetValue(null, null) as string;
            }
            // Versoes antigas guardavam o SDK nas preferencias.
            yield return EditorPrefs.GetString("AndroidSdkRoot", "");
            yield return Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            yield return Environment.GetEnvironmentVariable("ANDROID_HOME");
        }

        /// <summary>Executa o adb e devolve a saida padrao. Bloqueia: use fora da thread principal.</summary>
        public static string Executar(string adb, string argumentos, int tempoLimiteMs = 0)
        {
            if (tempoLimiteMs <= 0) tempoLimiteMs = ConfiguracoesEngineLink.Atual.tempoLimiteAdbMs;
            var inicio = new ProcessStartInfo(adb, argumentos)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var processo = Process.Start(inicio))
            {
                var saida = processo.StandardOutput.ReadToEndAsync();
                var erro = processo.StandardError.ReadToEndAsync();
                if (!processo.WaitForExit(tempoLimiteMs))
                {
                    try { processo.Kill(); } catch (InvalidOperationException) { }
                    throw new IOException("adb did not answer in time (" + argumentos + ")");
                }
                if (processo.ExitCode != 0)
                {
                    throw new IOException("adb " + argumentos + ": " + (erro.Result + saida.Result).Trim());
                }
                return saida.Result;
            }
        }

        public static List<AparelhoAdb> Listar(string adb)
        {
            var lista = new List<AparelhoAdb>();
            foreach (var linhaBruta in Executar(adb, "devices -l").Split('\n'))
            {
                var linha = linhaBruta.Trim();
                if (linha.Length == 0 || linha.StartsWith("List of devices") || linha.StartsWith("*")) continue;
                var partes = linha.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2) continue;
                var aparelho = new AparelhoAdb { Serial = partes[0], Estado = partes[1], Modelo = partes[0] };
                foreach (var parte in partes)
                {
                    if (parte.StartsWith("model:")) aparelho.Modelo = parte.Substring(6).Replace('_', ' ');
                }
                lista.Add(aparelho);
            }
            return lista;
        }

        public static void Encaminhar(string adb, string serial, int portaLocal, int portaAparelho)
        {
            Executar(adb, "-s " + serial + " forward tcp:" + portaLocal + " tcp:" + portaAparelho);
        }

        public static void RemoverEncaminhamento(string adb, string serial, int portaLocal)
        {
            try
            {
                Executar(adb, "-s " + serial + " forward --remove tcp:" + portaLocal, 3000);
            }
            catch (Exception)
            {
                // Encaminhamento ja removido ou aparelho desconectado.
            }
        }

        /// <summary>Porta TCP livre neste computador para o encaminhamento.</summary>
        public static int PortaLivre()
        {
            var ouvinte = new TcpListener(IPAddress.Loopback, 0);
            ouvinte.Start();
            try
            {
                return ((IPEndPoint)ouvinte.LocalEndpoint).Port;
            }
            finally
            {
                ouvinte.Stop();
            }
        }
    }
}
