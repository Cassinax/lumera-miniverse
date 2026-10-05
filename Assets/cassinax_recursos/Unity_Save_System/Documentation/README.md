# Cassinax Unity System Save

Versao `1.0.0`. Save local como fonte de verdade do progresso, com nuvem opcional.

Plataformas externas reconciliam com o save local; nunca o substituem.

## Onde ficam os recursos

Tudo pelo menu do Editor, junto das outras janelas Cassinax:

| Menu | O que faz |
| --- | --- |
| `Window > Cassinax > Save System` | janela do pacote: documentacao, exemplo, suite e manutencao |
| `Window > Cassinax > Auto Fix` | manutencao dos pacotes e limpeza de versoes antigas |
| `Window > Cassinax > Exemplo de Save Local` | cria na cena um exemplo que funciona sem internet |

## Em cinco minutos

1. Importe o pacote. Ele cai em `Assets/cassinax_recursos/Unity_Save_System`.
2. Abra `Window > Cassinax > Save System`. A janela diz se o projeto ja tem adapter
   proprio e abre o exemplo e a documentacao.
3. Abra `Samples/LocalOnly/SaveAdapterExemplo.cs` e **leia**.
4. Crie o adapter do seu projeto **fora** de `cassinax_recursos`, por exemplo em
   `Assets/Scripts/Controladores/SaveAdapter.cs`.
5. Defina ID do jogo, chave AES propria e versao de schema. Placeholder impede a
   inicializacao de proposito.
6. Apague o exemplo.
7. Coloque o adapter num objeto persistente da primeira cena.

Funciona offline desde o primeiro uso. Nuvem e conta sao opcionais.

## A unica peca que voce escreve

| Camada | Onde fica | Voce edita? |
| --- | --- | --- |
| `SaveCore` | `Runtime/Core` | Nao |
| `SaveSystem` | `Runtime/System` | Nao |
| Storage | `Runtime/Storage` | Nao, apenas escolhe |
| Contratos de nuvem | `Runtime/Cloud` | Nao, apenas implementa |
| Transporte do portal | `Runtime/Transports` | Nao |
| **Adapter do jogo** | **fora de `cassinax_recursos`** | **Sim, e so ele** |

O adapter e a ponte entre o jogo e a API. Todo o resto e fixo.

Precisando de algo que a API nao oferece, peca a mudanca no pacote. Corrigir a copia
local parece mais rapido e se perde na proxima atualizacao — e ninguem mais recebe a
correcao.

### Por que o exemplo se chama "Exemplo"

Para que reimportar o pacote nunca sobrescreva o adapter que voce escreveu. O exemplo e
substituido a cada atualizacao; o seu adapter nao e tocado. Ele tambem **nao deve ser
usado como script**: serve para ser lido e copiado.

## Estrutura

```
Unity_Save_System/
    Runtime/
        Core/           dados em memoria, codec, criptografia, hash
        System/         filas, transacoes, snapshots, validacao, migracao, escopo
        Storage/        arquivo, PlayerPrefs, memoria
        Payload/        divisao e remontagem de payloads grandes
        Cloud/          contratos de nuvem, conflito e coordenador
        Transports/     portal Cassinax e ponte WebGL
    Editor/             suite de aceitacao
    Samples/
        LocalOnly/      exemplo de adapter e exemplo de cena
        PlayGames/      exemplo de transporte para nuvem de plataforma
    Documentation/
```

Assemblies: `Cassinax.SaveSystem`, `Cassinax.SaveSystem.Editor`,
`Cassinax.SaveSystem.Samples`. Projetos que usam `asmdef` precisam referenciar
`Cassinax.SaveSystem`.

## Configuracao minima

```csharp
_saveCore = new SaveCore(chaveAes, SaveCore.CurrentCodecVersion, comprimir: true);
_saveSystem = new SaveSystem(_saveCore, new FileSaveStorage(pasta, arquivo));
_saveSystem.Configure(ID_DO_JOGO, VERSAO_DO_SCHEMA, Escopo);
```

`Configure` roda antes de qualquer leitura ou escrita.

## Escopo: o que viaja e o que fica

| Viaja | Fica no aparelho |
| --- | --- |
| progresso, inventario, conquistas | idioma, volumes, qualidade grafica |
| | consentimento, dados de conta, auditoria |

A regra e declarada por prefixo de chave. **Chave sem regra fica local.** Isso e
proposital para preferencias, mas vira armadilha quando alguem cria um prefixo de
progresso e esquece de declarar: o progresso deixa de viajar e nada avisa.

Confira com o relatorio:

```csharp
Debug.Log(_saveSystem.BuildScopeReport().Resumo());
```

Ele lista os comandos que **nao** viajam. Compare com a sua tabela antes de publicar.

## Apps com varios jogos

Um app que hospeda varios jogos grava cada um sob o proprio comando:

```csharp
string chave = SaveSystem.GameKey("jump_force", "recorde");   // [JOGO_JUMP_FORCE],[recorde]
_saveSystem.Configure(ID, SCHEMA, SaveSystem.MultiGameScope); // progresso por jogo viaja
```

`GamesWithData`, `HasGameData` e `EnqueueRemoveGameData` permitem listar e apagar os
dados de um jogo sem tocar na carteira comum nem nos outros jogos.

## Transacoes

```csharp
adapter.Begin();
// varias escritas
if (!adapter.Commit()) adapter.Rollback();
```

Nao aninhe transacoes. Nao chame dentro delas fluxos de gameplay que ja fazem flush.
Leituras representam o estado confirmado, nao a fila. Falha de disco nao publica saldo
como persistido.

## Quando o arquivo local esta invalido

Arquivo invalido ou de versao futura **nao e sobrescrito**. O jogo troca o storage por
`MemorySaveStorage`, continua jogavel na sessao e sinaliza o estado. O arquivo fica
intacto, esperando um reset explicito do jogador.

## Nuvem

Tres contratos, do mais simples ao mais exigente:

| Contrato | Para que serve |
| --- | --- |
| `ICloudSaveAdapter` | provedor sincrono, por itens com limite de tamanho |
| `IAsyncCloudSaveAdapter` | transporte assincrono de payload completo |
| `ISessionCloudSaveAdapter` | provedor com sessao e **commit condicional** |

`SaveSyncCoordinator` usa o terceiro e exige commit condicional de verdade: sem revisao
esperada, ele recusa a operacao em vez de arriscar sobrescrever. SDK que resolve conflito
no momento da abertura nao encaixa nesse contrato — nesse caso escreva o transporte,
usando `Samples/PlayGames` como referencia.

### Regras que evitam perda e duplicacao

1. Nunca somar economia de duas copias divergentes.
2. Nao decidir por data do aparelho nem por maior saldo.
3. Divergencia real exige escolha do jogador, com opcao de adiar.
4. Linhagem direta resolve sozinha; dois ramos nao.
5. Conta diferente nao recebe o progresso da conta anterior.
6. Alteracao local durante a consulta adia a restauracao.
7. Dois provedores simultaneos sao recusados: nao existe transacao distribuida.

### Apagar progresso

1. exigir a mesma conta autenticada, quando houver vinculo;
2. gravar no slot remoto um marcador de reset com nova epoca, sem progresso;
3. somente apos sucesso confirmado, limpar o local.

Falhando a operacao remota, o reset local nao prossegue. Isto e reinicio de progresso,
nao promessa de exclusao fisica do que o provedor retem.

## Limites honestos

| Afirmacao errada | Realidade |
| --- | --- |
| "criptografia impede cheat" | a chave esta no cliente; ela permite **detectar** adulteracao |
| "o sistema nao trata dados de conta" | o hash de identidade e pseudonimo, nao ausencia de dado |
| "reset apaga tudo" | reset e nova epoca, nao exclusao fisica |
| "integridade torna o save inviolavel" | permite detectar e recuperar, nao impedir |
| "passou nos testes, entao persiste" | teste puro nao prova gravacao em aparelho |

## Validar antes de publicar

Compilar e passar testes nao prova persistencia real. Em aparelho:

- conta nova com nuvem vazia: jogar, voltar ao menu, sincronizar, reabrir;
- reinstalar ou usar um segundo aparelho e confirmar a recuperacao;
- jogar offline e voltar online sem duplicar recompensa;
- alterar dois aparelhos offline e verificar a escolha explicita;
- trocar de conta e confirmar que o progresso nao vaza;
- reset com a mesma conta, reset offline e reset com outra conta;
- cortar a rede na abertura, na leitura e no commit;
- alterar progresso durante a consulta;
- permissao negada, disco cheio e falha de escrita em ambiente controlado.

## Atualizar de uma versao anterior

A 1.0.0 mudou a arvore de arquivos. Reimportar sobre uma instalacao antiga deixa as duas
copias no projeto, e classes duplicadas nao compilam.

Depois de importar, abra **Window > Cassinax > Auto Fix** e rode a verificacao. Ele mantem
a versao superior e remove as sobras. O Auto Fix atua somente dentro de
`cassinax_recursos`: restos fora dessa pasta sao relatados para voce remover a mao, porque
ali pode haver codigo seu.

## Ver tambem

- `CassinaxUnitySystemSave-Referencia.md` — API campo por campo
- `CassinaxUnitySystemSave-FluxoTecnico.md` — fluxo interno, para manutencao
- `MatrizDePlataformas.md` — o que e suportado e o que exige integracao propria
