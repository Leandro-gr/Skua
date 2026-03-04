/*
name: CoreSync
description: Cliente de sincronização multi-conta via TCP. Conecta ao Skua.SyncConsole e coordena progresso entre contas do mesmo grupo.
tags: sync, multi-conta, army, barreira
version: 0.2.0
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Skua.Core.Interfaces;

/// <summary>
/// Cliente de sincronização por grupo. Use um grupoId diferente para cada party.
///
/// Exemplo:
///   var Sync = new CoreSync(Bot, Bot.Player.Username, "Farm Squad");
///   Sync.Conectar();
///
///   while (!Bot.ShouldExit)
///   {
///       Bot.Combat.Kill("Orc");
///       if (Sync.Pronto("orcs", Bot.Inventory.GetQuantity("Orc Skull"), 50, "ruinas", "Orc")) break;
///   }
///
///   Sync.Desconectar();
/// </summary>
public class CoreSync : IDisposable
{
    private const string Host = "localhost";
    private const int Porta = 7352;
    private const int IntervaloEnvioMs = 1500;

    private readonly IScriptInterface _bot;
    private readonly string _contaId;
    private readonly string _grupoId;

    private TcpClient? _tcp;
    private StreamWriter? _escritor;
    private readonly CancellationTokenSource _cts = new();

    private readonly HashSet<string> _liberadas = new();
    private readonly Dictionary<string, (int UltimoAtual, long UltimoEnvioMs)> _ultimoProgresso = new();
    private readonly object _lock = new();

    public CoreSync(IScriptInterface bot, string contaId, string grupoId)
    {
        _bot = bot;
        _contaId = contaId;
        _grupoId = grupoId;
    }

    public bool Conectar()
    {
        try
        {
            _tcp = new TcpClient(Host, Porta);
            NetworkStream stream = _tcp.GetStream();
            _escritor = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            StreamReader leitor = new StreamReader(stream, Encoding.UTF8);

            _EnviarJson(new { Cmd = "join", ContaId = _contaId, GrupoId = _grupoId });
            _bot.Log($"[Sync] '{_contaId}' entrou no grupo '{_grupoId}'.");

            new Thread(() => _LerMensagens(leitor))
            {
                IsBackground = true,
                Name = "CoreSync.Leitor"
            }.Start();

            return true;
        }
        catch (Exception ex)
        {
            _bot.Log($"[Sync] ERRO ao conectar: {ex.Message}");
            _bot.Log("[Sync] O SyncConsole.exe está rodando?");
            return false;
        }
    }

    /// <summary>
    /// Reporta progresso e retorna true quando TODAS as contas do grupo terminaram.
    /// Chame dentro do loop de farm — a conta continua matando até o sinal chegar.
    /// </summary>
    public bool Pronto(string barreiraId, int atual, int necessario, string mapa, string monstro)
    {
        lock (_lock)
        {
            if (_liberadas.Contains(barreiraId))
                return true;
        }

        bool deveEnviar = false;
        long agora = Environment.TickCount64;

        lock (_lock)
        {
            if (_ultimoProgresso.TryGetValue(barreiraId, out (int UltimoAtual, long UltimoEnvioMs) ultimo))
                deveEnviar = atual != ultimo.UltimoAtual || (agora - ultimo.UltimoEnvioMs) >= IntervaloEnvioMs;
            else
                deveEnviar = true;

            if (deveEnviar)
                _ultimoProgresso[barreiraId] = (atual, agora);
        }

        if (deveEnviar)
        {
            _EnviarJson(new
            {
                Cmd        = "progresso",
                ContaId    = _contaId,
                GrupoId    = _grupoId,
                BarreiraId = barreiraId,
                Atual      = atual,
                Necessario = necessario,
                Mapa       = mapa,
                Monstro    = monstro
            });
        }

        return false;
    }

    public void Desconectar()
    {
        _EnviarJson(new { Cmd = "leave" });
        _cts.Cancel();
        _tcp?.Close();
        _bot.Log("[Sync] Desconectado.");
    }

    public void Dispose() => Desconectar();

    private void _LerMensagens(StreamReader leitor)
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                string? linha = leitor.ReadLine();
                if (linha is null) break;
                _ProcessarMensagem(linha);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        catch (Exception ex) { _bot.Log($"[Sync] Erro na leitura: {ex.Message}"); }
    }

    private void _ProcessarMensagem(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string cmd        = root.TryGetProperty("Cmd",        out JsonElement c) ? c.GetString() ?? "" : "";
            string barreiraId = root.TryGetProperty("BarreiraId", out JsonElement b) ? b.GetString() ?? "" : "";
            string mapa       = root.TryGetProperty("Mapa",       out JsonElement m) ? m.GetString() ?? "" : "";

            switch (cmd)
            {
                case "go":
                    lock (_lock) _liberadas.Add(barreiraId);
                    _bot.Log($"[Sync] '{barreiraId}' LIBERADA — todos prontos!");
                    break;
                case "ajudar":
                    _bot.Log($"[Sync] Aguardando grupo em '{barreiraId}' — continuando em {mapa}...");
                    break;
            }
        }
        catch (JsonException) { }
    }

    private void _EnviarJson(object obj)
    {
        try { _escritor?.WriteLine(JsonSerializer.Serialize(obj)); }
        catch { }
    }
}
