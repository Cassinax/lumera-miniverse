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

    // Carteira da plataforma: progresso (SLG0002), vale para todos os jogos. Ganha na loja ou na gameplay;
    // cada jogo cobra a entrada da partida ao ser aberto.
    public const string CMD_PROGRESSO = "SLG0002";
    public const string CHAVE_MOEDAS = "moedas";

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
        new DadoPadrao(CMD_PROGRESSO, CHAVE_MOEDAS, "0", PriorityLevel.Alto),
    };

    //---------- Moedas

    public event Action<long> MoedasAlteradas;

    public long ObterMoedas()
    {
        try
        {
            string valor = ObterDado(CMD_PROGRESSO, CHAVE_MOEDAS);
            return long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out long saldo) ? Math.Max(0, saldo) : 0;
        }
        catch (Exception) { return 0; }
    }

    // origem: "loja", "gameplay", "anuncio"... referencia: id do jogo, produto ou anuncio.
    public bool AdicionarMoedas(long quantidade, string origem, string referencia = "")
    {
        if (quantidade <= 0) return false;
        long antes = ObterMoedas();
        return AlterarMoedas(antes + quantidade, () => RegistrarGanho(CHAVE_MOEDAS, quantidade, antes, antes + quantidade, origem, referencia));
    }

    // Falso sem saldo suficiente; nada e debitado nesse caso.
    public bool GastarMoedas(long quantidade, string origem, string referencia = "")
    {
        if (quantidade <= 0) return true;
        long antes = ObterMoedas();
        if (antes < quantidade) return false;
        return AlterarMoedas(antes - quantidade, () => RegistrarGasto(CHAVE_MOEDAS, quantidade, antes, antes - quantidade, origem, referencia));
    }

    // Saldo e auditoria na mesma transacao: ou os dois ficam, ou nenhum.
    private bool AlterarMoedas(long depois, Action auditoria)
    {
        bool ok;
        try
        {
            Begin();
            SalvarDados(CMD_PROGRESSO, CHAVE_MOEDAS, depois.ToString(CultureInfo.InvariantCulture));
            auditoria();
            ok = Commit();
            if (!ok) Rollback();
        }
        catch (Exception)
        {
            Rollback();
            ok = false;
        }
        if (ok) MoedasAlteradas?.Invoke(depois);
        return ok;
    }

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

    // Cada jogo grava sob o proprio comando: [JOGO_<ID>],[chave] (SaveSystem.GameCommand, do pacote). Assim a
    // lista de saves sabe quais jogos tem dados e consegue apagar so os de um. O id e o do JogoLumera (fixo
    // depois de publicado). Essas chaves sao progresso (viajam para a nuvem): ver Escopo em SaveAdapter.cs.
    public string ObterDadoJogo(string idJogo, string chave, string padrao)
    {
        try
        {
            string valor = ObterDado(SaveSystem.GameCommand(idJogo), chave);
            return valor == null || valor == SemDado ? padrao : valor;
        }
        catch (Exception) { return padrao; }
    }

    public void SalvarDadoJogo(string idJogo, string chave, string valor)
    {
        try { SalvarDados(SaveSystem.GameCommand(idJogo), chave, valor ?? ""); }
        catch (Exception) { }
    }

    // Debito e liberacao do item na mesma transacao. Repetir uma compra nao cobra de novo.
    public bool ComprarItemJogo(string idJogo, string chaveItem, long preco)
    {
        if (string.IsNullOrWhiteSpace(idJogo) || string.IsNullOrEmpty(chaveItem) || preco < 0) return false;
        bool iniciou = false;
        long depois;
        try
        {
            GarantirInicializacao();
            if (_falhaPersistencia) return false;
            _saveSystem.ProcessQueues();
            if (_saveSystem.HasPendingOperations()) return false;
            if (ObterDadoJogo(idJogo, chaveItem, "0") == "1") return true;
            long antes = ObterMoedas();
            if (antes < preco) return false;
            depois = antes - preco;
            Begin();
            iniciou = true;
            SalvarDados(CMD_PROGRESSO, CHAVE_MOEDAS, depois.ToString(CultureInfo.InvariantCulture));
            SalvarDados(SaveSystem.GameCommand(idJogo), chaveItem, "1");
            RegistrarGasto(CHAVE_MOEDAS, preco, antes, depois, "skin", idJogo + "/" + chaveItem);
            if (!Commit()) { Rollback(); return false; }
        }
        catch (Exception)
        {
            if (iniciou) Rollback();
            return false;
        }
        MoedasAlteradas?.Invoke(depois);
        return true;
    }

    // O pacote le o estado confirmado, nao a fila: grava antes de consultar.
    public bool TemDadosDoJogo(string idJogo)
    {
        if (string.IsNullOrWhiteSpace(idJogo)) return false;
        try
        {
            GarantirInicializacao();
            SalvarAgora();
            return _saveSystem.HasGameData(idJogo);
        }
        catch (Exception) { return false; }
    }

    // So os dados do jogo: a carteira comum e as preferencias do aparelho nao sao tocadas.
    public bool ApagarDadosDoJogo(string idJogo)
    {
        if (!TemDadosDoJogo(idJogo)) return true;
        try
        {
            _saveSystem.EnqueueRemoveGameData(idJogo);
            _saveSystem.ProcessQueues();
            _saveSystem.ForceSave();
            return !_saveSystem.HasGameData(idJogo);
        }
        catch (Exception) { return false; }
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
