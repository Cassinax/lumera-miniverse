using System;
using UnityEditor;
using UnityEngine;

public enum CarlosPaletteId { Solar, LumeraAqua, CeuAventureiro, ArcadeSuave }

[Serializable]
public sealed class CarlosPaletteDefinition
{
    public string nome;
    public Color cabelo, camisa, shortCor, tenis, detalhesTenis;

    public Color RegionColor(string region, Color original)
    {
        switch (region)
        {
            case "Material_Cabelo": return cabelo;
            case "Material_Casaco": return camisa;
            case "Material_Short": return shortCor;
            case "Material_Botas": return tenis;
            case "Material_Botas_Cadarcos": return detalhesTenis;
            default: return original;
        }
    }
}

// Asset de autoria: Editor/ o mantem fora da build. O jogo usa so textura/material.
public sealed class CarlosPalettes : ScriptableObject
{
    public const string AssetPath = CarlosPaletteImporter.Root + "Editor/Carlos_Paletas.asset";
    public CarlosPaletteId selecionada = CarlosPaletteId.LumeraAqua;
    public CarlosPaletteDefinition[] paletas;

    public static CarlosPalettes LoadOrCreate()
    {
        var result = AssetDatabase.LoadAssetAtPath<CarlosPalettes>(AssetPath);
        if (result != null) return result;
        result = CreateInstance<CarlosPalettes>();
        result.paletas = new[] {
            Preset("Solar", "E9C84A", "E86D5D", "49362E", "E7E1D6", "555A62"),
            Preset("Lumera/Aqua", "3B3533", "32A9A0", "26384B", "E5E8E3", "4D6972"),
            Preset("Ceu aventureiro", "D19A3D", "4388D1", "28384A", "E6DFCE", "5F6B75"),
            Preset("Arcade suave", "45364F", "D96991", "39405A", "E7E4E0", "6A6076")
        };
        AssetDatabase.CreateAsset(result, AssetPath);
        return result;
    }

    static CarlosPaletteDefinition Preset(string name, string hair, string shirt, string shorts, string shoes, string details) =>
        new CarlosPaletteDefinition { nome = name, cabelo = Hex(hair), camisa = Hex(shirt), shortCor = Hex(shorts), tenis = Hex(shoes), detalhesTenis = Hex(details) };

    static Color Hex(string value)
    {
        if (!ColorUtility.TryParseHtmlString("#" + value, out Color color))
            throw new ArgumentException("Cor hexadecimal invalida: " + value);
        return color;
    }

    [MenuItem("Tools/Lumera/Carlos/Editar combinacoes de cores")]
    static void SelectAsset() => Selection.activeObject = LoadOrCreate();
}

[CustomEditor(typeof(CarlosPalettes))]
public sealed class CarlosPalettesEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Escolha a combinacao, ajuste as cores e clique em Gerar e aplicar. Pele, boca, olhos e sobrancelhas usam os materiais originais.", MessageType.Info);
        DrawDefaultInspector();
        if (GUILayout.Button("Gerar e aplicar paletas ao Carlos"))
        {
            AssetDatabase.SaveAssetIfDirty(target);
            CarlosPaletteTools.Rebuild();
        }
    }
}
