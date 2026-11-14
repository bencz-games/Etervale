# Onboarding técnico — Etervale

Visão de arquitetura • Sistemas • C# • Design de software

Vou assumir o papel de Tech Lead do projeto e explicar como explicaria a um desenvolvedor júnior que acabou de entrar na equipa.

A ideia não é apenas mostrar o que cada pasta faz. Quero que entendas por que o código foi organizado dessa maneira, como os sistemas comunicam e como decidir onde implementar uma funcionalidade nova.

Consultei a estrutura da branch `main`, o README, a documentação de arquitetura e os principais módulos. Vou distinguir o que o projeto realmente implementa dos exemplos didáticos que usarei para ensinar C#.

## 1. Primeiro: o que é o Etervale?

O Etervale é uma reimplementação do MMORPG Priston Tale usando C#, .NET 10 e MonoGame.

Mas é importante entender uma coisa:

Etervale não é simplesmente um jogo feito em C#. É um conjunto de aplicações que cooperam para executar um MMORPG online.

Existem três responsabilidades fundamentais:

- Cliente: desenhar o mundo, receber os comandos do jogador, reproduzir animações, efeitos, áudio e interfaces.
- Servidor: controlar o mundo, validar ações, calcular combate, movimentar monstros e decidir o que realmente acontece.
- Persistência: guardar personagens, inventários, contas, progresso e outros dados para que sobrevivam ao encerramento do jogo.

O projeto adota uma filosofia chamada Server Authoritative, ou servidor autoritativo.

Imagina que o jogador pressiona o botão para atacar um monstro.

O cliente pode imediatamente mostrar a animação do ataque. Mas não é o cliente quem decide, em definitivo, que o monstro perdeu 500 pontos de vida. Essa decisão pertence ao servidor.

Isto é essencial num MMORPG porque um cliente pode ser modificado ou manipulado por um jogador mal-intencionado.

## 2. O desenho geral da arquitetura

O princípio mais importante é que o cliente e o servidor partilham algumas bibliotecas, mas não têm as mesmas permissões nem responsabilidades.

Por exemplo, ambos podem conhecer a fórmula usada para calcular uma estatística. Contudo, apenas o servidor autoriza a alteração real daquela estatística durante o jogo.

Isso também explica por que a pasta `Shared` é tão importante: ela evita que a mesma regra precise ser reescrita em vários locais.

## 3. As pastas principais do repositório

A solução está organizada por projetos C#, e não como um único executável gigantesco.

Etervale/

Client/

Jogo, HUD, gráficos, áudio e rede do cliente

Server/

Host do servidor e ligações TCP

Shared/

Regras, contratos, simulação e aplicação

Persistence/

Repositórios, SQL e migrações

tests/

Testes de regressão e benchmarks

tools/

Importadores, diagnóstico e utilitários

monogame/

Fork do motor gráfico MonoGame

docs/

Documentação técnica por domínio

PristonTale.sln

Solução .NET

### O que é uma solução `.sln`?

Em C#, um arquivo `.sln` representa uma solução que agrupa projetos.

Cada `.csproj` define um projeto: como é compilado, quais bibliotecas utiliza e de quais outros projetos depende.

Por exemplo:

```
PristonTale.sln
    |
    +-- PristonTale.Server.csproj
    +-- PristonTale.Application.csproj
    +-- PristonTale.Simulation.csproj
    +-- PristonTale.Protocol.csproj
    +-- PristonTale.Persistence.csproj
    +-- ...
```

Pensa na solução como uma empresa e em cada projeto como um departamento especializado.

O departamento financeiro não deve ter de conhecer os detalhes internos do departamento de design. Eles comunicam através de contratos bem definidos.

A mesma ideia aplica-se à arquitetura de software.

## 4. Entendendo as camadas de software

Agora vamos sair das pastas e começar a pensar como arquitetos.

Quando encontrares um bug ou fores implementar uma feature, a primeira pergunta não é "em qual arquivo eu escrevo isso?".

A pergunta correta é:

"A qual camada pertence esta responsabilidade?"

### Shared — o núcleo das regras

É aqui que estão algumas das decisões de arquitetura mais importantes.

| Projeto       | O que faz                             | Exemplo                                     |
| ------------- | ------------------------------------- | ------------------------------------------- |
| `Contracts`   | Define conceitos comuns               | `PlayerClass`, `EntityKind`, `LoginOutcome` |
| `Geometry`    | Geometria e matemática espacial       | Cálculos geométricos                        |
| `Assets`      | Acesso aos arquivos do jogo           | `AssetFiles`, leitura de formatos           |
| `Simulation`  | Regras de gameplay                    | Combate, itens, atributos, skills           |
| `Application` | Coordena operações e controla o mundo | `WorldServer`, `AuthAppService`             |
| `Protocol`    | Define como os dados passam pela rede | Pacotes, opcodes, codecs                    |

Vamos usar um exemplo.

Suponhamos que queremos implementar uma nova regra: um escudo reduz 20% de determinado dano.

A fórmula ou regra reutilizável pertence ao `Simulation`.

Mas a decisão de aplicar efetivamente o dano a um monstro ou jogador pertence à autoridade mantida em `Application`.

A serialização do resultado para o cliente pertence ao `Protocol`, e o código que transforma esse resultado numa barra de vida atualizada pertence ao cliente.

Repara como uma única ação atravessa várias camadas.

Isso é o que chamamos de separação de responsabilidades.

### O que significa Simulation?

O `Shared/PristonTale.Simulation` concentra conceitos como:

| Sistema     | Responsabilidades                             |
| ----------- | --------------------------------------------- |
| Characters  | Classes, atributos, equipamentos, regeneração |
| Combat      | Acerto, defesa, crítico, absorção, dano       |
| Items       | Inventário, requisitos, stacks, equipamentos  |
| Skills      | Definições, fórmulas, custos, cooldowns       |
| Progression | Experiência, níveis, pontos                   |
| World       | Mapas, regras de movimento, monstros, NPCs    |
| Quests      | Requisitos e progressão das missões           |
| Social      | Clãs, comércio, grupos e regras sociais       |

Um exemplo real é a classe `CombatResolver`, em [Simulation/Combat/CombatResolver.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Shared/PristonTale.Simulation/Combat/CombatResolver.cs?utm_source=chatgpt.com).

Ela concentra cálculos de combate, incluindo possibilidade de acerto, bloqueio e dano.

A vantagem é que não precisamos implementar outra versão da fórmula no servidor, outra no cliente e uma terceira nos testes.

Uma regra, uma implementação.

### O que significa Application?

O `Application` é o organizador das operações de jogo.

Encontramos classes como `AuthAppService`, `NpcAppService`, `QuestAppService`, `SessionAppService` e `WorldServer`.

Pensa assim:

Simulation: "Dadas estas condições, qual é o resultado da regra?"

Application: "Quem está a pedir esta ação? Pode executá-la? Em que momento? O que precisa ser alterado ou guardado?"

Esta diferença será uma das coisas mais importantes para entenderes antes de mexer no código.

## 5. WorldServer — o coração do MMORPG

Arquivo: [WorldServer.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Shared/PristonTale.Application/World/WorldServer.cs?utm_source=chatgpt.com)

Esta é uma das classes centrais da aplicação.

O `WorldServer` mantém o controlo sobre o mundo em execução: jogadores conectados, campos carregados, comandos e publicação de estados.

Ele trabalha com outras classes importantes:

| Classe          | Responsabilidade                             |
| --------------- | -------------------------------------------- |
| `WorldServer`   | Coordenação global e tick                    |
| `FieldInstance` | Um campo ou mapa ativo com as suas entidades |
| `WorldPlayer`   | Jogador dentro da simulação do servidor      |
| `WorldNpc`      | NPC existente no mundo                       |
| `FieldCombat`   | Resolução de ataques                         |
| `FieldSkills`   | Execução de habilidades                      |
| `WorldSnapshot` | Estado publicado para os clientes            |
| `WorldOutbox`   | Entrega de atualizações aos consumidores     |

### O que é um tick?

O mundo é atualizado a 20 ticks por segundo, ou seja, um ciclo a cada 50 milissegundos de tempo de simulação.

Um ciclo do servidor, simplificado

1

Receber comandos

Movimento, ataques, ações

2

Processar comandos

Validar e modificar estados

3

Atualizar campos

IA, combate, efeitos, movimento

4

Preparar snapshots

Estado das entidades e eventos

5

Publicar atualizações

Disponibilizar para envio aos clientes

Fluxo conceitual. O código também executa tarefas de manutenção e coordenação de sistemas sociais.

Aqui existe uma decisão de design interessante.

Os comandos do mundo são processados numa fila com um leitor. Depois, os campos ativos podem avançar em paralelo, utilizando `Parallel.ForEach`.

Isso permite aproveitar múltiplos núcleos da CPU, mantendo um ponto de coordenação para comandos e alterações de estado.

É por isso que a arquitetura é muito cuidadosa com acesso concorrente, estado compartilhado e alocações de memória.

Um detalhe que quero que aprendas: não devemos colocar consultas ao banco de dados dentro da execução do tick. Um acesso lento ao disco poderia provocar atrasos perceptíveis para os jogadores.

As regras do repositório proíbem operações bloqueantes de rede, disco e banco nesses loops críticos.

## 6. Fluxo completo de uma funcionalidade

Vou dar-te uma tarefa imaginária de estagiário:

> "Precisamos permitir que um jogador compre uma poção de um NPC."

Antes de programares, quero que sejas capaz de percorrer mentalmente este caminho:

HUD de loja

Client/Gameplay

Pedido de compra

Client.Net / Protocol

Receção e encaminhamento

Server/Net

Validação e preparação

NpcAppService / WorldServer

Aplicação e persistência

Application / Persistence

Inventário atualizado

Snapshot → Client

O servidor verifica coisas como a distância ao NPC, a disponibilidade do item, o preço, o dinheiro do jogador e a capacidade do inventário.

Outro conceito importante aqui é Prepare → Commit.

Em vez de alterar tudo imediatamente, o sistema pode primeiro preparar uma operação validada e só depois aplicar a alteração. Há também operações que precisam de persistência antes do commit, especialmente transferências de itens e dinheiro.

Isso ajuda a prevenir duplicações e inconsistências quando há falhas.

O Etervale centraliza parte desse comportamento em `DurableOperations` e `DurableLease`.

Por exemplo, num trade entre dois jogadores, não queremos que o Jogador A perca um item e o Jogador B deixe de o receber porque uma operação de armazenamento falhou.

Consulta: [DurableOperations.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Shared/PristonTale.Application/Services/DurableOperations.cs?utm_source=chatgpt.com).

# 7. Agora vamos aprender C# usando o próprio Etervale

A partir daqui, quero que comeces a olhar para a declaração de uma classe e consigas identificar o seu papel.

## 7.1 `public sealed record` — o que significa cada palavra?

Este é um trecho real, reduzido ao essencial, de `LoginOutcome.cs`:

```

public sealed record LoginOutcome(
    bool Ok,
    string SessionToken,
    string Message)
{
    public LoginFailure Failure { get; init; }
}

```

Vamos desmontá-lo.

`public` indica que o tipo é acessível a outros códigos que possam referenciar a biblioteca.

`sealed` indica que não se pode criar uma classe derivada desse tipo.

`record` declara um tipo de referência com recursos especiais para representar dados, incluindo comparação por valor, geração de métodos de igualdade e suporte à expressão `with`.

`LoginOutcome` é o nome do tipo.

Os parâmetros entre parênteses são o construtor posicional do record. Também dão origem a propriedades públicas.

Na prática, poderíamos usar assim:

```
var resultado = new LoginOutcome(
    true,
    "token-123",
    "Login realizado"
);

Console.WriteLine(resultado.Ok);
```

E podemos criar uma cópia modificada:

```
var outro = resultado with
{
    Message = "Bem-vindo!"
};
```

O `with` cria uma cópia com a propriedade indicada alterada.

O detalhe é que um `record` não garante imutabilidade profunda. Pode conter propriedades mutáveis ou referências para objetos mutáveis. O `with` também faz uma cópia superficial.

O Etervale utiliza records para representar resultados, definições e operações preparadas.

É uma escolha coerente: esses objetos normalmente representam um conjunto de dados, e não uma entidade com identidade e ciclo de vida próprios.

Arquivo: [LoginOutcome.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Shared/PristonTale.Contracts/Sessions/LoginOutcome.cs?utm_source=chatgpt.com).

## 7.2 `readonly record struct` — dados pequenos e leves

Outro exemplo real:

```

public readonly record struct WeatherState(
    WeatherKind Kind,
    byte Intensity,
    byte WindHeading,
    byte Wind)
{
    public static readonly WeatherState Clear = default;

    public bool Raining =>
        Kind is WeatherKind.Rain or WeatherKind.Storm;
}

```

O que mudou?

`struct` é um tipo de valor, ao contrário de `class` e do `record` de referência.

Na atribuição normal, um struct é copiado por valor. Uma classe normalmente é partilhada através de referências.

`readonly` impede que o estado de instância do struct seja modificado depois de construído.

`record` acrescenta funcionalidades convenientes de comparação por valor e cópia.

Por que usar isso para o clima?

Porque um estado de clima é um conjunto pequeno de valores. Não precisa de identidade própria, herança ou um ciclo de vida complexo.

O projeto faz uso semelhante em `ItemStatBlock` e `HitResult`.

Regra prática para o teu dia a dia:

| Quando o conceito é...                           | Considera usar           |
| ------------------------------------------------ | ------------------------ |
| Uma entidade com identidade e estado mutável     | `class`                  |
| Um resultado ou definição comparável por valor   | `record`                 |
| Um valor pequeno que será copiado frequentemente | `readonly record struct` |
| Um conjunto de funções sem instâncias            | `static class`           |

Não uses `struct` em tudo apenas por imaginar que será mais rápido. Structs grandes ou copiados frequentemente também podem ter custos.

Arquivo: [Weather.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Shared/PristonTale.Contracts/World/Weather.cs?utm_source=chatgpt.com).

## 7.3 `interface` — um contrato entre sistemas

Agora vem um dos conceitos mais importantes da arquitetura.

O Etervale define esta interface:

```
public interface ICharacterRepository
{
    Task<bool> UpdateAsync(
        CharacterRecord record,
        CancellationToken ct = default
    );
}
```

A interface real possui mais operações; esta é uma delas.

Uma interface diz o que um componente precisa oferecer, mas não necessariamente como deve fazê-lo.

Ela não contém o SQL de atualização de personagem.

A implementação real existe noutra camada:

```
public sealed class DapperCharacterRepository(
    IDbConnectionFactory connectionFactory
) : ICharacterRepository
{
    // Implementação das operações
}
```

Aqui, `: ICharacterRepository` significa que a classe implementa o contrato.

O construtor entre parênteses depois do nome é um primary constructor, recurso moderno de C#.

Percebe o benefício arquitetural?

O `Application` precisa guardar personagens, mas não precisa de conhecer os detalhes de Dapper, SQLite ou PostgreSQL.

Ele depende de `ICharacterRepository`.

Isso chama-se Dependency Inversion, ou inversão de dependência.

O benefício aparece também nos testes: podemos substituir o repositório real por uma implementação em memória, sem precisar de uma base de dados de produção.

O repositório faz exatamente isso em `MemoryCharacters`, nos testes.

Arquivos: [ICharacterRepository.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Persistence/PristonTale.Persistence.Abstractions/Characters/ICharacterRepository.cs?utm_source=chatgpt.com) e [DapperCharacterRepository.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Persistence/PristonTale.Persistence/Repositories/DapperCharacterRepository.cs?utm_source=chatgpt.com).

## 7.4 Dependency Injection — quem cria os objetos?

Abre [HostingExtensions.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Server/PristonTale.Server/Hosting/HostingExtensions.cs?utm_source=chatgpt.com).

Encontrarás registros semelhantes a estes:

```
builder.Services.AddSingleton<
    ISessionRegistry,
    SessionRegistry
>();

builder.Services.AddSingleton<AuthAppService>();
```

É o sistema de Dependency Injection (DI) do .NET.

Em vez de espalhar código como:

```
var repository = new DapperCharacterRepository(...);
var service = new CharacterAppService(...);
```

por vários locais da aplicação, o servidor configura como os objetos serão criados e partilhados.

`AddSingleton` significa que uma única instância será reutilizada naquele container durante a sua vida útil.

Outras opções comuns no .NET incluem `AddTransient` (nova instância por resolução) e `AddScoped` (uma instância por escopo). No Etervale, o host do jogo não é uma aplicação HTTP tradicional, portanto não deves presumir que cada jogador equivale automaticamente a um escopo.

O servidor também utiliza `AddHostedService`, que registra serviços geridos pelo host, incluindo tarefas com ciclo de vida próprio, como a execução do tick e serviços de manutenção.

## 7.5 `sealed partial class` — uma classe dividida em arquivos

Olha este exemplo real:

```
public sealed partial class GameScene
    : IDisposable, IHudPortraitScene
{
    // ...
}
```

Existem três conceitos aqui.

`sealed`: a classe não pode ser herdada.

`partial`: partes da mesma classe podem ser declaradas em diferentes arquivos.

`IDisposable, IHudPortraitScene`: a classe implementa essas interfaces.

O Etervale organiza `GameScene` assim:

```
GameScene.cs
GameScene.Skills.cs
GameScene.Monsters.cs
GameScene.Items.cs
GameScene.Audio.cs
GameScene.Weather.cs
GameScene.Trades.cs
...
```

Estes arquivos não representam várias instâncias de `GameScene`. São partes da mesma classe, reunidas pelo compilador.

Isso permite dividir uma implementação muito grande por responsabilidade.

É uma boa ferramenta organizacional, mas há uma ressalva que quero que aprendas desde cedo:

Dividir uma classe com `partial` não equivale a dividir responsabilidades entre classes independentes.

Os métodos dos diferentes arquivos continuam a poder partilhar os membros privados da mesma classe. Portanto, `partial` melhora a navegação, mas não elimina automaticamente o acoplamento.

O projeto também utiliza essa estratégia em `WorldServer`, `FieldInstance` e `PristonRenderer`.

## 7.6 `abstract`, `virtual` e `override`

O cliente tem uma classe chamada `GameScreen`.

Parte da implementação:

```
public abstract class GameScreen : IDisposable
{
    public virtual void Enter() { }

    public abstract void Update(GameTime gameTime);

    public abstract void Draw();

    public virtual bool HandleEscape() => false;
}
```

Aqui temos outra abordagem: herança.

`abstract class` significa que a classe não pode ser instanciada diretamente; serve como base para outras classes.

`abstract void Update(...)` significa que uma classe concreta derivada tem de fornecer uma implementação.

`virtual` significa que a classe base já fornece um comportamento, mas uma classe derivada pode substituí-lo usando `override`.

Por exemplo, um exemplo didático simplificado:

```
public class LoginScreen : GameScreen
{
    public override void Update(GameTime time)
    {
        // Atualizar a tela de login
    }

    public override void Draw()
    {
        // Desenhar a tela de login
    }
}
```

Neste exemplo, o construtor da classe base foi omitido para simplificar; no Etervale real, `GameScreen` recebe um `ScreenContext`.

A ideia é que todas as telas respeitem um contrato comum de execução, entrada e desenho.

Arquivo: [GameScreen.cs](https://github.com/GabrielAsafe/Etervale/blob/main/Client/PristonTale.Gameplay/Screens/Framework/GameScreen.cs?utm_source=chatgpt.com).

## 7.7 Outros elementos de C# que vais encontrar constantemente

| Sintaxe             | Significado                                                            |
| ------------------- | ---------------------------------------------------------------------- |
| `private readonly`  | Campo privado que só pode ser atribuído na inicialização ou construção |
| `const`             | Constante em tempo de compilação                                       |
| `static`            | Membro associado ao tipo, não a uma instância                          |
| `internal`          | Acessível dentro do assembly, salvo acesso especial concedido          |
| `get; private set;` | Leitura pública, alteração apenas dentro do tipo                       |
| `get; init;`        | Permite inicialização, mas não uma atribuição comum posterior          |
| `enum`              | Conjunto nomeado de valores                                            |
| `T?`                | Valor anulável ou referência anotada como anulável                     |
| `Task<T>`           | Operação assíncrona que devolve `T`                                    |
| `async` / `await`   | Implementação e consumo de operações assíncronas                       |
| `CancellationToken` | Permite pedir o cancelamento de uma operação                           |
| `IDisposable`       | Contrato para libertar recursos                                        |
| `in`                | Passa um valor por referência somente para leitura                     |
| `event`             | Permite subscrever notificações de acontecimentos                      |
| `where T : ...`     | Impõe restrições a um parâmetro genérico                               |

Dois padrões que também merecem atenção são os métodos estáticos auxiliares, como `CombatResolver.ResolveMonsterAttack`, e as propriedades calculadas:

```
public bool IsEquippable => EquipSlot.HasValue;
```

Esse `=>` é uma expressão compacta para devolver o resultado, sem precisar de escrever um bloco `get` completo.

# 8. Os sistemas do jogo e como foram desenhados

Agora já tens vocabulário de C# suficiente para começarmos a conversar sobre design dos sistemas.

## Sistema de personagens

A classe `PlayerCharacter`, em `Simulation/Characters`, é um objeto que mantém o estado do personagem.

Tem nível, experiência, dinheiro, atributos, skills, inventário e equipamentos. O próprio código controla alterações a várias propriedades com `private set`.

É um exemplo de encapsulamento: não é suposto qualquer parte da aplicação poder alterar arbitrariamente o nível do personagem.

Outro conceito aqui é o de aggregate, uma entidade que reúne e controla um conjunto relacionado de estado e regras.

O `PlayerCharacter` não é simplesmente um saco de propriedades.

## Sistema de skills

O Etervale possui um design parcialmente data-driven.

Em vez de definir todos os números de cada skill diretamente numa grande sequência de condicionais C#, existem arquivos JSON com definições por classe.

Por exemplo, os dados são organizados em:

`Shared/PristonTale.Simulation/Skills/Data/Classes/`

As fórmulas e os tipos de comportamento são processados por componentes como `SkillRule`, `SkillRuleCatalog` e `SkillFormulas`. Mecanismos específicos são tratados na simulação e na camada autoritativa do mundo.

Por que isso é interessante?

Se precisamos mudar o custo de mana de uma skill já suportada pelo modelo de regras, normalmente a mudança pode ser apenas nos dados.

Se precisamos inventar um novo comportamento que o modelo não compreende, aí é necessário alterar código.

É uma separação entre configuração do conteúdo e mecânica do motor.

## Inventário e economia

O sistema de inventário tem regras de colocação em grelha, ocupação de espaço, stack, equipamento e transferências.

O servidor valida as alterações e utiliza operações duráveis para preservar a integridade dos itens.

Este é um domínio em que bugs podem ser particularmente graves: duplicação de itens, perda de ouro ou transferências executadas parcialmente.

Por isso, o projeto dá atenção a revisões, identificadores de operações, transações e à ordem de gravação.

## Sistemas sociais

O projeto também possui sistemas de grupos, trade entre jogadores, lojas pessoais, contatos, clãs, Bellatra e Bless Castle.

Nota um padrão recorrente: as regras fundamentais ficam em `Simulation`, enquanto os processos que coordenam jogadores, sessões e mudanças persistentes ficam em `Application`.

Essa consistência é um dos aspetos que facilitam navegar num repositório grande.

## Renderização e efeitos visuais

O cliente tem uma camada própria de rendering, `PristonTale.Engine3D`, além de `PristonTale.Effects`.

O motor interpreta formatos originais de modelos e mapas, como `.smd` e `.inx`, e faz rendering com MonoGame, através de um fork com backend Vulkan.

O rendering inclui iluminação, sombras, atmosfera, pós-processamento, animações e gestão de recursos gráficos.

A decisão importante é que a simulação de combate não depende do motor gráfico.

O resultado de um ataque pode ser calculado no servidor sem criar uma janela, carregar uma textura ou inicializar uma GPU.

Isso é uma boa separação entre lógica e apresentação.

# 9. As decisões arquiteturais que eu destacaria numa revisão

Se eu estivesse a avaliar esta arquitetura com a equipa, estes seriam os principais pontos de discussão.

| Decisão                        | Por que faz sentido                         | Qual é o custo                                 |
| ------------------------------ | ------------------------------------------- | ---------------------------------------------- |
| Servidor autoritativo          | Reduz a confiança no cliente                | Exige lidar com latência e sincronização       |
| Bibliotecas compartilhadas     | Evita duplicação de regras                  | Requer cuidado com dependências                |
| World tick a 20 Hz             | Dá cadência previsível à simulação          | Impõe orçamento de processamento por ciclo     |
| Fila de comandos               | Coordena alterações ao mundo                | Pode acumular pressão sob carga                |
| Atualização paralela de campos | Aproveita múltiplos núcleos                 | Exige invariantes rigorosos de concorrência    |
| Records para dados             | Facilita comparação e transporte de valores | Não garante imutabilidade profunda             |
| Classes `partial`              | Organiza componentes grandes por tópico     | Pode esconder classes excessivamente acopladas |
| Skills orientadas a dados      | Facilita balanceamento e expansão           | Requer validação dos contratos de dados        |
| Interfaces de persistência     | Facilita testes e troca de provider         | Introduz abstrações adicionais                 |
| Fork do MonoGame               | Permite controlar o backend gráfico         | Aumenta o trabalho de manutenção               |

Eu não classificaria o Etervale como uma implementação rígida de Clean Architecture, nem como um conjunto de microserviços.

Descreveria como uma arquitetura cliente-servidor modular, organizada em camadas, com domínio compartilhado e forte separação entre lógica, autoridade, transporte e infraestrutura.

Ela usa princípios associados a Clean Architecture, separação de responsabilidades, injeção de dependências e modelagem de domínio.

Isso é mais útil do que tentar dar ao projeto um rótulo de arquitetura perfeito.

## 10. E os testes?

Um aspeto interessante é que os testes não dependem do runner tradicional `dotnet test`.

O projeto possui uma aplicação de consola em:

`tests/PristonTale.Tests`

Ela executa testes de regressão, cenários de multiplayer, verificações de gameplay e alguns benchmarks e probes opcionais.

Há testes para combate, skills, transações, movimento, persistência, protocolo, quests e rendering.

A documentação especifica como devem ser adicionados novos casos e como avaliar operações do mundo.

Os comandos gerais são:

```
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror

dotnet run --project tests/PristonTale.Tests \
    -c Release --no-build
```

Isso importa por duas razões: o projeto trata warnings como erros, e uma alteração de comportamento deve vir acompanhada de um teste de regressão.

Não executei o build nem os testes nesta análise; a avaliação baseia-se nos arquivos e na documentação do repositório.

# 11. Como eu organizaria a tua primeira semana na equipa

Em vez de tentares ler milhares de arquivos, eu dar-te-ia um roteiro de estudo.

Plano de onboarding

0 de 5 concluídos

Dia 1 — Compreender a solução

Ler docs/architecture.md, identificar .sln, .csproj e o sentido das dependências.

docs/architecture.md

Dia 2 — Entender as entidades

Estudar PlayerCharacter, ItemDefinition, ItemInstance, LoginOutcome e interfaces de repositório.

Shared/PristonTale.Simulation/Characters/PlayerCharacter.cs

Dia 3 — Entender o servidor

Seguir Program.cs → HostingExtensions → WorldTickService → WorldServer.

Shared/PristonTale.Application/World/WorldServer.cs

Dia 4 — Seguir uma feature ponta a ponta

Acompanhar login ou uma operação de compra, desde o cliente até à persistência.

Shared/PristonTale.Application/Services/NpcAppService.cs

Dia 5 — Ler e executar testes

Entender testes de combate, inventário, transações e regressões antes de propor alterações.

tests/PristonTale.Tests/README.md

## 12. O exercício que eu te passaria como Tech Lead

Imagina que precisamos implementar esta funcionalidade:

> Um monstro derrotado tem 10% de probabilidade de deixar cair uma poção especial, desde que o jogador tenha nível 30 ou superior.

Antes de escrever código, quero que saibas responder:

1. Onde deve ficar a regra que determina a elegibilidade e a probabilidade de drop?
2. Quem decide se o monstro morreu e se o item vai aparecer no mundo?
3. Como é que os jogadores próximos ficam a saber que o item existe?
4. O que acontece quando alguém tenta apanhar o item?
5. Como garantir que o item não é duplicado quando há uma falha de conexão?
6. Que teste adicionaríamos para validar o comportamento?

A solução envolveria o domínio de loot em `Simulation`, a autoridade do campo em `Application`, a publicação de estado pela rede e as regras duráveis de recolha de itens.

Não precisarias necessariamente de modificar todas as camadas. Essa é precisamente a habilidade que quero que desenvolvas: identificar o menor conjunto correto de componentes que precisa mudar.

## 13. O que eu quero que retenhas desta primeira sessão

Um bom desenvolvedor C# não é aquele que conhece mais palavras-chave de cor. É aquele que olha para um sistema e reconhece os seus limites e responsabilidades.

Ao entrar no Etervale, quero que saibas distinguir três perguntas:

O que representa este objeto? Um `PlayerCharacter` tem identidade e estado; um `WeatherState` representa um valor; um `LoginOutcome` representa um resultado.

Quem é responsável por esta regra? Uma fórmula de combate pertence ao domínio; uma operação validada e executada no mundo pertence à aplicação; um comando de rede pertence à fronteira de transporte.

O que pode dar errado? Latência, concorrência, corrupção de estado, duplicação de itens, alocações excessivas e regras inconsistentes entre sistemas.

Se desenvolveres esse raciocínio, consegues trabalhar em partes do projeto que ainda nem conheces.

A minha orientação como Tech Lead seria começar por `PlayerCharacter`, `CombatResolver`, `AuthAppService` e `WorldServer`. Essas quatro classes dão-te uma boa amostra dos principais estilos de C# presentes no projeto: entidade mutável, regras estáticas, serviço com dependências e coordenador de estado em tempo real.

A documentação mais importante para acompanhar essa aprendizagem é [architecture.md](https://github.com/GabrielAsafe/Etervale/blob/main/docs/architecture.md?utm_source=chatgpt.com), seguida de [simulation.md](https://github.com/GabrielAsafe/Etervale/blob/main/docs/simulation.md?utm_source=chatgpt.com) e [application.md](https://github.com/GabrielAsafe/Etervale/blob/main/docs/application.md?utm_source=chatgpt.com).

São os três documentos que eu pediria a qualquer novo membro da equipa para compreender antes de aceitar a primeira tarefa que altera gameplay.
