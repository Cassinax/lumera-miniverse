// Cassinax Unity System Save
// Version: v0.10.0
// Status: integration-pilot

using UnityEngine;
using cassinax.savesystem;

/// <summary>
/// Armazenamento local usando PlayerPrefs.
/// Fica fora do SaveSystem para manter o nucleo fixo independente do meio de persistencia.
///
/// Em WebGL, PlayerPrefs e gravado em IndexedDB pelo proprio Unity e e o storage
/// recomendado, porque System.IO nao tem garantia de flush no navegador.
///
/// As chaves de backup sao prefixadas com a chave principal para que dois jogos
/// publicados no mesmo dominio nao disputem os mesmos backups.
/// </summary>
public class PlayerPrefsSaveStorage : ISaveStorageAdapter
{
    private readonly string _mainKey;

    public PlayerPrefsSaveStorage(string mainKey)
    {
        _mainKey = mainKey;
    }

    public bool HasMainSave()
    {
        return PlayerPrefs.HasKey(_mainKey) && !string.IsNullOrEmpty(PlayerPrefs.GetString(_mainKey));
    }

    public string LoadMainSave()
    {
        return PlayerPrefs.GetString(_mainKey, string.Empty);
    }

    public void SaveMainSave(string payload)
    {
        PlayerPrefs.SetString(_mainKey, payload);
        PlayerPrefs.Save();
    }

    public bool HasBackup(string backupName)
    {
        string key = BuildBackupKey(backupName);
        return PlayerPrefs.HasKey(key) && !string.IsNullOrEmpty(PlayerPrefs.GetString(key));
    }

    public string LoadBackup(string backupName)
    {
        return PlayerPrefs.GetString(BuildBackupKey(backupName), string.Empty);
    }

    public void SaveBackup(string backupName, string payload)
    {
        PlayerPrefs.SetString(BuildBackupKey(backupName), payload);
        PlayerPrefs.Save();
    }

    public void DeleteBackup(string backupName)
    {
        string key = BuildBackupKey(backupName);

        if (PlayerPrefs.HasKey(key))
            PlayerPrefs.DeleteKey(key);

        PlayerPrefs.Save();
    }

    public void DeleteAll()
    {
        PlayerPrefs.DeleteKey(_mainKey);
        PlayerPrefs.DeleteKey(BuildBackupKey("Backup Auto"));
        PlayerPrefs.DeleteKey(BuildBackupKey("Backup Manual"));
        PlayerPrefs.DeleteKey(BuildBackupKey("Backup Seguranca"));
        PlayerPrefs.Save();
    }

    public string BuildBackupKey(string backupName)
    {
        return $"{_mainKey}.{backupName}";
    }
}
