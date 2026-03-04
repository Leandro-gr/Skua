namespace Skua.SyncConsole;

// Mensagem que as CONTAS (ou o Manager) enviam para o COORDENADOR
public record MensagemEntrada(
    string Cmd,               // "registrar_grupo" | "join" | "progresso" | "leave"
    string? GrupoId,          // id do grupo (ex: "Farm Squad")
    string? ContaId,          // nome/id da conta (ex: "ContaA")
    List<string>? Contas,     // usado em "registrar_grupo": lista de contas esperadas
    string? BarreiraId,       // id da etapa (ex: "orcs_ruinas")
    int Atual,                // quantidade atual
    int Necessario,           // quantidade necessária
    string? Mapa,             // mapa onde a etapa acontece
    string? Monstro           // monstro a matar
);

// Mensagem que o COORDENADOR envia para as CONTAS
public record MensagemSaida(
    string Cmd,               // "go" | "ajudar" | "info"
    string? BarreiraId,       // qual barreira foi liberada
    string? Mapa,             // mapa de ajuda (quando Cmd = "ajudar")
    string? Monstro,          // monstro de ajuda (quando Cmd = "ajudar")
    string? Texto             // mensagem opcional para log
);
