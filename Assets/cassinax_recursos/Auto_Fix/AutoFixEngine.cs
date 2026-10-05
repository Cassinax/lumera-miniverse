// Cassinax Auto Fix - v1.0.0
// Motor: descobre os modulos, junta os achados e aplica somente o que foi aprovado.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace cassinax.autofix
{
    /// <summary>
    /// Manutencao dos pacotes Cassinax dentro de um projeto.
    ///
    /// O problema que resolve: quando um pacote muda de layout entre versoes, reimportar
    /// deixa as duas copias no projeto. Duas copias da mesma classe nao compilam, e o
    /// Unity so aponta "tipo duplicado" sem dizer qual sobra apagar.
    ///
    /// Regras que o motor nunca quebra:
    ///
    /// 1. **Versao superior ganha.** Entre duas instalacoes da mesma API, fica a de maior
    ///    versao; a menor e proposta para remocao.
    /// 2. **So remove o que e do pacote.** Um arquivo so entra na lista se a assinatura da
    ///    API aparecer no conteudo. Codigo escrito pelo projeto nunca e candidato, mesmo
    ///    que esteja num caminho antigo do pacote.
    /// 3. **Nada sai sem aprovacao.** <see cref="Scan"/> nao altera o disco.
    ///    <see cref="Apply"/> roda apenas sobre a lista que o chamador entregou.
    /// 4. **Um modulo por API.** Cada pacote conhece os proprios caminhos legados; o motor
    ///    nao adivinha nada sobre APIs que nao se apresentaram.
    /// </summary>
    public static class AutoFixEngine
    {
        /// <summary>
        /// Fronteira de atuacao. O motor nunca remove nada fora de uma pasta
        /// <c>cassinax_recursos</c>: o que esta fora pertence ao projeto, nao aos pacotes.
        /// Sobras de versoes antigas fora dessa fronteira sao apenas relatadas.
        /// </summary>
        public const string FronteiraDeAtuacao = "cassinax_recursos";

        /// <summary>
        /// Verdadeiro quando o caminho esta dentro da fronteira e, portanto, pode ser
        /// removido pelo motor. Vale tanto para arquivo como para pasta.
        /// </summary>
        public static bool DentroDaFronteira(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (assetPath.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            string alvo = "/" + FronteiraDeAtuacao + "/";
            return assetPath.IndexOf(alvo, StringComparison.Ordinal) >= 0 ||
                   assetPath.EndsWith("/" + FronteiraDeAtuacao, StringComparison.Ordinal);
        }

        /// <summary>Modulos descobertos por reflexao nas assemblies do Editor.</summary>
        public static List<IAutoFixModule> DiscoverModules()
        {
            var modulos = new List<IAutoFixModule>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] tipos;
                try { tipos = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException carga)
                {
                    // Assembly com dependencia faltando ainda pode conter modulos utilizaveis.
                    tipos = carga.Types.Where(t => t != null).ToArray();
                }
                catch (Exception) { continue; }

                foreach (Type tipo in tipos)
                {
                    if (tipo.IsAbstract || tipo.IsInterface) continue;
                    if (!typeof(IAutoFixModule).IsAssignableFrom(tipo)) continue;
                    if (tipo.GetConstructor(Type.EmptyTypes) == null) continue;
                    try { modulos.Add((IAutoFixModule)Activator.CreateInstance(tipo)); }
                    catch (Exception e) { Debug.LogWarning("[Auto Fix] Modulo " + tipo.Name + " nao carregou: " + e.Message); }
                }
            }
            return modulos.OrderBy(m => m.DisplayName, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Roda todos os modulos e devolve os achados. Nao altera nada no projeto.
        /// </summary>
        public static List<AutoFixFinding> Scan(IEnumerable<IAutoFixModule> modulos = null)
        {
            var achados = new List<AutoFixFinding>();
            foreach (IAutoFixModule modulo in modulos ?? DiscoverModules())
            {
                try { achados.AddRange(modulo.Scan() ?? Enumerable.Empty<AutoFixFinding>()); }
                catch (Exception e)
                {
                    Debug.LogError("[Auto Fix] " + modulo.DisplayName + " falhou na varredura: " + e.Message);
                }
            }
            return achados
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.AssetPath, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Aplica os achados de remocao aprovados. Devolve quantos caminhos sairam.
        ///
        /// Achados marcados como <see cref="AutoFixAction.Report"/> sao ignorados por
        /// construcao: eles existem para o usuario decidir a mao.
        /// </summary>
        public static int Apply(IEnumerable<AutoFixFinding> aprovados)
        {
            if (aprovados == null) return 0;
            var paraRemover = aprovados.Where(a => a != null && a.Action == AutoFixAction.Delete)
                                       .Select(a => a.AssetPath)
                                       .Where(p => !string.IsNullOrEmpty(p))
                                       .Distinct(StringComparer.Ordinal)
                                       .ToList();
            if (paraRemover.Count == 0) return 0;

            int removidos = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string caminho in paraRemover)
                {
                    // Ultima barreira: vale mesmo que um modulo tenha proposto o caminho.
                    if (!DentroDaFronteira(caminho))
                    {
                        Debug.LogWarning("[Auto Fix] Recusado por estar fora de " +
                                         FronteiraDeAtuacao + ": " + caminho);
                        continue;
                    }
                    if (!AssetDatabase.DeleteAsset(caminho))
                    {
                        Debug.LogWarning("[Auto Fix] Nao foi possivel remover: " + caminho);
                        continue;
                    }
                    removidos++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
            Debug.Log("[Auto Fix] " + removidos + " de " + paraRemover.Count + " caminho(s) removido(s).");
            return removidos;
        }

        //--------------------------------------------------------------- Apoio aos modulos

        /// <summary>
        /// Procura, em todo o projeto, arquivos com um nome e que contenham a assinatura da
        /// API. A assinatura e a salvaguarda: sem ela, um arquivo homonimo escrito pelo
        /// projeto entraria na lista de remocao.
        /// </summary>
        public static List<string> FindSignedAssets(string fileName, string signature)
        {
            var encontrados = new List<string>();
            if (string.IsNullOrEmpty(fileName)) return encontrados;

            string alvo = Path.GetFileName(fileName);
            foreach (string guid in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(alvo)))
            {
                string caminho = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(caminho) || Path.GetFileName(caminho) != alvo) continue;
                if (!string.IsNullOrEmpty(signature) && !ContainsSignature(caminho, signature)) continue;
                encontrados.Add(caminho);
            }
            return encontrados.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>Verdadeiro quando o arquivo existe e tem a assinatura da API.</summary>
        public static bool ContainsSignature(string assetPath, string signature)
        {
            try
            {
                string absoluto = ToAbsolute(assetPath);
                if (!File.Exists(absoluto)) return false;
                // A assinatura fica no cabecalho: ler o inicio basta e evita carregar arquivos grandes.
                using (var leitor = new StreamReader(absoluto))
                {
                    char[] buffer = new char[2048];
                    int lidos = leitor.Read(buffer, 0, buffer.Length);
                    return lidos > 0 && new string(buffer, 0, lidos).IndexOf(signature, StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>Le a versao declarada no cabecalho de um arquivo do pacote.</summary>
        public static AutoFixVersion ReadVersion(string assetPath)
        {
            try
            {
                string absoluto = ToAbsolute(assetPath);
                if (!File.Exists(absoluto)) return AutoFixVersion.Desconhecida;
                using (var leitor = new StreamReader(absoluto))
                {
                    char[] buffer = new char[2048];
                    int lidos = leitor.Read(buffer, 0, buffer.Length);
                    return lidos > 0 ? AutoFixVersion.Ler(new string(buffer, 0, lidos)) : AutoFixVersion.Desconhecida;
                }
            }
            catch (Exception) { return AutoFixVersion.Desconhecida; }
        }

        /// <summary>Verdadeiro quando o caminho existe como arquivo ou pasta.</summary>
        public static bool Exists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string absoluto = ToAbsolute(assetPath);
            return File.Exists(absoluto) || Directory.Exists(absoluto);
        }

        /// <summary>Pasta que contem o caminho, em formato de asset.</summary>
        public static string ParentFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;
            int corte = assetPath.LastIndexOf('/');
            return corte <= 0 ? string.Empty : assetPath.Substring(0, corte);
        }

        private static string ToAbsolute(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath);
    }
}
#endif
