# Cassinax Unity System Save 1.0.0 - Referencia

Piloto de integracao. Codec 2. Atualizacao: 2026-09-09.
O template e uma fachada adaptavel, sem regras de um jogo ou servidor proprio
obrigatorio. Auditoria analitica pertence ao programa externo.

## Configuracao minima

Adicione SaveAdapter e defina _idJogo, _chaveCriptografia de 16/24/32 bytes UTF-8,
_versaoEstruturaSave e _dadosPadrao. Nao publique placeholders nem troque ID/chave
de um jogo publicado. Ative apenas os recursos utilizados. O padrao e local.

Em _escopos declare prefixos internos como [COMANDO],[. O prefixo mais longo vence.
[SLG0002], representa progresso e [SLG0200], auditoria por padrao. Desconhecidos sao
preferencias locais. Campos conhecidos de SLG0001, CUSS e identidade SLG0003 sao
reservados. Configuracoes adicionais em SLG0001 seguem a regra declarada:
nao desaparecem porque compartilham o grupo do cabecalho.

Window > Cassinax > Exemplo de Save Local cria objeto com ID e chave de
exemplo. Em Play Mode, Commit example checkpoint grava um checkpoint sem internet.

## Transacoes e leitura

~~~csharp
adapter.Begin();
adapter.SalvarDados("SLG0002", "checkpoint", "3");
if (!adapter.Commit())
    adapter.Rollback();
~~~

Begin/Commit/Rollback usam staging. Eventos ocorrem depois da persistencia aceita.
Falha mantem o estado anterior em memoria. Numa interrupcao no limite do commit,
o disco pode conter a versao anterior ou a nova, sempre submetida a validacao.

SalvarAgora processa filas. ExportarSaveCompleto, CapturarSnapshot e
ExportarParaNuvem leem o ultimo commit, nao a fila. Nao geram revisao, data ou backup.
CriarBackupManual e explicito. Nao deixe filas pendentes ao iniciar sincronizacao.

ProgressoAplicado serve para atualizar visualizacao/contratos, nunca para creditar
novamente uma recompensa. ReconciliarComprasDaPlataforma registra posse comprovada,
sem premios. O jogo verifica recibos, escopo de SLG0100 e regras de consumiveis.

## Validar e aplicar

~~~csharp
if (adapter.ValidarSnapshot(payload, out var snapshot, out var erro))
    adapter.AplicarSnapshotTransacional(snapshot, SaveImportMode.ReplaceSnapshot);
~~~

No SaveSystem, TryValidateSnapshot(payload, gameId, schema, out snapshot, out erro)
retorna leitura imutavel de dados, identidade, versoes, revisao, parent, base local,
epoca e tombstone apos validar integridade. HMAC invalido, outro jogo, schema/core
futuro, duplicatas e limites excedidos sao recusados. Um snapshot emitido por uma
instancia nao pode ser fabricado nem aplicado em outra sem nova validacao.

ReplaceSnapshot remove progresso ausente e preserva preferencias locais.
Merge e explicito, sobrescreve campos e nao soma saldos. Rejeita outra conta/epoca.
Export filtrado nao pode substituir snapshot completo.

| Contrato | Progresso | Preferencias locais | Identidade | Auditoria |
| --- | --- | --- | --- | --- |
| Manual completo | Sim | Nao | Campos de identidade | Sim |
| Cloud | Sim | Nao | Somente accountHash pseudonimo | Sim |
| Backup | Sim | Sim | Sim | Sim |

Metadados de formato acompanham os contratos. CUSSL, a base sincronizada local,
nao vai para nuvem. Completo significa completo dentro do contrato manual.
Para restaurar preferencias de backup use explicitamente
SaveSystem.AplicarSnapshotTransacional com restoreLocalPreferences: true;
essa opcao exige finalidade Backup. A restauracao padrao preserva locais.

TryPreviewPlainText esta obsoleto e restrito a diagnostico validado. Nao extraia
ID/conta por regex de dados apenas descriptografados para considera-los validos.

## Codec e migracoes

Codec 2: marcador CUSS2 seguido de pares base64(chave):base64(valor), em UTF-8.
Envelope [2]base64(IV + AES-CBC).HMAC-SHA256. O HMAC cobre prefixo de versao e
cifra; a verificacao antecede descompressao. GZip e detectado pela assinatura;
GZip invalido nunca recai para texto bruto.

JSON, Unicode, colchetes, vazio e multiplas linhas fazem round-trip.
Limites padrao: payload 4 MiB, texto descomprimido 8 MiB, 20.000 entradas,
chave 1 KiB, valor 256 KiB. Ajuste SaveLimits a quota do jogo/provedor.
O codec 1 e lido estritamente. Conteudo ja truncado pelo formato antigo nao pode
ser reconstruido. Diretivas antigas de remocao nao sao snapshots: migre esses
patches separadamente, sem executar comandos externos como dados.

~~~csharp
protected override void RegistrarMigracoes(SaveSystem system)
{
    system.RegisterMigration(1, input =>
    {
        var output = input.ToDictionary(p => p.Key, p => p.Value);
        if (output.TryGetValue("[SLG0002],[oldCheckpoint]", out var value))
        {
            output["[SLG0002],[checkpoint]"] = value;
            output.Remove("[SLG0002],[oldCheckpoint]");
        }
        return output;
    });
}
~~~

Use esse hook numa subclasse ou adapte seu corpo no template. Cada passo avanca
N -> N+1. Migracoes sao puras: sem Unity, SDK, escrita ou alteracao de identidade.
Faltando passo retorna MigrationRequired; schema futuro e recusado.

PermitirLegadoLocalSemHmac e opcao explicita de migracao local/backup do codec 1.
Nunca libera import externo sem HMAC. Cabecalho basico e preenchido ao escrever
dados locais. ID/schema criticos recebidos nao sao inventados; datas e revisoes
historicas ausentes continuam desconhecidas.

## Autenticacao injetada

O sistema central autentica e injeta CloudSaveSession: provider, identidade
pseudonima estavel e SessionId novo a cada login. Nao use tokens, emails ou IDs
brutos como SessionId/Identity.

~~~csharp
string owner = adapter.CriarIdentidadePseudonima(providerName, authenticatedUserId);
var session = new CloudSaveSession(providerName, owner, Guid.NewGuid().ToString("N"));
adapter.IdentidadeMudou(session);
bool linked = adapter.TentarVincularConta(providerName, authenticatedUserId);
~~~

O provedor injetado deve expor essa mesma sessao. Helpers de hash nao autenticam:
o chamador precisa obter dados comprovados pelo SDK.

Primeiro vinculo de save anonimo com progresso exige reset transacional aos
padroes. Mesma conta preserva dados; outra conta e recusada. Com a politica
RequireExplicitReset, ConfirmarTrocaContaComReset efetua a troca somente apos
confirmacao da UI. Isso nao apaga a conta antiga na plataforma.

MarcarContaNaoConectada encerra sessao sem apagar dono. O estado inicial e
nao_conectado. Jogos locais nao precisam de conta. Fora da sessao autenticada,
o template recusa importacao com conta.

Email cross-platform exige email realmente verificado pelo provedor. Use
CriarIdentidadePseudonimaPorEmail para o hash da sessao e
TentarVincularContaComEmailAutenticado para vincular. Email visivel somente no modo
explicito correspondente, fora do snapshot cloud. Nem toda plataforma fornece
email verificado; sem ele use o identificador autenticado suportado pelo jogo.
Email digitado nao e prova de identidade.

Preserve o algoritmo de derivacao de jogos publicados. O template 0.9.0 convertia
ID de provedor para minusculas; o novo helper preserva a caixa. Planeje migracao
autenticada antes de mudar esse contrato.

## Nuvem e callbacks

ConfigurarNuvem(ISessionCloudSaveAdapter, Func<bool>) injeta transporte e hook de
momento seguro. O save nao conhece cenas nem abre login. Os nomes antigos de
envio/baixar do site passam por esse coordenador.

O provedor implementa:
- Session: sessao atual autenticada, sem credenciais.
- IsAvailable: disponibilidade real.
- SupportsConditionalCommit: true apenas quando a integracao realmente impede
  sobrescrita silenciosa de uma revisao diferente.
- Execute: Read devolve contexto/lease e revisao. Commit recebe esse mesmo Context
  e ExpectedProviderRevision. Arquivo ausente usa revisao vazia, nao null.
- ReleaseContext: encerra uso local sem fingir cancelar escrita remota.

Capture a sessao da requisicao, confira a conta antes de cada etapa SDK e entregue
callbacks na thread principal. Preserve etapas open/read/resolve/commit no Context.
Nao reabra outro slot/conta para concluir uma operacao antiga.

Resultados: SdkSuccess, Error, Conflict, Unavailable, UserCancelled, WaitCancelled,
Timeout e Unknown. SdkSuccess e o sucesso reportado pelo SDK, nao consenso global.
Timeout/WaitCancelled encerram espera; a escrita remota pode terminar depois.
Uma nova tentativa rele antes de enviar. Tickets, fase e SessionId descartam
callbacks antigos/repetidos. Mudanca local ou saida do momento seguro impede
aplicacao tardia. Gameplay local continua disponivel.

EstadoAlterado, ErroTipado, ConflitoEncontrado, ProgressoAplicado,
ResetSolicitado e ResetConfirmado permitem UI e localization do jogo.
SaveAdapterExemploCena fornece ChooseLocal/ChooseIncoming/Defer, um painel opcional
e a chave save.conflict, para conectar a botoes/textos existentes sem impor layout.

## Exemplo opcional Web/site

CassinaxSiteSaveApi, SaveJsonMinimo e CassinaxContexto.jslib continuam incluidos.
O transporte legado usa salvar.php, carregar.php, apagar.php, csrf_token.php,
check-login.php e /api/v1/game-config/. A pagina fornece JOGO_ID, USUARIO_LOGADO,
IDIOMA_PORTAL, JOGO_BUILD, GAME_CONFIG_KEY e CSRF.

~~~csharp
site.DownloadSave(slot, result =>
{
    if (!result.Success || !result.HasPayload) return;
    if (!adapter.ValidarSnapshot(result.Payload, out var snapshot, out var error)) return;
    // Encaminhe o candidato ao fluxo de conciliacao do jogo.
    // Nao aplique automaticamente por login booleano ou horario.
});
~~~

Esse e um exemplo de leitura/validacao do transporte legado. Para o coordenador,
o site existente precisa oferecer sessao verificavel e revisao/escrita condicional.
O cliente antigo nao inventa essa capacidade nem e promovido automaticamente a
ISessionCloudSaveAdapter. Nenhum servidor/endpoint novo foi criado.

Um booleano de login nao distingue A de B. A fachada ApagarSaveNoSite retorna erro
orientando o reset; exclusao fisica exige confirmacao real da integracao.
ObterConfigRemotaDoSite e ObterIdiomaDoPortal continuam disponiveis.

## Exemplo opcional Google Saved Games

Nao ha SDK Google nos scripts distribuidos. Mantenha o provedor Android num
arquivo/assembly opcional do jogo, com autenticacao central. O trecho abaixo
orienta a etapa de abertura/leitura de um provedor, nao inicializa conta.

~~~csharp
#if CASSINAX_GOOGLE_SAVED_GAMES && UNITY_ANDROID
client.OpenWithManualConflictResolution(
    request.Slot, GooglePlayGames.BasicApi.DataSource.ReadNetworkOnly, true,
    (resolver, original, originalBytes, other, otherBytes) =>
    {
        // Valide ambos, conta/epoca e decisao do jogo.
        // Preserve resolver/handles ate uma decisao ainda valida.
        // Nunca escolha maior XP, saldo ou tempo de jogo.
    },
    (status, handle) =>
    {
        if (status != GooglePlayGames.BasicApi.SavedGame.SavedGameRequestStatus.Success)
            return; // Mapeie erro tipado e finalize uma unica vez.
        client.ReadBinaryData(handle, (readStatus, bytes) =>
        {
            // Confira limites e devolva payload/revisao/Context com esse handle.
        });
    });
// No Commit: client.CommitUpdate com o MESMO handle aberto e o payload validado.
// Mapeie status e descarte callbacks de sessoes/tickets antigos.
#endif
~~~

O trecho e guia de integracao, nao provedor Google homologado. O SDK pode registrar
conflitos para a proxima abertura; isso nao equivale automaticamente a
compare-and-swap. So declare SupportsConditionalCommit apos implementar e testar
essa garantia. Sem ela mantenha modo local ou o fluxo especifico do SDK.
Conflitos do SDK tambem precisam respeitar a maior epoca validada. Exclusao sem
callback conclusivo deve retornar Unknown, nao sucesso.

Referencia primaria: [Saved games in Unity](https://developer.android.com/games/pgs/unity/saved-games).
Nao ha credenciais reais, login automatico ou mock Google de runtime.

## Reset e offline

SolicitarReset(requestId) segue a politica do Inspector. LocalOnly muda apenas
o local. RemoteFirst le revisao remota, grava tombstone de epoca superior e so
entao aplica localmente. Falha remota conserva local. Repetir o mesmo requestId
concluido nao gera novo reset nem premios.

Epoca antiga e recusada. Ramos da mesma epoca precisam de ancestralidade/base
conhecida ou decisao explicita. A data e informativa. Nao existe merge generico
de economia. Uniao de fatos monotônicos e politica de dominio do jogo, somente
para mesma conta/epoca; nunca some consumiveis.

ApagarTodosDados e exclusao fisica local sem conta: quatro nomes gerenciados e
sidecars, sem recriar main, apagar exports ou remover pastas recursivamente.
Com conta/nuvem use reset. Depois do tombstone, o jogo cria padroes sem tratar
recebimento remoto como recompensa.

## Auditoria externa

RegistrarGanho/Gasto/Uso/Compra apenas registram eventos JSON. Chame junto da
alteracao real de saldo na mesma transacao. O payload inclui UTC, tipo, recurso,
quantidade, antes/depois, origem e referencia. O programa externo analisa.
Nao ha reconstrucao de historico legado ou auditor economico dentro do jogo.
Defina retencao/rotacao por jogo; exceder limites e erro observavel.

## Suporte e limites

| Plataforma | Local | Integracao adicional |
| --- | --- | --- |
| Editor/desktop | File ou PlayerPrefs | Conta/provedor se habilitados |
| WebGL | PlayerPrefs por padrao | Quota do navegador e pagina/SDK existente |
| Android | File ou PlayerPrefs | SDK opcional e teste em aparelhos/contas |
| iOS | File ou PlayerPrefs | Provedor do jogo e testes de suspensao |
| Consoles | Nao homologado | Storage/conta conforme SDK/licenca |

Codigo usa netstandard2.1. Referencias de compilacao testadas: Unity 6000.5.9f1.
Outras versoes/plataformas exigem homologacao. Compilar com simbolos WebGL nao
equivale a testar navegador/IL2CPP. O fake nao valida nuvem real.

Chave embutida protege formato/integridade acidental, nao impede fraude por quem
controla o cliente/chave. HMAC nao prova identidade de plataforma. Nao registre
payload, tokens, IDs brutos ou emails em logs. O auditor externo precisa entender
codec 2 e ausencia legitima de historico em saves antigos.
