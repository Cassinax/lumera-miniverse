// Cassinax Unity System Save - v1.0.0
// Chaves reservadas, escopo por chave e exigencia de configuracao.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace cassinax.savesystem
{
    public partial class SaveSystem
    {
        public static string Header(string field) => "[SLG0001],[" + field + "]";
        public static string Meta(string field) => "[CUSS],[" + field + "]";
        public static string LocalMeta(string field) => "[CUSSL],[" + field + "]";
        internal static string Value(IDictionary<string, string> data, string key, string fallback = "") => data.TryGetValue(key, out var v) ? v : fallback;
        internal static string Owner(IDictionary<string, string> data)
        {
            string owner = Value(data, "[SLG0003],[accountHash]");
            return owner == "nao_vinculado" ? "" : owner;
        }
        public static SaveScope DefaultScope(string key)
        {
            if (key.StartsWith("[SLG0003],", StringComparison.Ordinal)) return SaveScope.Identity;
            if (key.StartsWith("[SLG0200],", StringComparison.Ordinal)) return SaveScope.Audit;
            if (key.StartsWith("[CUSS],", StringComparison.Ordinal) || IsHeader(key)) return SaveScope.Metadata;
            if (key.StartsWith("[SLG0002],", StringComparison.Ordinal)) return SaveScope.Progress;
            return SaveScope.LocalPreference;
        }
        private static bool IsHeader(string key) => new[] { "0001", "IDJogo", "versaoJogo", "versaoEstruturaSave",
            "versaoCore", "dataCriacao", "dataExportacao", "dataAtualizacao", "plataformaOrigem", "tipoSave", "hashVerificacao" }
            .Any(f => key == Header(f));
        public SaveScope ScopeOf(string key)
        {
            if (key.StartsWith("[CUSS],", StringComparison.Ordinal) || IsHeader(key)) return SaveScope.Metadata;
            if (key.StartsWith("[CUSSL],", StringComparison.Ordinal)) return SaveScope.LocalPreference;
            if (key.StartsWith("[SLG0003],", StringComparison.Ordinal)) return SaveScope.Identity;
            var scope = _scope(key);
            return scope == SaveScope.Metadata ? SaveScope.LocalPreference : scope;
        }
        private void RequireConfiguration()
        {
            if (_gameId == null) throw new SaveValidationException(SaveError.InvalidConfiguration);
        }
    }
}
