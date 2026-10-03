using System;
using System.Collections.Generic;
using System.Globalization;
using cassinax.savesystem;

// Lumera Miniverse: chaves, padroes e acessos tipados da plataforma.
// O template (SaveAdapter.cs) continua generico; regras do Lumera ficam aqui.
public partial class SaveAdapter
{
    // Configuracoes do aparelho. Prefixo sem regra em _escopos = preferencia local: nao viaja para a
    // nuvem e e preservado numa restauracao normal (Biblioteca, Sistema de save: idioma e volumes ficam locais).
    public const string CMD_CONFIG = "CONFIG";
    public const string CHAVE_VOLUME_GERAL = "volume_geral";
    public const string CHAVE_VOLUME_EFEITOS = "volume_efeitos";
    public const string CHAVE_VIBRACAO = "vibracao";
    // Codigo ISO (pt, en, es, hi). Vazio = detectar pelo idioma do aparelho.
    public const string CHAVE_IDIOMA = "idioma";
    // Nome do nivel em Project Settings > Quality, nunca o indice: a ordem pode mudar entre builds.
    public const string CHAVE_QUALIDADE_GRAFICA = "qualidade_grafica";
    public const string CHAVE_AJUSTE_AUTOMATICO_GRAFICO = "ajuste_automatico_grafico";
    public const string QUALIDADE_GRAFICA_PADRAO = "Medio";

    // Valor que ObterDado devolve quando a chave nao existe.
    private const string SemDado = "BuscarDado erro";

    private static List<DadoPadrao> DadosPadraoLumera() => new List<DadoPadrao>
    {
        new DadoPadrao(CMD_CONFIG, CHAVE_VOLUME_GERAL, "1", PriorityLevel.Normal),
        new DadoPadrao(CMD_CONFIG, CHAVE_VOLUME_EFEITOS, "1", PriorityLevel.Normal),
        new DadoPadrao(CMD_CONFIG, CHAVE_VIBRACAO, "1", PriorityLevel.Normal),
        new DadoPadrao(CMD_CONFIG, CHAVE_IDIOMA, "", PriorityLevel.Normal),
        new DadoPadrao(CMD_CONFIG, CHAVE_QUALIDADE_GRAFICA, QUALIDADE_GRAFICA_PADRAO, PriorityLevel.Normal),
        new DadoPadrao(CMD_CONFIG, CHAVE_AJUSTE_AUTOMATICO_GRAFICO, "1", PriorityLevel.Normal),
    };

    //---------- Configuracoes

    // Leituras nunca derrubam o jogo: sem save utilizavel, valem os padroes.
    public string ObterTextoConfig(string chave, string padrao)
    {
        try
        {
            string valor = ObterDado(CMD_CONFIG, chave);
            return valor == null || valor == SemDado ? padrao : valor;
        }
        catch (SaveValidationException) { return padrao; }
    }

    public float ObterFloatConfig(string chave, float padrao) =>
        float.TryParse(ObterTextoConfig(chave, null), NumberStyles.Float, CultureInfo.InvariantCulture, out float valor) ? valor : padrao;

    public bool ObterBoolConfig(string chave, bool padrao)
    {
        string valor = ObterTextoConfig(chave, null);
        return valor == "1" || (valor != "0" && padrao);
    }

    public void SalvarConfig(string chave, string valor)
    {
        try { SalvarDados(CMD_CONFIG, chave, valor ?? ""); }
        catch (SaveValidationException) { }
    }

    public void SalvarConfig(string chave, float valor) =>
        SalvarConfig(chave, valor.ToString("0.###", CultureInfo.InvariantCulture));

    public void SalvarConfig(string chave, bool valor) => SalvarConfig(chave, valor ? "1" : "0");

    //---------- Dados de cada jogo

    // Cada jogo grava sob o proprio comando: [JOGO_<ID>],[chave]. Assim a lista de saves sabe quais jogos
    // tem dados e consegue apagar so os de um. O id e o do JogoLumera (fixo depois de publicado).
    public static string ComandoDoJogo(string idJogo) => "JOGO_" + (idJogo ?? "").Trim().ToUpperInvariant();

    public string ObterDadoJogo(string idJogo, string chave, string padrao)
    {
        try
        {
            string valor = ObterDado(ComandoDoJogo(idJogo), chave);
            return valor == null || valor == SemDado ? padrao : valor;
        }
        catch (Exception) { return padrao; }
    }

    public void SalvarDadoJogo(string idJogo, string chave, string valor)
    {
        try { SalvarDados(ComandoDoJogo(idJogo), chave, valor ?? ""); }
        catch (Exception) { }
    }

    public bool TemDadosDoJogo(string idJogo) => ChavesDoJogo(idJogo).Count > 0;

    public bool ApagarDadosDoJogo(string idJogo)
    {
        var chaves = ChavesDoJogo(idJogo);
        if (chaves.Count == 0) return true;
        try
        {
            foreach (string chave in chaves) _saveSystem.EnqueueRemove(chave, PriorityLevel.Alto);
            _saveSystem.ProcessQueues();
            _saveSystem.ForceSave();
            return !TemDadosDoJogo(idJogo);
        }
        catch (Exception) { return false; }
    }

    // O snapshot le o ultimo commit, nao a fila: grava antes de procurar.
    private List<string> ChavesDoJogo(string idJogo)
    {
        var chaves = new List<string>();
        if (string.IsNullOrWhiteSpace(idJogo)) return chaves;
        try
        {
            GarantirInicializacao();
            SalvarAgora();
            string prefixo = "[" + ComandoDoJogo(idJogo) + "],";
            foreach (var par in CapturarSnapshot().Data)
                if (par.Key.StartsWith(prefixo, StringComparison.Ordinal)) chaves.Add(par.Key);
        }
        catch (Exception) { }
        return chaves;
    }

    //---------- Apagar dados

    // "Apagar dados" das configuracoes. O Lumera e local e sem conta: apaga fisicamente o save e grava os
    // padroes de novo (configuracoes inclusive, como pede a Biblioteca: reset reinicia os padroes).
    // Com conta ou nuvem ativas, o caminho certo e SolicitarReset; aqui recusamos para nao perder o vinculo.
    public bool RestaurarPadroesDeFabrica()
    {
        try
        {
            GarantirInicializacao();
            if (_politicaReset == ResetPolicy.RemoteFirst || !string.IsNullOrEmpty(CapturarSnapshot().Identity))
                return false;
            ApagarTodosDados();
            AplicarPadroes();
            GarantirContaPadrao();
            _saveSystem.ProcessQueues();
            _saveSystem.ForceSave();
            return true;
        }
        catch (Exception) { return false; }
    }
}
