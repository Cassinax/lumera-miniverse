using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Apenas Editor: o FBX original permanece intacto, sem custo extra em runtime.
public sealed class CarlosPaletteImporter : AssetPostprocessor
{
    internal const string Root = "Assets/Games/002_Jump_Force/Personagem/Carlos/";
    internal const string Model = Root + "Carlos_Unity.fbx";
    internal const string Palette = Root + "Materials/Carlos_Paleta.png";
    internal const string MaterialPath = Root + "Materials/Carlos_Paleta.mat";
    internal static readonly string[] Regions = {
        "Material_Boca", "Material_Botas", "Material_Botas_Cadarcos",
        "Material_Cabelo", "Material_Casaco", "Material_Olho",
        "Material_Pele", "Material_Short", "Material_Sombrancelhas"
    };
    public override uint GetVersion() => 3;
    public override int GetPostprocessOrder() => 100;

    void OnPreprocessTexture()
    {
        if (assetPath != Palette) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false; // Alpha guarda brilho.
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        // Uma linha por paleta, sem limite de quantidade: o tamanho maximo acompanha a altura real,
        // para nenhuma linha ser reduzida (o que misturaria as cores de paletas vizinhas).
        importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(32, AlturaPng(assetPath))), 32, 16384);
    }

    // Altura gravada no cabecalho IHDR do PNG (bytes 20 a 23).
    static int AlturaPng(string caminho)
    {
        try
        {
            using var arquivo = File.OpenRead(caminho);
            var cabecalho = new byte[24];
            if (arquivo.Read(cabecalho, 0, 24) == 24)
                return (cabecalho[20] << 24) | (cabecalho[21] << 16) | (cabecalho[22] << 8) | cabecalho[23];
        }
        catch (IOException) { }
        return 32;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != Model) return;
        context.DependsOnSourceAsset(MaterialPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) return;
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = renderer.sharedMesh;
            var sources = renderer.sharedMaterials;
            if (mesh == null || sources.Length != mesh.subMeshCount)
                throw new InvalidOperationException("Carlos: materiais e submeshes inconsistentes.");
            int[] slots = sources.Select(m => m == null ? -1 : Array.IndexOf(Regions, m.name)).ToArray();
            if (slots.Any(i => i < 0))
                throw new InvalidOperationException("Carlos: material desconhecido; revise a paleta.");
            if (mesh.blendShapeCount != 0)
                throw new InvalidOperationException("Carlos: blend shapes novos exigem revisao do conversor.");
            Merge(mesh, slots);
            renderer.sharedMaterials = new[] { material };
        }
    }

    static T[] Remap<T>(T[] values, List<int> indices) =>
        values.Length == 0 ? Array.Empty<T>() : indices.Select(i => values[i]).ToArray();

    static void Merge(Mesh mesh, int[] slots)
    {
        int originalVertices = mesh.vertexCount;
        int originalSubmeshes = mesh.subMeshCount;
        var indices = new List<int>();
        var triangles = new List<int>();
        var uv0 = new List<Vector2>();
        var remap = new Dictionary<long, int>();
        for (int sub = 0; sub < originalSubmeshes; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                throw new InvalidOperationException("Carlos: topologia diferente de triangulos.");
            foreach (int source in mesh.GetTriangles(sub))
            {
                // Se duas cores compartilham um vertice, separamos so essa fronteira.
                long key = ((long)slots[sub] << 32) | (uint)source;
                if (!remap.TryGetValue(key, out int destination))
                {
                    destination = indices.Count;
                    remap.Add(key, destination);
                    indices.Add(source);
                    uv0.Add(new Vector2((slots[sub] + 0.5f) / 16f, 0.5f));
                }
                triangles.Add(destination);
            }
        }
        var positions = Remap(mesh.vertices, indices);
        var normals = Remap(mesh.normals, indices);
        var tangents = Remap(mesh.tangents, indices);
        var colors = Remap(mesh.colors, indices);
        var bindposes = mesh.bindposes;
        var bounds = mesh.bounds;
        var uv = new List<Vector4>[8];
        var uvDimensions = new int[8];
        for (int channel = 1; channel < 8; channel++)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
            if (!mesh.HasVertexAttribute(attribute)) continue;
            uvDimensions[channel] = mesh.GetVertexAttributeDimension(attribute);
            var sourceUV = new List<Vector4>();
            mesh.GetUVs(channel, sourceUV);
            uv[channel] = indices.Select(i => sourceUV[i]).ToList();
        }
        // Nao limita os pesos a quatro influencias por vertice.
        using var originalCounts = mesh.GetBonesPerVertex();
        using var originalWeights = mesh.GetAllBoneWeights();
        int[] offsets = new int[originalVertices + 1];
        for (int i = 0; i < originalCounts.Length; i++)
            offsets[i + 1] = offsets[i] + originalCounts[i];
        var counts = new List<byte>();
        var weights = new List<BoneWeight1>();
        foreach (int source in indices)
        {
            counts.Add(originalCounts.Length == 0 ? (byte)0 : originalCounts[source]);
            for (int j = offsets[source]; j < offsets[source + 1]; j++) weights.Add(originalWeights[j]);
        }
        // Mantem a instancia/nome do Mesh e as referencias locais do FBX.
        var importedMesh = mesh;
        bool wasReadable = importedMesh.isReadable;
        mesh = new Mesh { name = importedMesh.name };
        mesh.indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = positions;
        if (normals.Length != 0) mesh.normals = normals;
        if (tangents.Length != 0) mesh.tangents = tangents;
        if (colors.Length != 0) mesh.colors = colors;
        mesh.SetUVs(0, uv0);
        for (int channel = 1; channel < 8; channel++)
        {
            if (uv[channel] == null) continue;
            if (uvDimensions[channel] == 2) mesh.SetUVs(channel, uv[channel].Select(v => new Vector2(v.x, v.y)).ToList());
            else if (uvDimensions[channel] == 3) mesh.SetUVs(channel, uv[channel].Select(v => new Vector3(v.x, v.y, v.z)).ToList());
            else mesh.SetUVs(channel, uv[channel]);
        }
        mesh.bindposes = bindposes;
        if (weights.Count != 0)
        {
            using var nativeCounts = new Unity.Collections.NativeArray<byte>(counts.ToArray(), Unity.Collections.Allocator.Temp);
            using var nativeWeights = new Unity.Collections.NativeArray<BoneWeight1>(weights.ToArray(), Unity.Collections.Allocator.Temp);
            mesh.SetBoneWeights(nativeCounts, nativeWeights);
        }
        mesh.subMeshCount = 1;
        mesh.SetTriangles(triangles, 0, false);
        mesh.bounds = bounds;
        if (mesh.vertexCount != positions.Length || mesh.GetIndexCount(0) != triangles.Count)
            throw new InvalidOperationException("Carlos: a gravacao da malha falhou.");
        EditorUtility.CopySerialized(mesh, importedMesh);
        UnityEngine.Object.DestroyImmediate(mesh);
        mesh = importedMesh;
        var serializedMesh = new SerializedObject(mesh);
        serializedMesh.FindProperty("m_IsReadable").boolValue = wasReadable;
        serializedMesh.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"Carlos paleta: {originalSubmeshes} -> 1 submesh; {originalVertices} -> {mesh.vertexCount} vertices; {triangles.Count / 3} triangulos; {bindposes.Length} bind poses preservadas.");
    }
}

public static class CarlosPaletteTools
{
    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists("Library/CarlosPalette.request")) return;
            try { Rebuild(); File.Delete("Library/CarlosPalette.request"); }
            catch (Exception ex) { Debug.LogException(ex); }
        };
    }

    [MenuItem("Tools/Lumera/Carlos/Atualizar paleta e validar")]
    public static void Rebuild()
    {
        var settings = CarlosPalettes.LoadOrCreate();
        // Quantidade livre de paletas; so precisa de pelo menos uma e de uma selecao existente.
        if (settings.paletas == null || settings.paletas.Length == 0 ||
            settings.selecionada < 0 || settings.selecionada >= settings.paletas.Length)
            throw new InvalidOperationException("Carlos: configure pelo menos uma paleta e uma selecao valida.");
        int rows = settings.paletas.Length;
        var texture = new Texture2D(16, rows, TextureFormat.RGBA32, false);
        for (int row = 0; row < rows; row++)
        {
            for (int i = 0; i < 16; i++) texture.SetPixel(i, row, Color.black);
            foreach (string region in CarlosPaletteImporter.Regions)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>(CarlosPaletteImporter.Root + "Materials/" + region + ".mat");
                if (source == null || source.shader.name != "Universal Render Pipeline/Simple Lit" ||
                    source.GetTexture("_BaseMap") != null || source.GetFloat("_Surface") != 0 ||
                    source.GetFloat("_AlphaClip") != 0 || source.GetFloat("_SpecularHighlights") != 1)
                    throw new InvalidOperationException("Paleta requer Simple Lit opaco, sem textura e sem reflexo especular: " + region);
                Color color = settings.paletas[row].RegionColor(region, source.GetColor("_BaseColor"));
                color.a = source.GetFloat("_Smoothness");
                texture.SetPixel(Array.IndexOf(CarlosPaletteImporter.Regions, region), row, color);
            }
        }
        texture.Apply();
        File.WriteAllBytes(CarlosPaletteImporter.Palette, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(CarlosPaletteImporter.Palette, ImportAssetOptions.ForceSynchronousImport);
        var template = AssetDatabase.LoadAssetAtPath<Material>(CarlosPaletteImporter.Root + "Materials/Material_Boca.mat");
        var material = AssetDatabase.LoadAssetAtPath<Material>(CarlosPaletteImporter.MaterialPath);
        if (material == null)
        {
            material = new Material(template);
            AssetDatabase.CreateAsset(material, CarlosPaletteImporter.MaterialPath);
        }
        material.CopyPropertiesFromMaterial(template);
        material.name = "Carlos_Paleta";
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CarlosPaletteImporter.Palette));
        material.SetColor("_BaseColor", Color.white);
        material.SetTextureScale("_BaseMap", new Vector2(1f, 1f / rows));
        material.SetTextureOffset("_BaseMap", new Vector2(0f, settings.selecionada / (float)rows));
        material.SetFloat("_SpecularHighlights", 1);
        material.SetFloat("_Smoothness", 1);
        material.SetFloat("_SmoothnessSource", 1);
        material.DisableKeyword("_SPECULAR_COLOR");
        material.DisableKeyword("_SPECGLOSSMAP");
        material.DisableKeyword("_GLOSSINESS_FROM_BASE_ALPHA");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        AssetDatabase.ImportAsset(CarlosPaletteImporter.Model, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        UpdateWardrobeCatalog();
        Validate();
    }

    public static Lumera.JumpForce.JumpForcePaletteCatalog UpdateWardrobeCatalog()
    {
        const string path = CarlosPaletteImporter.Root + "Paletas_Vestiario.asset";
        var settings = CarlosPalettes.LoadOrCreate();
        var catalog = AssetDatabase.LoadAssetAtPath<Lumera.JumpForce.JumpForcePaletteCatalog>(path);
        if (!catalog)
        {
            catalog = ScriptableObject.CreateInstance<Lumera.JumpForce.JumpForcePaletteCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }
        catalog.options = settings.paletas.Select(p => new Lumera.JumpForce.JumpForcePaletteCatalog.Option { name = p.nome, swatch = p.camisa }).ToArray();
        catalog.defaultIndex = settings.selecionada;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        return catalog;
    }

    public static void Validate()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CarlosPaletteImporter.Model);
        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length != 1 || renderers.Any(r => r.sharedMaterials.Length != 1 || r.sharedMesh.subMeshCount != 1))
            throw new InvalidOperationException("Carlos: esperado um renderer, um material e um submesh.");
        if (renderers[0].bones.Length != renderers[0].sharedMesh.bindposes.Length || renderers[0].rootBone == null)
            throw new InvalidOperationException("Carlos: bones e bind poses nao correspondem; desative Optimize Game Objects no importador.");
        var importer = (ModelImporter)AssetImporter.GetAtPath(CarlosPaletteImporter.Model);
        if (importer.animationType != ModelImporterAnimationType.Generic)
            throw new InvalidOperationException("Carlos: o rig deve continuar Generic.");
        var mesh = renderers[0].sharedMesh;
        if (mesh.vertexCount == 0 || mesh.GetIndexCount(0) == 0)
            throw new InvalidOperationException("Carlos: malha vazia depois da importacao.");
        var clips = AssetDatabase.LoadAllAssetsAtPath(CarlosPaletteImporter.Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        string report = $"Unity: {Application.unityVersion}\nRig: {importer.animationType}\nRenderers: {renderers.Length}\nMateriais: 1\nSubmeshes: {mesh.subMeshCount}\nVertices: {mesh.vertexCount}\nTriangulos: {mesh.GetIndexCount(0) / 3}\nClipes: {string.Join(", ", clips.Select(c => c.name + " (" + c.length + " s)"))}\n";
        File.WriteAllText("Library/CarlosPalette-validation.txt", report);
        Debug.Log(report);
    }
}

