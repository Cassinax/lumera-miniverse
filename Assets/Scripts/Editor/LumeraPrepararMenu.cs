using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Lumera > Preparar Menu e Graficos: liga os scripts da plataforma na cena do Menu, cria os quatro niveis
// graficos e o card do Jump Force. Pode ser executado de novo: so completa o que faltar e reaplica os
// presets. Tambem roda em batchmode: -executeMethod LumeraPrepararMenu.Executar
public static class LumeraPrepararMenu
{
    const string CaminhoCena = "Assets/Games/001_Menu/Menu.unity";
    const string PastaGraficos = "Assets/Settings/Graficos";
    const string AssetBaseUrp = "Assets/Settings/Mobile_RPAsset.asset";
    const string CaminhoJogoJumpForce = "Assets/Games/002_Jump_Force/JumpForce_Jogo.asset";

    // Ordem = indice do nivel grafico (Baixo, Medio, Alto, Ultra).
    struct Preset
    {
        public string nome;
        public float escalaRender, distanciaSombra, lodBias;
        public int msaa, cascatas, resolucaoSombra, limiteMipmap;
        public bool sombras, sombrasSuaves, hdr;
    }

    static readonly Preset[] Presets =
    {
        new Preset { nome = "Baixo", escalaRender = 0.7f, msaa = 1, sombras = false, distanciaSombra = 20, cascatas = 1,
            resolucaoSombra = 512, sombrasSuaves = false, hdr = false, limiteMipmap = 1, lodBias = 0.5f },
        new Preset { nome = "Medio", escalaRender = 0.85f, msaa = 1, sombras = true, distanciaSombra = 30, cascatas = 1,
            resolucaoSombra = 1024, sombrasSuaves = false, hdr = false, limiteMipmap = 0, lodBias = 1 },
        new Preset { nome = "Alto", escalaRender = 1, msaa = 2, sombras = true, distanciaSombra = 45, cascatas = 2,
            resolucaoSombra = 2048, sombrasSuaves = true, hdr = true, limiteMipmap = 0, lodBias = 1.5f },
        new Preset { nome = "Ultra", escalaRender = 1, msaa = 4, sombras = true, distanciaSombra = 60, cascatas = 4,
            resolucaoSombra = 2048, sombrasSuaves = true, hdr = true, limiteMipmap = 0, lodBias = 2 },
    };

    [MenuItem("Lumera/Preparar Menu e Graficos")]
    public static void Executar()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var relatorio = new StringBuilder("[Lumera] Preparar Menu e Graficos\n");
        PrepararGraficos(relatorio);
        PrepararJogoJumpForce(relatorio);
        var cena = EditorSceneManager.OpenScene(CaminhoCena, OpenSceneMode.Single);
        // Abrir a cena descarta da memoria o asset recem-criado: carrega de novo do disco.
        var jogo = AssetDatabase.LoadAssetAtPath<JogoLumera>(CaminhoJogoJumpForce);
        PrepararCena(cena, jogo, relatorio);
        if (jogo) EditorUtility.SetDirty(jogo);
        EditorSceneManager.MarkSceneDirty(cena);
        EditorSceneManager.SaveScene(cena);
        AssetDatabase.SaveAssets();
        Debug.Log(relatorio.ToString());
    }

    //---------- Graficos

    static void PrepararGraficos(StringBuilder relatorio)
    {
        if (!AssetDatabase.IsValidFolder(PastaGraficos)) AssetDatabase.CreateFolder("Assets/Settings", "Graficos");
        var assets = new Object[Presets.Length];
        for (int i = 0; i < Presets.Length; i++)
        {
            string caminho = $"{PastaGraficos}/Lumera_{Presets[i].nome}_RPAsset.asset";
            if (!AssetDatabase.LoadMainAssetAtPath(caminho) && !AssetDatabase.CopyAsset(AssetBaseUrp, caminho))
            {
                relatorio.AppendLine($"ERRO: nao foi possivel copiar {AssetBaseUrp} para {caminho}.");
                return;
            }
            assets[i] = AssetDatabase.LoadMainAssetAtPath(caminho);
            AjustarAssetUrp(assets[i], Presets[i]);
        }

        var qualidade = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var niveis = qualidade.FindProperty("m_QualitySettings");
        // Parte do nivel "Mobile" do template do URP; os quatro presets substituem Mobile e PC.
        int baseIndice = 0;
        for (int i = 0; i < niveis.arraySize; i++)
            if (niveis.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "Mobile") baseIndice = i;
        if (baseIndice != 0) niveis.MoveArrayElement(baseIndice, 0);
        bool jaPreparado = niveis.arraySize == Presets.Length &&
            Enumerable.Range(0, Presets.Length).All(i => niveis.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == Presets[i].nome);
        if (!jaPreparado)
        {
            niveis.arraySize = 1;
            while (niveis.arraySize < Presets.Length) niveis.InsertArrayElementAtIndex(0);
        }
        for (int i = 0; i < Presets.Length; i++)
        {
            var nivel = niveis.GetArrayElementAtIndex(i);
            nivel.FindPropertyRelative("name").stringValue = Presets[i].nome;
            nivel.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
            nivel.FindPropertyRelative("excludedTargetPlatforms").arraySize = 0;
            nivel.FindPropertyRelative("vSyncCount").intValue = 0;
            nivel.FindPropertyRelative("antiAliasing").intValue = 0; // O MSAA e do asset do URP.
            nivel.FindPropertyRelative("lodBias").floatValue = Presets[i].lodBias;
            nivel.FindPropertyRelative("globalTextureMipmapLimit").intValue = Presets[i].limiteMipmap;
        }
        // Padrao Medio em todas as plataformas.
        int medio = ControladorGraficos.IndiceDoNome(SaveAdapter.QUALIDADE_GRAFICA_PADRAO);
        var padroes = qualidade.FindProperty("m_PerPlatformDefaultQuality");
        if (padroes != null && padroes.isArray)
            for (int i = 0; i < padroes.arraySize; i++)
            {
                var valor = padroes.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                if (valor != null) valor.intValue = medio;
            }
        qualidade.FindProperty("m_CurrentQuality").intValue = medio;
        qualidade.ApplyModifiedPropertiesWithoutUndo();
        relatorio.AppendLine("Graficos: niveis Baixo, Medio, Alto e Ultra em Project Settings > Quality, padrao Medio; assets em " + PastaGraficos + ".");
    }

    static void AjustarAssetUrp(Object asset, Preset preset)
    {
        var so = new SerializedObject(asset);
        void Float(string campo, float valor) { var p = so.FindProperty(campo); if (p != null) p.floatValue = valor; }
        void Int(string campo, int valor) { var p = so.FindProperty(campo); if (p != null) p.intValue = valor; }
        void Bool(string campo, bool valor) { var p = so.FindProperty(campo); if (p != null) p.boolValue = valor; }
        Float("m_RenderScale", preset.escalaRender);
        Int("m_MSAA", preset.msaa);
        Bool("m_SupportsHDR", preset.hdr);
        Bool("m_MainLightShadowsSupported", preset.sombras);
        Int("m_MainLightShadowmapResolution", preset.resolucaoSombra);
        Float("m_ShadowDistance", preset.distanciaSombra);
        Int("m_ShadowCascadeCount", preset.cascatas);
        Bool("m_SoftShadowsSupported", preset.sombrasSuaves);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    //---------- Catalogo

    static void PrepararJogoJumpForce(StringBuilder relatorio)
    {
        var jogo = AssetDatabase.LoadAssetAtPath<JogoLumera>(CaminhoJogoJumpForce);
        if (jogo) { relatorio.AppendLine("Catalogo: " + CaminhoJogoJumpForce + " ja existia (mantido)."); return; }
        jogo = ScriptableObject.CreateInstance<JogoLumera>();
        jogo.id = "002_Jump_Force";
        jogo.cena = "Jump_Force";
        jogo.nome = new TextoLocalizado("Jump Force", "Jump Force", "Jump Force");
        jogo.ordem = 1;
        jogo.preco = 0;
        AssetDatabase.CreateAsset(jogo, CaminhoJogoJumpForce);
        AssetDatabase.SaveAssets();
        relatorio.AppendLine("Catalogo: criado " + CaminhoJogoJumpForce + ".");
    }

    //---------- Cena do Menu

    static void PrepararCena(Scene cena, JogoLumera jogo, StringBuilder relatorio)
    {
        // Objeto Mestre e seus controladores (um filho por controlador).
        var mestre = Achar(cena, "Objeto Mestre", relatorio);
        if (mestre)
        {
            var objetoMestre = Garantir<ObjetoMestre>(mestre.gameObject);
            Definir(objetoMestre, "painelCarregando", Achar(cena, "Objeto Mestre/Canvas/Panel_Carregando", relatorio)?.gameObject);
            Definir(objetoMestre, "textoCarregando", Componente<TMP_Text>(cena, "Objeto Mestre/Canvas/Panel_Carregando/Text_Carregando", relatorio));
            Garantir<SaveAdapter>(Filho(mestre, "Save_Adapter").gameObject);
            Garantir<ControladorAudio>(Filho(mestre, "Audio_Controller").gameObject);
            Garantir<ControladorVibracao>(Filho(mestre, "Vibracao_Controller").gameObject);
            Garantir<ControladorGraficos>(Filho(mestre, "Graficos_Controller").gameObject);
            Garantir<MonitorDesempenho>(Filho(mestre, "Monitor_Desempenho").gameObject);
            // O painel de carregamento fica por cima de qualquer Canvas das cenas.
            var canvas = mestre.Find("Canvas")?.GetComponent<Canvas>();
            if (canvas && canvas.sortingOrder < 1000) canvas.sortingOrder = 1000;
            relatorio.AppendLine("Objeto Mestre: ObjetoMestre, Save_Adapter, Audio_Controller, Vibracao_Controller, Graficos_Controller e Monitor_Desempenho.");
        }

        // Cards de jogos.
        const string conteudo = "Canvas/Area_Cards/Scroll View/Viewport/Content";
        var area = Achar(cena, "Canvas/Area_Cards", relatorio);
        var modelo = Achar(cena, conteudo + "/Card_Tamplete", relatorio);
        if (area && modelo)
        {
            var card = Garantir<CardJogo>(modelo.gameObject);
            var capa = modelo.Find("Image_Capa")?.GetComponent<Image>();
            Definir(card, "capa", capa);
            Definir(card, "nome", modelo.Find("Interativo/Text_Nome")?.GetComponent<TMP_Text>());
            Definir(card, "preco", modelo.Find("Preco")?.gameObject);
            Definir(card, "textoPreco", modelo.Find("Preco/Text (TMP)")?.GetComponent<TMP_Text>());
            Definir(card, "botaoDecorativo", modelo.Find("Interativo/Button_Jogar")?.GetComponent<Button>());
            var botao = modelo.GetComponent<Button>();
            if (botao && capa) { botao.targetGraphic = capa; botao.transition = Selectable.Transition.ColorTint; }

            var menu = Garantir<MenuCards>(area.gameObject);
            Definir(menu, "conteudo", Achar(cena, conteudo, relatorio));
            Definir(menu, "modelo", card);
            Definir(menu, "cardEmBreve", Achar(cena, conteudo + "/Card_EmBreve", relatorio));
            var so = new SerializedObject(menu);
            var lista = so.FindProperty("jogos");
            // Remove entradas vazias (ex.: referencia perdida numa execucao anterior) e garante o Jump Force.
            for (int i = lista.arraySize - 1; i >= 0; i--)
                if (!lista.GetArrayElementAtIndex(i).objectReferenceValue) lista.DeleteArrayElementAtIndex(i);
            bool presente = Enumerable.Range(0, lista.arraySize).Any(i => lista.GetArrayElementAtIndex(i).objectReferenceValue == jogo);
            if (jogo && !presente)
            {
                lista.arraySize++;
                lista.GetArrayElementAtIndex(lista.arraySize - 1).objectReferenceValue = jogo;
            }
            else if (!jogo) relatorio.AppendLine("AVISO: " + CaminhoJogoJumpForce + " nao carregou; lista de jogos sem o Jump Force.");
            so.ApplyModifiedPropertiesWithoutUndo();
            relatorio.AppendLine("Cards: MenuCards em Area_Cards, CardJogo no Card_Tamplete (tint na capa, Button_Jogar decorativo).");
        }

        // Configuracoes.
        const string painel = "Canvas/Painel_Configuracoes";
        const string itens = painel + "/Scroll_View_Configuracoes/Viewport/Content";
        var painelConfig = Achar(cena, painel, relatorio);
        if (painelConfig)
        {
            var config = Garantir<MenuConfiguracoes>(painelConfig.gameObject);
            Definir(config, "volumeGeral", Componente<Slider>(cena, itens + "/Volume_Geral/Slider", relatorio));
            Definir(config, "porcentagemGeral", Componente<TMP_Text>(cena, itens + "/Volume_Geral/Text_Porcentagem", relatorio));
            Definir(config, "volumeEfeitos", Componente<Slider>(cena, itens + "/Volume_Efeitos/Slider", relatorio));
            Definir(config, "porcentagemEfeitos", Componente<TMP_Text>(cena, itens + "/Volume_Efeitos/Text_Porcentagem", relatorio));
            Definir(config, "vibracao", Componente<Toggle>(cena, itens + "/Ligar_Vibracao/Toggle", relatorio));
            Definir(config, "idioma", Componente<TMP_Dropdown>(cena, itens + "/Idioma/Dropdown", relatorio));
            Definir(config, "graficos", Componente<TMP_Dropdown>(cena, itens + "/Graficos/Dropdown", relatorio));
            Definir(config, "ajusteAutomaticoGraficos", Componente<Toggle>(cena, itens + "/Auto_Ajuste_Graficos/Toggle", relatorio));
            Definir(config, "botaoPoliticas", Componente<Button>(cena, itens + "/Politicas/Button", relatorio));
            Definir(config, "botaoTermos", Componente<Button>(cena, itens + "/Termos/Button", relatorio));
            Definir(config, "botaoApagarDados", Componente<Button>(cena, itens + "/Apagar_Dados/Button", relatorio));
            Definir(config, "painelApagarDados", Achar(cena, itens + "/Painel_Apagar_Dados", relatorio)?.gameObject);
            Definir(config, "confirmarApagar", Componente<Button>(cena, itens + "/Painel_Apagar_Dados/Button_Confirmar", relatorio));
            Definir(config, "cancelarApagar", Componente<Button>(cena, itens + "/Painel_Apagar_Dados/Button_Cancelar", relatorio));
            Definir(config, "botaoVoltar", Componente<Button>(cena, painel + "/Button_Voltar", relatorio));

            // O painel comeca inativo: o botao do topo chama Abrir.
            var abrir = Componente<Button>(cena, "Canvas/Painel_Topo/Button_Configuracoes", relatorio);
            if (abrir)
            {
                bool ligado = Enumerable.Range(0, abrir.onClick.GetPersistentEventCount())
                    .Any(i => abrir.onClick.GetPersistentTarget(i) == config && abrir.onClick.GetPersistentMethodName(i) == nameof(MenuConfiguracoes.Abrir));
                if (!ligado) UnityEventTools.AddPersistentListener(abrir.onClick, config.Abrir);
                EditorUtility.SetDirty(abrir);
            }
            relatorio.AppendLine("Configuracoes: MenuConfiguracoes em Painel_Configuracoes; Button_Configuracoes abre o painel.");
        }
    }

    //---------- Utilitarios

    static Transform Achar(Scene cena, string caminho, StringBuilder relatorio)
    {
        string[] partes = caminho.Split(new[] { '/' }, 2);
        var raiz = cena.GetRootGameObjects().FirstOrDefault(g => g.name == partes[0]);
        var alvo = raiz ? (partes.Length > 1 ? raiz.transform.Find(partes[1]) : raiz.transform) : null;
        if (!alvo) relatorio.AppendLine("AVISO: nao encontrado na cena: " + caminho);
        return alvo;
    }

    static T Componente<T>(Scene cena, string caminho, StringBuilder relatorio) where T : Component
    {
        var alvo = Achar(cena, caminho, relatorio);
        var componente = alvo ? alvo.GetComponent<T>() : null;
        if (alvo && !componente) relatorio.AppendLine($"AVISO: {caminho} nao tem {typeof(T).Name}.");
        return componente;
    }

    static Transform Filho(Transform pai, string nome)
    {
        var filho = pai.Find(nome);
        if (filho) return filho;
        var novo = new GameObject(nome);
        novo.transform.SetParent(pai, false);
        return novo.transform;
    }

    static T Garantir<T>(GameObject alvo) where T : Component
    {
        var componente = alvo.GetComponent<T>();
        return componente ? componente : alvo.AddComponent<T>();
    }

    static void Definir(Component componente, string campo, Object valor)
    {
        if (!componente || !valor) return;
        var so = new SerializedObject(componente);
        var propriedade = so.FindProperty(campo);
        if (propriedade == null) { Debug.LogWarning($"[Lumera] Campo {campo} nao existe em {componente.GetType().Name}."); return; }
        propriedade.objectReferenceValue = valor;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
