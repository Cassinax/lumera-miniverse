// Cassinax Unity System Save - v1.0.0
// Armazenamento em memoria. Nada e gravado em disco.
using System;
using System.Collections.Generic;

namespace cassinax.savesystem
{
    /// <summary>
    /// Armazenamento volatil que implementa <see cref="ISaveStorageAdapter"/> sem tocar o disco.
    ///
    /// Dois usos legitimos:
    ///
    /// 1. Persistencia suspensa. Quando o arquivo local e invalido ou de versao futura, a
    ///    regra do pacote e nao sobrescrever. O jogo troca o storage por este, continua
    ///    jogavel na sessao e sinaliza o estado. O arquivo protegido permanece intacto no
    ///    disco, aguardando um reset explicito do jogador.
    /// 2. Testes, onde gravar em disco atrasa e suja o ambiente.
    ///
    /// O conteudo morre com o processo: isto nao e um storage de producao.
    /// Antes da v1.0.0 cada jogo reimplementava esta classe; agora ela vem no pacote.
    /// </summary>
    public sealed class MemorySaveStorage : ISaveStorageAdapter
    {
        private readonly Dictionary<string, string> _backups = new Dictionary<string, string>(StringComparer.Ordinal);
        private string _main = string.Empty;

        /// <summary>Cria um armazenamento vazio.</summary>
        public MemorySaveStorage() { }

        /// <summary>
        /// Cria um armazenamento ja contendo um payload principal.
        /// Util para continuar a sessao a partir do ultimo conteudo valido lido do disco.
        /// </summary>
        public MemorySaveStorage(string payloadInicial)
        {
            _main = payloadInicial ?? string.Empty;
        }

        /// <summary>Quantidade de backups guardados nesta instancia.</summary>
        public int BackupCount => _backups.Count;

        public bool HasMainSave() => !string.IsNullOrEmpty(_main);

        public string LoadMainSave() => _main;

        public void SaveMainSave(string payload) => _main = payload ?? string.Empty;

        public bool HasBackup(string backupName) =>
            backupName != null && _backups.TryGetValue(backupName, out var p) && !string.IsNullOrEmpty(p);

        public string LoadBackup(string backupName) =>
            backupName != null && _backups.TryGetValue(backupName, out var p) ? p : string.Empty;

        public void SaveBackup(string backupName, string payload)
        {
            if (backupName == null) return;
            _backups[backupName] = payload ?? string.Empty;
        }

        public void DeleteBackup(string backupName)
        {
            if (backupName != null) _backups.Remove(backupName);
        }

        public void DeleteAll()
        {
            _main = string.Empty;
            _backups.Clear();
        }
    }
}
