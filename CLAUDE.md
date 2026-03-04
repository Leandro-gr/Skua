# Skua Fork — Contexto do Projeto

## O que é o Skua

Bot/cliente de automação para o jogo **AdventureQuest Worlds (AQW)** — jogo browser baseado em Flash.
Permite escrever **scripts em C#** para automatizar tarefas: farmar itens, completar quests, combate automático, gerenciar múltiplas contas.

O AQW ainda usa Flash (com cliente próprio que também bypassa o EoL do Flash).
Existe um AQW 2 em Unreal Engine planejado, mas é futuro distante. O foco aqui é o AQW Flash atual.

---

## Arquitetura geral

```
Skua.App.WPF          → Interface gráfica principal (WPF)
Skua.WPF              → Componentes UI reutilizáveis (controls, converters)
Skua.Core             → Motor principal (lógica de automação, scripts)
Skua.Core.Interfaces  → Contratos/interfaces (~80 interfaces)
Skua.Core.Models      → Modelos de dados do jogo (itens, quests, monstros...)
Skua.Core.Utils       → Utilitários gerais
Skua.Core.Generators  → Source generators
Skua.AS3              → Código ActionScript 3 (compilado para skua.swf)
Skua.SyncConsole      → Coordenador multi-conta (VAZIO — principal alvo do fork)
Skua.App.WPF.Follower → App seguidor (incompleto, gRPC comentado)
Skua.App.WPF.Sync     → App sync (incompleto, só janela)
Skua.App.WPF.Lite     → Versão leve
Skua.Manager          → Gerenciador de contas/launcher
```

**Tecnologias:** .NET 6 + WPF, Roslyn (compilação de scripts em runtime), CommunityToolkit.Mvvm (MVVM), CoreHook (hooking), Newtonsoft.Json, gRPC (planejado mas não implementado).

---

## Como o jogo se conecta ao Skua

### skua.swf (Skua.AS3)
Não é o jogo — é um wrapper ActionScript 3 que:
1. Consulta `https://game.aq.com/game/api/data/gameversion` para pegar versão atual
2. Baixa o `.swf` real do jogo do servidor AQW
3. Carrega o jogo dentro de si mesmo
4. Injeta o código Skua no domínio do jogo
5. Cria ponte de comunicação C# ↔ Flash via `ExternalInterface`

### EoLHook (Skua.WPF/Flash/EoLHook.cs)
Flash foi descontinuado em 31/12/2020 e recusa rodar após essa data.
O hook intercepta `GetSystemTime` do `kernel32.dll` e força o ano a retornar **2020**, enganando o Flash.

```csharp
ptr->wYear = 2020; // força ano para bypass do EoL
```

### CaptureProxy (Skua.Core/GameProxy/CaptureProxy.cs)
Proxy TCP man-in-the-middle na porta **5588**:
- O cliente Flash conecta em `localhost:5588` em vez do servidor real
- O proxy repassa para o servidor AQW (SmartFoxServer)
- Todos os pacotes passam por `Interceptors` que podem ler/modificar/bloquear
- Pacotes AQW são delimitados por byte nulo `\0`
- Protocolo: SmartFoxServer com formato `%xt%zm%comando%roomId%dados%`

### FlashUtil (Skua.WPF/Flash/FlashUtil.cs)
Ponte C# ↔ Flash via `AxShockwaveFlash.CallFunction()` usando XML:
```csharp
Flash.CallFunction("<invoke name=\"funcao\" returntype=\"xml\">...</invoke>");
```

---

## ScriptInterface — centro de tudo

`Skua.Core/Scripts/ScriptInterface.cs` — agrega 30+ serviços em um único objeto injetado nos scripts:

| Propriedade | O que faz |
|---|---|
| `Script.Combat` | Atacar, matar, caçar monstros |
| `Script.Inventory` | Gerenciar itens, banco, inventário |
| `Script.Map` | Navegar entre mapas |
| `Script.Quests` | Accept/complete quests |
| `Script.Player` | Estado do personagem |
| `Script.Drops` | Coletar drops |
| `Script.Skills` | Usar habilidades com prioridade |
| `Script.Send` | Enviar pacotes customizados |

---

## Feature principal do fork: Barrier Sync Multi-conta

### Problema identificado
O sistema de Follower atual (incompleto) simplesmente faz o seguidor espelhar o líder.
Isso quebra quando: líder tem 50/50 itens de quest, seguidor tem 49/50 → líder avança, seguidor vai junto sem terminar.

### Solução: Barrier Synchronization
Padrão onde **ninguém avança até todos estarem prontos**.

```
[Skua Conta A]  ─┐
[Skua Conta B]  ─┼──► [SyncConsole - Coordenador Local]
[Skua Conta C]  ─┘         │
                            │  Só libera quando TODOS completaram a etapa
                            ▼
                    [Broadcast: "podem avançar"]
```

### Onde implementar
- **`Skua.SyncConsole/Program.cs`** — atualmente vazio, é o lugar certo para o coordenador
- Cada instância Skua conecta ao SyncConsole via Named Pipes ou TCP local
- Cada conta reporta progresso (`questId`, `atual`, `necessario`)
- SyncConsole mantém estado de todas as contas
- Só faz broadcast de "avançar" quando **todas** atingirem a condição
- Cada Skua fica em wait bloqueante até receber o sinal

### Estado atual dos projetos multi-conta
- `Skua.SyncConsole` → **vazio** (Program.cs com Main vazio)
- `Skua.App.WPF.Follower` → código gRPC todo comentado, não funciona
- `Skua.App.WPF.Sync` → só a janela, sem lógica

O projeto original tentou implementar via gRPC mas não finalizou. Named Pipes é mais simples para começar.

---

## Padrões de código usados no projeto

- **MVVM** com CommunityToolkit.Mvvm (source generators `[ObservableProperty]`, `[RelayCommand]`)
- **Dependency Injection** via `Microsoft.Extensions.DependencyInjection` + `Ioc.Default`
- **Lazy initialization** para evitar dependências circulares (`Lazy<IServiço>`)
- **`[ObjectBinding]` e `[MethodCallBinding]`** — atributos customizados que geram chamadas Flash automaticamente
- **Messenger** (forte e fraco) para comunicação desacoplada entre ViewModels

---

## Arquivos chave para estudar

| Arquivo | Por que ler |
|---|---|
| `Skua.Core/Scripts/ScriptInterface.cs` | Centro de tudo |
| `Skua.Core/Scripts/ScriptQuest.cs` | Lógica de quests, RegisterQuests, barrier |
| `Skua.Core/GameProxy/CaptureProxy.cs` | Como o proxy funciona |
| `Skua.WPF/Flash/FlashUtil.cs` | Comunicação C# ↔ Flash |
| `Skua.WPF/Flash/EoLHook.cs` | Hook do sistema |
| `Skua.AS3/skua/src/skua/Main.as` | Lado Flash/ActionScript |
| `Skua.Core/AppStartup/Services.cs` | Como DI está configurada |
| `Skua.SyncConsole/Program.cs` | Onde implementar o coordenador (vazio) |

---

## Plano de aprendizado C#/.NET via este projeto

1. **Fundamentos C#:** classes, interfaces, `async/await`, `Task`, generics
2. **Injeção de dependência:** como `Services.cs` registra e como `Ioc.Default` resolve
3. **MVVM:** como `[ObservableProperty]` e `[RelayCommand]` funcionam
4. **Ler ScriptQuest.cs** para entender o padrão geral dos serviços
5. **Implementar:** comunicação via Named Pipes no SyncConsole
6. **Implementar:** lógica de barreira (barrier pattern)
7. **Integrar:** hook de barreira nos pontos de avanço de quest

---

## Preferências do desenvolvedor

- Iniciante em C#/.NET — explicações didáticas são bem-vindas
- Foco em aprender através do projeto, não apenas receber código pronto
- Não automatizar commits — sempre pedir confirmação
- Português brasileiro nas explicações
