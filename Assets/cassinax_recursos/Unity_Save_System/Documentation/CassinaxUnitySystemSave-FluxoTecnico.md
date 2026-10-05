# Cassinax Unity System Save 1.0.0 - Fluxo tecnico

Data: 2026-09-09. Status: piloto de integracao.
Este documento descreve garantias implementadas e limites observados, nao uma
homologacao universal de armazenamento ou nuvem.

## Responsabilidades

SaveCore manipula dicionario, codec, AES, GZip e hash. SaveSystem e o unico escritor
de estado na integracao padrao: valida, migra em staging, controla transacoes e
escopos. SaveAdapter configura e expõe uma fachada customizavel. FileSaveStorage e
PlayerPrefsSaveStorage persistem. SaveSyncCoordinator organiza sessoes e tickets.
A analise de auditoria e externa ao jogo.

Todos os acessos ao SaveSystem/coordenador devem ocorrer na thread principal.
O lock interno do Core protege seu dicionario, nao torna o pipeline inteiro
multithread. Nao altere o Core diretamente enquanto ele pertence ao System.

## Caminho de entrada

1. Limite de bytes e prefixo de versao.
2. Codec 2: HMAC sobre versao+cifra antes de descriptografar/descomprimir.
3. Codec 1: descriptografia limitada, parse estrito e HMAC legado. Excecao de
   ausencia de HMAC so existe quando configurada explicitamente para leitura local.
4. Rejeicao de entradas duplicadas e formato inesperado.
5. Verificacao de ID do jogo, core/schema futuros, identidade e metadados.
6. Migracoes registradas N -> N+1, sempre sobre copia e com identidade preservada.
7. Snapshot imutavel, emitido pela instancia validadora.
8. Aplicacao conforme ReplaceSnapshot/Merge e escopos declarados, rejeitando
   outra conta ou epoca antiga. Dados locais sao preservados por politica.
9. Persistencia de todo o staging; troca do estado em memoria apenas apos retorno
   bem-sucedido do armazenamento. Filas antigas sao descartadas no commit.
10. Eventos de progresso/aplicacao observam somente o commit, nao etapas da migracao.

Falha de validacao nao limpa dados, cria backup ou regrava o save.
TryPreviewPlainText deixou de representar uma via alternativa sem validacao.
Mudar o ID/schema passado ao validador nao permite aplicar um schema acima do
configurado na instancia.

## Codec 2

Envelope: [2] + Base64(IV de 16 bytes + AES-CBC/PKCS7) + ponto + HMAC-SHA256 hexadecimal.
O HMAC autentica inclusive [2]. O formato de texto comeca com CUSS2 e newline.
Cada entrada e Base64(UTF-8 chave), dois pontos, Base64(UTF-8 valor), newline.
A ordenacao de chaves e ordinal. Strings vazias, Unicode e delimitadores sao dados.

GZip e reconhecido por assinatura. Corrupcao/limite durante descompressao gera
erro; nao e interpretada como legado sem compressao. A leitura usa buffer limitado.
Limites sao aplicados antes de base64, antes/depois da descompressao e por entrada.
O marcador/codec e versionado separadamente do schema do jogo.

Codec 1 aceita as formas [COMANDO][chave][valor] e [COMANDO],[chave][valor].
Entradas ambiguas, comandos de remocao e duplicatas sao recusados. Dados que foram
perdidos pelo serializador antigo nao sao reconstruidos. Nao ha historico inventado.

HMAC usa a chave embutida no cliente e nao equivale a assinatura de um servidor.
Quem controla a chave/executavel pode fabricar payloads validos. O pacote nao
promete anti-fraude absoluta, autenticacao ou protecao contra rollback por atacante
com controle total do armazenamento. Revisoes/epocas protegem o protocolo cooperante.

## Transacoes, revisao e escopos

Begin captura a base; Enqueue opera sobre staging; Commit valida limites e persiste
uma vez. Rollback descarta staging. Falha de commit conserva o estado anterior em
memoria e permite rollback. Filas preexistentes sao consumidas somente no sucesso.
ProcessQueues agrupa o lote inteiro; valores identicos nao geram nova revisao.

Revision e um GUID de commit de progresso; parent guarda a revisao anterior.
Epoch e contador de reset. CUSSL/baseRevision e a ultima base confirmada localmente.
DataAtualizacao muda somente em alteracao real do progresso/reset; exportar e
capturar snapshot nao alteram datas ou revisoes. Historico legado desconhecido
fica vazio. ConfirmSynced nao dispara aplicacao de progresso nem premios.

DefaultSaveConflictResolver ignora relogio e saldos. Mesmo ID de revisao exige
fingerprint de progresso igual. Ancestralidade direta/base conhecida permite
escolher; dois ramos divergentes exigem decisao. Merge generico nao soma nada.
Uma politica customizada nao pode autorizar upload de epoca antiga no coordenador.

Scopes: Progress, LocalPreference, Identity, Metadata e Audit. Chaves desconhecidas
sao locais. Metadata exportavel e reservado aos campos do sistema. SLG0001 extra
pode conter dados do jogo conforme politica de escopo. Cloud inclui somente
accountHash como identidade; nao inclui emails, provider IDs ou CUSSL.
Backup inclui locais, mas restaura-los requer opcao explicita.

## Disco

FileSaveStorage exige diretorio relativo exclusivo do jogo, nome valido e
validador configurado pelo SaveSystem. Rejeita caminhos ligados/reparse points,
travessia e nomes arbitrarios de backup. Limita tamanho da leitura de arquivos.

Escrita:
- Valida o payload antes de abrir temporario.
- Preserva um tmp valido quando ele e a unica copia recuperavel.
- Escreve tmp, executa Flush(true) e valida a leitura do temporario.
- Usa File.Replace quando suportado, mantendo a copia anterior em bak.
- Apenas PlatformNotSupportedException/NotSupportedException habilitam fallback.
  IOException nao e tratada como plataforma sem suporte.
- No fallback, copia o main valido para bak antes de remover main e mover tmp.
  Existe uma janela sem main, coberta pelo protocolo de recuperacao.

Recuperacao deterministica: main valido, senao bak valido, senao tmp valido.
Nao escolhe por timestamp. Um arquivo desconhecido/invalido nao e devolvido como
valido. A copia recuperada e lida sem criar nova revisao; proximo commit pode
reescrever main. Nenhuma dessas etapas promete durabilidade universal frente a
falha fisica de hardware/controlador ou a todas as implementacoes de filesystem.

DeleteAll remove somente main, Backup_Auto.save, Backup_Manual.save,
Backup_Seguranca.save e .bak/.tmp de cada um. DeleteBackup exige nome conhecido e
remove seus sidecars. Nao remove a pasta, exports ou outros nomes.
Use uma pasta exclusiva por jogo. O template novo usa subpasta pelo ID; migrar
uma pasta legada compartilhada exige uma politica explicita antes de apagar dados.

PlayerPrefs possui garantias diferentes: a API Save nao confirma sincronizacao
do navegador/disco. Nao e apresentada como armazenamento atomico equivalente a
File.Replace. Falhas de quota/navegador precisam de testes no alvo.

Referencia primaria de substituicao:
[File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace).
A existencia de tmp isoladamente nao constitui garantia de atomicidade.

## Sessao, operacao e reset remoto

O jogo autentica. CloudSaveSession e injetada, com identidade pseudonima e
SessionId por login. O provedor deve conferir sessao real em cada etapa SDK.
Request contem slot, ticket, operacao, Context, timeout e revisao esperada.

Cada fase aceita somente uma resposta. Ticket/fase/sessao, revisao local,
filas pendentes e hook de momento seguro sao conferidos antes de aplicar.
Troca de identidade, reset e timeout invalidam callbacks antigos. CancelarEspera
nao significa cancelar a operacao no servidor. Unknown exige reconciliacao por
nova leitura, nunca repeticao cega de upload.

O coordenador requer escrita condicional/contexto que impeça perda silenciosa.
O cliente site legado nao oferece automaticamente esse contrato. O exemplo Google
na referencia e um guia, nao um provedor certificado. Nao ha servidor novo,
credenciais ou SDK obrigatorio neste pacote.

Reset RemoteFirst:
1. Le revisao/contexto remotos sob a sessao atual.
2. Cria tombstone sem progresso, com epoca maior e requestId.
3. Grava condicionalmente sobre a revisao lida.
4. Apos SdkSuccess, aplica no local e registra confirmacao.
5. Repeticao do mesmo requestId reconhece tombstone ja concluido.
6. Falha remota mantem o estado local. Falha local apos sucesso remoto exige nova
   leitura para concluir a reconciliacao; nunca reporta que os dois lados salvaram.

LocalOnly e outra politica; nao significa reset remoto. Exclusao fisica nao e
sinonimo de tombstone. Sem confirmacao de exclusao por uma API, nao retorne sucesso.
O jogo nao aplica recompensas ao receber tombstone, snapshot ou conquista remota.

## Verificacoes executadas

- 42 cenarios standalone sobre os arquivos finais do pacote, .NET SDK 10.0.400.
- 30 cenarios no Unity Editor 6000.5.9f1, sobre o unitypackage importado em
  projeto temporario isolado; importacao e testes encerraram com codigo 0.
- Os 12 cenarios extras standalone encerram um processo auxiliar nas etapas de
  escrita, reabrem o armazenamento em outro processo e exigem copia integral valida.
- Fault injection de IOException nos caminhos de File.Replace e fallback.
- Round-trip de JSON, Unicode, delimitadores, vazio e multiline.
- Import legado, politica explicita de legado sem HMAC, duplicatas, futuro,
  identidade, limites de descompressao, merge/replace, fila/reset e eventos.
- Vinculo anonimo/reset, mesma conta, troca explicita, rollback em falha de disco.
- Callbacks duplicados, troca A/B, timeout, alteracao local durante leitura,
  reset remoto falho e requestId repetido com provedor deterministico.
- Dois clientes isolados contra provedor deterministico: divergencia exige
  decisao, commit com revisao antiga falha e epoca anterior nao restaura progresso.
- Compilacao isolada com referencias Unity e simbolos Editor e WebGL, sem Google:
  zero erros e zero avisos.
- Os fakes ficam em Editor e condicionais UNITY_EDITOR/SAVE_STANDALONE. Development
  Build nao os habilita. O host standalone esta fora de Assets.

O primeiro teste Editor foi interrompido por certificado de um pacote de compras
adicionado ao criar o projeto temporario. O teste foi repetido sem essa dependencia
e terminou com codigo de saida 0. Nao foi desabilitada validacao de certificados.

## Pendencias de homologacao

Nao executados: encher um volume real, queda de energia fisica, filesystem de
consoles, quotas reais do navegador, suspensao em iOS/Android, build IL2CPP completa,
duas instalacoes reais sincronizando contra um servico de producao e callbacks
de SDK Google/site com contas reais. Portanto, atomicidade em todas as plataformas
e sincronizacao remota confiavel universal NAO sao alegadas nesta versao.

Os exemplos opcionais de Web/Google documentam pontos de integracao. A equipe de
cada jogo implementa/autentica seu provedor e executa os testes de duas instalacoes.
A suite deterministica valida o protocolo local, nao substitui essa homologacao.

## Distribuicao

PackageManifests/CassinaxUnitySystemSave.json e a lista permitida.
Tools/ExportSavePackage.ps1 gera Cassinax Unity System Save (1.0.0).unitypackage
em Packs e verifica pathnames/hashes. Inclui somente os arquivos listados, metas
e pastas ancestrais. Os pacotes anteriores permanecem intactos.
