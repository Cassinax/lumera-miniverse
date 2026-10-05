// Cassinax Unity System Save - v1.0.0
// Validacao integral de payload antes de qualquer mutacao.
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
        public bool TryValidateSnapshot(string payload, string expectedGame, int schema, out ValidatedSaveSnapshot snapshot, out SaveError error)
            => Validate(payload, expectedGame, schema, false, out snapshot, out error);
        private bool Validate(string payload, string expectedGame, int schema, bool local, out ValidatedSaveSnapshot snapshot, out SaveError error)
        {
            snapshot = null;
            error = SaveError.None;
            try
            {
                if (string.IsNullOrEmpty(payload)) throw new SaveValidationException(SaveError.InvalidFormat);
                if (payload.Length > _core.Limits.MaxPayloadBytes || Encoding.UTF8.GetByteCount(payload) > _core.Limits.MaxPayloadBytes)
                    throw new SaveValidationException(SaveError.LimitExceeded);
                int end = payload.IndexOf(']');
                if (payload[0] != '[' || end < 2 || end > 10 || !int.TryParse(payload.Substring(1, end - 1), out int core) || core < 1)
                    throw new SaveValidationException(SaveError.InvalidFormat);
                if (core > CoreVersion) throw new SaveValidationException(SaveError.FutureCore);
                _legacyKeys.TryGetValue(core, out var legacyKey);
                string cipher = payload.Substring(end + 1);
                string plain;
                if (core == 2)
                {
                    int dot = cipher.LastIndexOf('.');
                    if (dot < 0) throw new SaveValidationException(SaveError.Integrity);
                    string body = payload.Substring(0, end + 1 + dot);
                    if (!SaveCore.HashEquals(_core.ComputeIntegrityHash(body), cipher.Substring(dot + 1)))
                        throw new SaveValidationException(SaveError.Integrity);
                    plain = _core.Decrypt(cipher.Substring(0, dot));
                    if (!plain.StartsWith("CUSS2\n", StringComparison.Ordinal)) throw new SaveValidationException(SaveError.InvalidFormat);
                }
                else plain = legacyKey == null ? _core.Decrypt(cipher) : _core.DecryptCompativel(cipher, legacyKey);
                var data = _core.Parse(plain);
                if (core == 1)
                {
                    string hash = Value(data, Header("hashVerificacao"));
                    bool unsigned = hash == "" || hash == "local-nao-exportado";
                    if (!(unsigned && local && AllowUnsignedLegacyLocal))
                    {
                        string unsignedText = Regex.Replace(plain, @"(?m)^\[SLG0001\],?\[hashVerificacao\]\[[^\]]*\]\r?\n?", "");
                        string computed = legacyKey == null ? _core.ComputeIntegrityHash(unsignedText) : _core.ComputeIntegrityHash(unsignedText, legacyKey);
                        if (!SaveCore.HashEquals(computed, hash)) throw new SaveValidationException(SaveError.Integrity);
                    }
                }
                if (Value(data, Header("IDJogo")) != expectedGame) throw new SaveValidationException(SaveError.WrongGame);
                if (!int.TryParse(Value(data, Header("versaoEstruturaSave")), out int version) || version < 1)
                    throw new SaveValidationException(SaveError.InvalidFormat);
                if (version > schema) throw new SaveValidationException(SaveError.FutureSchema);
                if (int.TryParse(Value(data, Header("versaoCore")), out int headerCore) && headerCore > CoreVersion)
                    throw new SaveValidationException(SaveError.FutureCore);
                if (core == 2 && headerCore != core) throw new SaveValidationException(SaveError.InvalidFormat);
                string identity = Owner(data);
                while (version < schema)
                {
                    if (_migrations.TryGetValue(version, out var migrate))
                    {
                        var readOnly = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(data));
                        data = new Dictionary<string, string>(migrate(readOnly), StringComparer.Ordinal);
                        version++;
                    }
                    else if (_legacyMigration != null)
                    {
                        data = _core.Parse(_legacyMigration(_core.Serialize(data), version));
                        version = schema;
                    }
                    else throw new SaveValidationException(SaveError.MigrationRequired);
                    if (Owner(data) != identity || Value(data, Header("IDJogo")) != expectedGame)
                        throw new SaveValidationException(SaveError.MigrationFailed);
                    _core.Serialize(data);
                }
                data[Header("versaoEstruturaSave")] = schema.ToString(CultureInfo.InvariantCulture);
                data.Remove(Header("hashVerificacao"));
                if (!data.ContainsKey(Meta("epoch"))) data[Meta("epoch")] = "0";
                if (!long.TryParse(data[Meta("epoch")], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch) || epoch < 0)
                    throw new SaveValidationException(SaveError.InvalidFormat);
                foreach (var key in new[] { Meta("revision"), Meta("parent"), LocalMeta("baseRevision") })
                    if (Value(data, key) != "" && !Guid.TryParseExact(data[key], "N", out _)) throw new SaveValidationException(SaveError.InvalidFormat);
                if (Value(data, Meta("tombstone")) != "" && Value(data, Meta("tombstone")) != "true" && Value(data, Meta("tombstone")) != "false")
                    throw new SaveValidationException(SaveError.InvalidFormat);
                if (Value(data, Meta("tombstone")) == "true" && data.Any(p => ScopeOf(p.Key) == SaveScope.Progress))
                    throw new SaveValidationException(SaveError.InvalidFormat);
                snapshot = new ValidatedSaveSnapshot(this, data, core, core == 1);
                return true;
            }
            catch (Exception ex) { error = Classify(ex); return false; }
        }
    }
}
