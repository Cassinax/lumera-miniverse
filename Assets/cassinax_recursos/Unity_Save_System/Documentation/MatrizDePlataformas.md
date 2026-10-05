# Matriz de plataformas

Versao `1.0.0`.

Esta matriz separa tres coisas que costumam ser confundidas: o que o pacote entrega, o
que exige integracao escrita pelo projeto, e o que ainda nao foi verificado em execucao
real. Um item marcado como entregue significa que o codigo existe e passou nos testes
automatizados — nao que foi exercitado em producao naquela plataforma.

## Save local

| Plataforma | Storage recomendado | Situacao |
| --- | --- | --- |
| Windows, Linux, macOS | `FileSaveStorage` | entregue; escrita atomica com `File.Replace` quando suportado |
| Android | `FileSaveStorage` | entregue; IL2CPP nao verificado em execucao |
| iOS | `FileSaveStorage` | entregue; nao verificado em execucao |
| WebGL | `PlayerPrefsSaveStorage` | entregue; `System.IO` no navegador nao garante flush |
| Editor | qualquer | entregue; usado pela suite de aceitacao |
| Qualquer | `MemorySaveStorage` | entregue; para persistencia suspensa e testes |

Em WebGL, use `PlayerPrefs`. O Unity grava no IndexedDB e garante o flush; escrever por
`System.IO` no navegador pode perder o ultimo save quando a aba fecha.

## Nuvem

| Destino | Contrato | Situacao |
| --- | --- | --- |
| Portal Cassinax | `IAsyncCloudSaveAdapter` via `CassinaxSiteSaveApi` | entregue; nao exercitado contra o backend de producao a partir deste pacote |
| Provedor com commit condicional | `ISessionCloudSaveAdapter` + `SaveSyncCoordinator` | entregue; exige implementacao real de commit condicional |
| Nuvem de plataforma que resolve conflito na abertura | transporte proprio, com `Samples/PlayGames` como referencia | exemplo entregue; o transporte e escrito pelo projeto |
| Provedor por itens com limite de tamanho | `ICloudSaveAdapter` + `SavePayloadChunks` | entregue |

### Por que uma nuvem de plataforma pede transporte proprio

`SaveSyncCoordinator` segue abrir, ler, resolver e confirmar, e exige enviar a revisao
esperada no commit. Isso protege contra sobrescrever alteracao alheia.

Alguns SDKs funcionam de outra forma: entregam as duas copias no momento da abertura e
esperam que o cliente escolha ali mesmo, sem expor uma revisao comparavel. O coordenador
recusa esse cenario em vez de fingir que houve commit condicional. O caminho correto e
escrever o transporte, e `Samples/PlayGames` traz as pecas reutilizaveis: envelope
versionado, hash canonico, validacao e decisao por linhagem e epoca.

## Transferencia manual

| Recurso | Situacao |
| --- | --- |
| Exportar e importar payload completo | entregue |
| Multipart por texto, para copiar e colar | entregue |
| Multipart por varios arquivos | entregue |
| Download e upload de arquivo pelo navegador em WebGL | nao entregue |

## Compras

| Recurso | Situacao |
| --- | --- |
| Reconciliacao a partir de recibos | entregue via `IPlatformPurchaseAdapter` |
| Validacao do recibo | fora do escopo: quem valida e a plataforma ou o seu servidor |

O pacote aplica o resultado que a plataforma afirma; ele nao julga recibo.

## O que nao foi verificado

Registrar o que nao foi testado faz parte do relatorio. Nesta versao:

- build WebGL real em navegador, incluindo o link da ponte `.jslib`;
- build Android com IL2CPP;
- disco cheio e perda de energia em volume real, em vez de falha injetada;
- transporte para nuvem de plataforma em producao a partir deste pacote;
- as janelas do Editor (Save System e Auto Fix) executadas dentro do Editor.

## O que foi verificado

- 42 cenarios de aceitacao em harness .NET, cobrindo arquivo temporario, falha de disco,
  interrupcao de processo em oito pontos, recuperacao, transacoes e sincronizacao simulada;
- 24 cenarios das regras de decisao do exemplo de nuvem de plataforma, incluindo colisao
  de hash, chave duplicada, troca de conta e marcador de reset;
- compilacao de referencia contra o Editor 6000.5.9f1, sem erro nem aviso;
- a ponte `.jslib` executada com stubs do emscripten, conferindo o simbolo exportado, os
  seis campos de contexto e a degradacao com jogador deslogado;
- em campo: recuperacao apos reinstalacao, com o save reconhecido na nuvem de plataforma
  e as opcoes apresentadas ao jogador.
