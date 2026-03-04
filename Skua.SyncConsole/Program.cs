namespace Skua.SyncConsole;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("=== Skua SyncConsole ===");
        Console.WriteLine("Coordenador de barreira multi-conta");
        Console.WriteLine("Pressione Ctrl+C para encerrar.\n");

        // CancellationTokenSource é a "fonte" do cancelamento
        var cts = new CancellationTokenSource();

        // Quando Ctrl+C for pressionado, cancela o token
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // impede o processo de fechar abruptamente
            cts.Cancel();    // sinaliza cancelamento para o coordenador
        };

        var coordenador = new Coordenador();

        // await aqui = aguarda o coordenador rodar até ser cancelado
        await coordenador.IniciarAsync(cts.Token);
    }
}
