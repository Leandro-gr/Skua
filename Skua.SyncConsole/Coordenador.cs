using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Skua.SyncConsole;

public class Coordenador
{
    private const int Porta = 7352;

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Contas conectadas: contaId → (grupoId, canal de escrita)
    private readonly ConcurrentDictionary<string, (string GrupoId, StreamWriter Escritor)> _contas = new();

    // Grupos registrados: grupoId → lista de contas esperadas
    // Populado pelo Manager antes de lançar as contas
    private readonly ConcurrentDictionary<string, HashSet<string>> _gruposEsperados = new();

    // Barreiras: "grupoId::barreiraId" → progresso de cada conta
    private readonly ConcurrentDictionary<string, EstadoBarreira> _barreiras = new();

    private readonly object _lock = new();

    public async Task IniciarAsync(CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Loopback, Porta);
        listener.Start();
        Console.WriteLine($"Coordenador ouvindo em localhost:{Porta}");
        Console.WriteLine("Aguardando grupos e contas...\n");

        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient cliente = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => ProcessarClienteAsync(cliente, token), token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
            Console.WriteLine("Coordenador encerrado.");
        }
    }

    private async Task ProcessarClienteAsync(TcpClient cliente, CancellationToken token)
    {
        string? contaId = null;
        string? grupoId = null;

        try
        {
            using var stream = cliente.GetStream();
            using var leitor = new StreamReader(stream, Encoding.UTF8);
            using var escritor = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            while (!token.IsCancellationRequested)
            {
                string? linha = await leitor.ReadLineAsync(token);
                if (linha is null) break;

                MensagemEntrada? msg = JsonSerializer.Deserialize<MensagemEntrada>(linha, _jsonOpts);
                if (msg is null) continue;

                switch (msg.Cmd)
                {
                    case "registrar_grupo":
                        // Manager registra o grupo antes de lançar as contas
                        if (msg.GrupoId is not null && msg.Contas is not null)
                        {
                            lock (_lock)
                                _gruposEsperados[msg.GrupoId] = new HashSet<string>(msg.Contas, StringComparer.OrdinalIgnoreCase);

                            Console.WriteLine($"[Grupo '{msg.GrupoId}'] Registrado — {msg.Contas.Count} conta(s): {string.Join(", ", msg.Contas)}");
                        }
                        return; // Manager fecha conexão após registrar

                    case "join":
                        contaId = msg.ContaId ?? "desconhecida";
                        grupoId = msg.GrupoId ?? "default";
                        _contas[contaId] = (grupoId, escritor);
                        Console.WriteLine($"[+] '{contaId}' entrou no grupo '{grupoId}'. No grupo agora: {_ContasNoGrupo(grupoId)}");
                        break;

                    case "progresso":
                        if (contaId is not null && grupoId is not null && msg.BarreiraId is not null)
                            await ProcessarProgressoAsync(contaId, grupoId, msg);
                        break;

                    case "leave":
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        finally
        {
            cliente.Dispose();
            if (contaId is not null)
            {
                _contas.TryRemove(contaId, out _);
                Console.WriteLine($"[-] '{contaId}' saiu do grupo '{grupoId}'. No grupo agora: {_ContasNoGrupo(grupoId ?? "")}");
            }
        }
    }

    private async Task ProcessarProgressoAsync(string contaId, string grupoId, MensagemEntrada msg)
    {
        string barreiraId = msg.BarreiraId!;
        string chave = $"{grupoId}::{barreiraId}"; // isola grupos diferentes

        int totalEsperado = _TotalEsperadoNoGrupo(grupoId);
        bool todasProntas;
        EstadoBarreira? estadoParaAjuda = null;

        lock (_lock)
        {
            EstadoBarreira estado = _barreiras.GetOrAdd(chave, _ => new EstadoBarreira(msg.Mapa, msg.Monstro));
            estado.Progresso[contaId] = (msg.Atual, msg.Necessario);

            int prontas = estado.Progresso.Values.Count(p => p.Atual >= p.Necessario);
            Console.WriteLine($"['{grupoId}'/'{barreiraId}'] {contaId}: {msg.Atual}/{msg.Necessario} | {prontas}/{totalEsperado} prontas");

            todasProntas = estado.Progresso.Count >= totalEsperado
                        && estado.Progresso.Values.All(p => p.Atual >= p.Necessario);

            if (!todasProntas && msg.Atual >= msg.Necessario)
                estadoParaAjuda = estado;
        }

        if (todasProntas)
        {
            _barreiras.TryRemove(chave, out _);
            Console.WriteLine($"['{grupoId}'/'{barreiraId}'] TODAS PRONTAS — liberando!\n");
            await BroadcastParaGrupoAsync(grupoId, new MensagemSaida("go", barreiraId, null, null, null));
        }
        else if (estadoParaAjuda?.Mapa is not null)
        {
            Console.WriteLine($"['{grupoId}'/'{barreiraId}'] '{contaId}' terminou — ajudando em {estadoParaAjuda.Mapa}");
            await EnviarParaContaAsync(contaId, new MensagemSaida("ajudar", barreiraId, estadoParaAjuda.Mapa, estadoParaAjuda.Monstro, null));
        }
    }

    private async Task EnviarParaContaAsync(string contaId, MensagemSaida mensagem)
    {
        if (_contas.TryGetValue(contaId, out (string GrupoId, StreamWriter Escritor) entrada))
        {
            string json = JsonSerializer.Serialize(mensagem);
            try { await entrada.Escritor.WriteLineAsync(json); }
            catch { }
        }
    }

    private async Task BroadcastParaGrupoAsync(string grupoId, MensagemSaida mensagem)
    {
        string json = JsonSerializer.Serialize(mensagem);
        foreach ((string _, (string GrupoId, StreamWriter Escritor) entrada) in _contas)
        {
            if (entrada.GrupoId != grupoId) continue;
            try { await entrada.Escritor.WriteLineAsync(json); }
            catch { }
        }
    }

    private int _ContasNoGrupo(string grupoId)
        => _contas.Values.Count(c => c.GrupoId == grupoId);

    private int _TotalEsperadoNoGrupo(string grupoId)
    {
        lock (_lock)
        {
            if (_gruposEsperados.TryGetValue(grupoId, out HashSet<string>? esperadas))
                return esperadas.Count;
        }
        return _ContasNoGrupo(grupoId);
    }
}

public class EstadoBarreira(string? mapa, string? monstro)
{
    public string? Mapa    { get; } = mapa;
    public string? Monstro { get; } = monstro;
    public Dictionary<string, (int Atual, int Necessario)> Progresso { get; } = [];
}
