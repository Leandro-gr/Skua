// Simulador de 3 contas Skua conectando ao SyncConsole.
// Roda JUNTO com o SyncConsole.exe para testar a sincronização.
//
// Como testar:
//   1. Abra um terminal → rode: Skua.SyncConsole.exe
//   2. Abra outro terminal → rode: Skua.SyncConsole.Teste.exe
//   3. Observe os logs de ambos

using System.Net.Sockets;
using System.Text;
using System.Text.Json;

Console.WriteLine("=== Simulador de Contas Skua ===");
Console.WriteLine("Simulando 3 contas com velocidades diferentes de farm.\n");

// Cria 3 contas em paralelo, cada uma com velocidade diferente
// Task.WhenAll = aguarda TODAS as tasks terminarem
await Task.WhenAll(
    SimularConta("ContaA", velocidadeMs: 300,  metaOrcs: 50, metaTrolls: 20),  // rápida
    SimularConta("ContaB", velocidadeMs: 800,  metaOrcs: 50, metaTrolls: 20),  // média
    SimularConta("ContaC", velocidadeMs: 1400, metaOrcs: 50, metaTrolls: 20)   // lenta
);

Console.WriteLine("\n=== Simulação concluída! ===");

// ----------------------------------------------------------------
// Simula uma conta completa (conecta, faz a quest, desconecta)
// ----------------------------------------------------------------

async Task SimularConta(string nome, int velocidadeMs, int metaOrcs, int metaTrolls)
{
    // Pequeno delay para as contas não conectarem exatamente ao mesmo tempo
    await Task.Delay(Random.Shared.Next(0, 500));

    using ClienteTCP cliente = new(nome);

    bool conectou = await cliente.ConectarAsync();
    if (!conectou)
    {
        Console.WriteLine($"[{nome}] FALHA ao conectar. SyncConsole está rodando?");
        return;
    }

    // === ETAPA 1: Matar Orcs em Ruinas ===
    Log(nome, "Indo para Ruinas...");
    int orcs = 0;

    while (true)
    {
        await Task.Delay(velocidadeMs);

        if (orcs < metaOrcs)
            orcs++;

        Log(nome, $"Orcs: {orcs}/{metaOrcs}");

        await cliente.EnviarProgressoAsync("etapa1_orcs", orcs, metaOrcs, "ruinas", "Orc");

        // Conta que terminou fica no loop ajudando — só sai quando coordenador liberar
        if (orcs >= metaOrcs && await cliente.ReceberGoAsync("etapa1_orcs"))
            break;
    }

    Log(nome, "Etapa 1 concluída! Indo para Escala...");
    await Task.Delay(500); // simula viagem de mapa

    // === ETAPA 2: Matar Trolls em Escala ===
    Log(nome, "Indo para Escala...");
    int trolls = 0;

    while (true)
    {
        await Task.Delay(velocidadeMs);

        if (trolls < metaTrolls)
            trolls++;

        Log(nome, $"Trolls: {trolls}/{metaTrolls}");

        await cliente.EnviarProgressoAsync("etapa2_trolls", trolls, metaTrolls, "escala", "Troll");

        if (trolls >= metaTrolls && await cliente.ReceberGoAsync("etapa2_trolls"))
            break;
    }

    Log(nome, "Quest completa!");
    await cliente.DesconectarAsync();
}

// ----------------------------------------------------------------
// Log colorido por conta
// ----------------------------------------------------------------

void Log(string conta, string msg)
{
    ConsoleColor cor = conta switch
    {
        "ContaA" => ConsoleColor.Cyan,
        "ContaB" => ConsoleColor.Green,
        "ContaC" => ConsoleColor.Yellow,
        _        => ConsoleColor.White
    };

    Console.ForegroundColor = cor;
    Console.WriteLine($"[{conta}] {msg}");
    Console.ResetColor();
}

// ================================================================
// ClienteTCP: versão simplificada do CoreSync para o simulador
// ================================================================

class ClienteTCP : IDisposable
{
    private readonly string _nome;
    private TcpClient? _tcp;
    private StreamWriter? _escritor;
    private StreamReader? _leitor;

    private readonly HashSet<string> _liberadas = new();
    private readonly object _lock = new();

    public ClienteTCP(string nome) => _nome = nome;

    public async Task<bool> ConectarAsync()
    {
        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync("localhost", 7352);

            NetworkStream stream = _tcp.GetStream();
            _escritor = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            _leitor   = new StreamReader(stream, Encoding.UTF8);

            await _escritor.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Cmd = "join",
                ContaId = _nome
            }));

            _ = Task.Run(LerMensagensAsync);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnviarProgressoAsync(string barreiraId, int atual, int necessario, string mapa, string monstro)
    {
        if (_escritor is null) return;
        try
        {
            await _escritor.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Cmd        = "progresso",
                ContaId    = _nome,
                BarreiraId = barreiraId,
                Atual      = atual,
                Necessario = necessario,
                Mapa       = mapa,
                Monstro    = monstro
            }));
        }
        catch { }
    }

    public Task<bool> ReceberGoAsync(string barreiraId)
    {
        lock (_lock)
            return Task.FromResult(_liberadas.Contains(barreiraId));
    }

    public async Task DesconectarAsync()
    {
        try
        {
            if (_escritor is not null)
                await _escritor.WriteLineAsync(JsonSerializer.Serialize(new { Cmd = "leave" }));
        }
        catch { }
        _tcp?.Close();
    }

    private async Task LerMensagensAsync()
    {
        try
        {
            while (true)
            {
                string? linha = await _leitor!.ReadLineAsync();
                if (linha is null) break;

                using JsonDocument doc = JsonDocument.Parse(linha);
                string cmd        = doc.RootElement.TryGetProperty("Cmd",        out JsonElement c) ? c.GetString() ?? "" : "";
                string barreiraId = doc.RootElement.TryGetProperty("BarreiraId", out JsonElement b) ? b.GetString() ?? "" : "";

                if (cmd == "go")
                {
                    lock (_lock)
                        _liberadas.Add(barreiraId);
                }
            }
        }
        catch { }
    }

    public void Dispose() => _tcp?.Close();
}
