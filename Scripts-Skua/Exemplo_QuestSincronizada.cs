/*
name: Exemplo Quest Sincronizada
description: Demonstra como usar CoreSync para múltiplas contas trabalharem juntas.
             Quest hipotética: matar 50 Orcs em Ruinas → matar 20 Trolls em Escala.
             Contas que terminam primeiro ficam ajudando as outras até todas estarem prontas.
tags: exemplo, sync, multi-conta
version: 0.1.0
*/

//cs_include Scripts/CoreSync.cs

using Skua.Core.Interfaces;

public class ExemploQuestSincronizada
{
    private IScriptInterface Bot => IScriptInterface.Instance;

    public void ScriptMain(IScriptInterface bot)
    {
        // Cria o cliente de sync usando o nome do personagem como ID
        var Sync = new CoreSync(Bot, Bot.Player.Username);

        // Conecta ao SyncConsole (precisa estar rodando!)
        if (!Sync.Conectar())
        {
            Bot.Log("SyncConsole não encontrado. Rodando solo.");
            RodarSolo();
            return;
        }

        try
        {
            RodarSincronizado(Sync);
        }
        finally
        {
            // Sempre desconecta ao terminar (mesmo se der erro)
            Sync.Desconectar();
        }
    }

    // ------------------------------------------------------------------
    // Modo sincronizado (com múltiplas contas)
    // ------------------------------------------------------------------

    void RodarSincronizado(CoreSync Sync)
    {
        // === ETAPA 1: Matar 50 Orcs em Ruinas ===
        Bot.Log("Etapa 1: Indo para Ruinas...");
        Bot.Map.Join("ruinas", "r1", "Left");
        Bot.Wait.ForMapLoad("ruinas");

        // O loop continua até que TODAS as contas tenham 50 Orc Skull.
        // Contas que terminam primeiro continuam matando Orcs (ajudando as outras).
        while (!Bot.ShouldExit)
        {
            Bot.Combat.Kill("Orc");
            int skulls = Bot.Inventory.GetQuantity("Orc Skull");

            // Sync.Pronto() envia o progresso e retorna true quando todos terminaram
            if (Sync.Pronto("etapa1_orcs", skulls, 50, "ruinas", "Orc"))
                break;
        }

        if (Bot.ShouldExit) return;
        Bot.Log("Etapa 1 concluída por todas as contas!");

        // === ETAPA 2: Matar 20 Trolls em Escala ===
        Bot.Log("Etapa 2: Indo para Escala...");
        Bot.Map.Join("escala", "e1", "Left");
        Bot.Wait.ForMapLoad("escala");

        while (!Bot.ShouldExit)
        {
            Bot.Combat.Kill("Troll");
            int tails = Bot.Inventory.GetQuantity("Troll Tail");

            if (Sync.Pronto("etapa2_trolls", tails, 20, "escala", "Troll"))
                break;
        }

        if (Bot.ShouldExit) return;
        Bot.Log("Etapa 2 concluída por todas as contas!");

        // === FINALIZAR QUEST ===
        Bot.Map.Join("cidade", "ci1", "Left");
        Bot.Quests.EnsureAccept(1234);
        Bot.Quests.EnsureComplete(1234);

        Bot.Log("Quest finalizada com todas as contas em sincronia!");
    }

    // ------------------------------------------------------------------
    // Modo solo (fallback se SyncConsole não estiver rodando)
    // ------------------------------------------------------------------

    void RodarSolo()
    {
        Bot.Map.Join("ruinas", "r1", "Left");
        while (!Bot.ShouldExit && Bot.Inventory.GetQuantity("Orc Skull") < 50)
            Bot.Combat.Kill("Orc");

        Bot.Map.Join("escala", "e1", "Left");
        while (!Bot.ShouldExit && Bot.Inventory.GetQuantity("Troll Tail") < 20)
            Bot.Combat.Kill("Troll");

        Bot.Map.Join("cidade", "ci1", "Left");
        Bot.Quests.EnsureAccept(1234);
        Bot.Quests.EnsureComplete(1234);
    }
}
