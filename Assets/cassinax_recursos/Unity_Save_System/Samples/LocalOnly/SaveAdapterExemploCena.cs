// Cassinax Unity System Save - v1.0.0. Exemplo de cena, nunca criado sozinho em runtime.
using System;
using UnityEngine;
using UnityEngine.Events;
using cassinax.savesystem;

/// <summary>
/// Evento de chave de traducao. Precisa ser uma subclasse concreta e serializavel:
/// UnityEvent&lt;T&gt; generico nao aparece no Inspector e o campo chega vazio em runtime.
/// </summary>
[Serializable]
public sealed class EventoChaveDeTexto : UnityEvent<string> { }

public sealed class SaveAdapterExemploCena : MonoBehaviour
{
    [SerializeField] private SaveAdapterExemplo _adapter = null;
    [SerializeField] private GameObject _conflictPanel = null;
    [SerializeField] private EventoChaveDeTexto _localizationKey = new EventoChaveDeTexto();
    private SaveConflictPrompt _prompt;
    private void OnEnable()
    {
        if (_adapter != null) _adapter.ConflitoEncontrado += ShowConflict;
    }
    private void OnDisable()
    {
        if (_adapter != null) _adapter.ConflitoEncontrado -= ShowConflict;
        Defer();
    }
    private void ShowConflict(SaveConflictPrompt prompt)
    {
        _prompt = prompt;
        _localizationKey.Invoke("save.conflict");
        if (_conflictPanel != null) _conflictPanel.SetActive(true);
    }
    public void ChooseLocal() { var p = Close(); p?.ChooseLocal(); }
    public void ChooseIncoming() { var p = Close(); p?.ChooseIncoming(); }
    public void Defer() { var p = Close(); p?.Defer(); }
    private SaveConflictPrompt Close()
    {
        var p = _prompt; _prompt = null;
        if (_conflictPanel != null) _conflictPanel.SetActive(false);
        return p;
    }
    [ContextMenu("Commit example checkpoint")]
    public void CommitExampleCheckpoint()
    {
        if (_adapter == null) return;
        _adapter.Begin();
        _adapter.SalvarDados("SLG0002", "checkpoint", "1");
        if (!_adapter.Commit()) _adapter.Rollback();
    }
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Window/Cassinax/Exemplo de Save Local")]
    private static void CreateLocalExample()
    {
        var root = new GameObject("Save Local Example");
        root.SetActive(false);
        UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Create local save example");
        var adapter = root.AddComponent<SaveAdapterExemplo>();
        var config = new UnityEditor.SerializedObject(adapter);
        config.FindProperty("_idJogo").stringValue = "example.local-only";
        config.FindProperty("_chaveCriptografia").stringValue = Guid.NewGuid().ToString("N");
        config.FindProperty("_inicializarNoAwake").boolValue = true;
        config.ApplyModifiedPropertiesWithoutUndo();
        var example = root.AddComponent<SaveAdapterExemploCena>();
        example._adapter = adapter;
        root.SetActive(true);
        UnityEditor.Selection.activeGameObject = root;
    }
#endif
}
