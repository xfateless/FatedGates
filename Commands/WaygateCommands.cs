using FatedGates.Commands.Converters;
using Stunlock.Core;
using VampireCommandFramework;
using Unity.Transforms;

namespace FatedGates.Commands;

// Commands for managing waygates.
[CommandGroup("waygate", "wg")]
class WaygateCommands
{
    readonly static PrefabGUID defaultWaygate = new (2107199037);

    [Command("create", "c", description: "Creates a discoverable waygate at the player's location.", adminOnly: true)]
    public static void CreateWaygate(ChatCommandContext ctx, FoundWaygatePrefab foundWaygatePrefab = null)
    {
        if(!Core.WaygateService.CreateWaygate(ctx.Event.SenderCharacterEntity, foundWaygatePrefab?.Value ?? defaultWaygate))
            ctx.Reply("Current chunk already has a waygate");
        else
            ctx.Reply("Discoverable waygate created");
    }

    [Command("createpublic", "cp", description: "Creates a waygate and unlocks it for all connected users and future connecting users.", adminOnly: true)]
    public static void CreateUnlockedWaygate(ChatCommandContext ctx, string waygateName = "Public Waygate", FoundWaygatePrefab foundWaygatePrefab = null)
    {
        if (!Core.WaygateService.CreateWaygate(
                ctx.Event.SenderCharacterEntity,
                foundWaygatePrefab?.Value ?? defaultWaygate,
                autoUnlockForAllConnectedUsers: true,
                waygateName
            ))
        {
            ctx.Reply("Current chunk already has a waygate");
        }
        else
        {
            ctx.Reply("Auto-unlocked waygate created and unlocked for all connected users");
        }
    }

    [Command("teleportclosest", "tpc", description: "Teleports the player to the closest spawned waygate.", adminOnly: true)]
    public static void TeleportToClosestWaygate(ChatCommandContext ctx)
    {
        if(Core.WaygateService.TeleportToClosestWaygate(ctx.Event.SenderCharacterEntity))
            ctx.Reply("Teleported to closest spawned waygate");
        else
            ctx.Reply("No spawned waygate to teleport to");
    }

    [Command("destroy", "d", description: "Destroys the closest spawned waygate, or a saved public waygate by index.", adminOnly: true)]
    public static void DestroyWaygate(ChatCommandContext ctx, int publicWaygateIndex = 0)
    {
        if (Core.WaygateService.DestroyWaygate(ctx.Event.SenderCharacterEntity, publicWaygateIndex))
        {
            ctx.Reply(
                publicWaygateIndex > 0
                ? $"Destroyed public waygate #{publicWaygateIndex}."
                : "Destroyed closest spawned waygate."
            );
        }
        else
        {
            ctx.Reply(
                publicWaygateIndex > 0
                ? $"Could not destroy public waygate #{publicWaygateIndex}. Use .waygate listpublic to check indexes."
                : "Not standing near a spawned waygate."
            );
        }
    }

    [Command("listpublic", "lp", description: "Lists saved public waygates.", adminOnly: true)]
    public static void ListPublicWaygates(ChatCommandContext ctx)
    {
        ctx.Reply(Core.WaygateService.GetPublicWaygateListText());
    }
}
