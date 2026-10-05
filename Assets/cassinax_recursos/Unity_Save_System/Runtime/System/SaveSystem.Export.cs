// Cassinax Unity System Save - v1.0.0
// Exportacao por proposito com filtro e codificacao.
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
        public string Exportar(string idJogo, string versaoJogo, int versaoEstrutura, ExportFilter? filtro, string plataforma)
        {
            if (_gameId == null) Configure(idJogo, versaoEstrutura);
            if (_gameId != idJogo || _schema != versaoEstrutura) throw new SaveValidationException(SaveError.WrongGame);
            return ExportSnapshot(SaveExportPurpose.Manual, filtro, versaoJogo, plataforma);
        }
        public string ExportSnapshot(SaveExportPurpose purpose, ExportFilter? filter = null, string gameVersion = "", string platform = "")
        {
            RequireConfiguration();
            var data = _core.Snapshot();
            foreach (var key in data.Keys.ToList())
            {
                SaveScope scope = ScopeOf(key);
                bool include = purpose == SaveExportPurpose.Backup || scope == SaveScope.Progress || scope == SaveScope.Audit ||
                    (purpose == SaveExportPurpose.Manual && scope == SaveScope.Identity);
                if (scope == SaveScope.Metadata) include = true;
                if (include && scope == SaveScope.Progress && filter.HasValue && !filter.Value.AllData)
                {
                    var f = filter.Value;
                    if (f.ComandosIncluir != null && f.ComandosIncluir.Count > 0) include = f.ComandosIncluir.Any(c => key.StartsWith("[" + c + "],", StringComparison.Ordinal));
                    if (f.ComandosExcluir != null && f.ComandosExcluir.Any(c => key.StartsWith("[" + c + "],", StringComparison.Ordinal))) include = false;
                    if (f.PrefixosChave != null && f.PrefixosChave.Count > 0) include &= f.PrefixosChave.Any(p => key.StartsWith(p, StringComparison.Ordinal));
                }
                if (!include) data.Remove(key);
            }
            // Cloud transmits only the pseudonymous ownership field, never generic identity fields.
            if (purpose == SaveExportPurpose.Cloud)
            {
                string owner = Owner(_core.Snapshot());
                if (!string.IsNullOrEmpty(owner)) data["[SLG0003],[accountHash]"] = owner;
            }
            FillHeader(data);
            data[Header("versaoJogo")] = gameVersion;
            data[Header("plataformaOrigem")] = platform;
            data[Header("tipoSave")] = filter.HasValue && !filter.Value.AllData ? "filtered" : purpose.ToString();
            return Encode(data);
        }
        private string Encode(Dictionary<string, string> data)
        {
            FillHeader(data);
            string body = "[2]" + _core.Encrypt(_core.Serialize(data));
            string result = body + "." + _core.ComputeIntegrityHash(body);
            if (Encoding.UTF8.GetByteCount(result) > _core.Limits.MaxPayloadBytes) throw new SaveValidationException(SaveError.LimitExceeded);
            return result;
        }
    }
}
