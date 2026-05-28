using FatedGates.Commands.Converters;
using Stunlock.Core;
using VampireCommandFramework;
using Unity.Transforms;

namespace FatedGates.Commands;

// Commands for managing reveal services.
[CommandGroup("reveal", "rv")]
class RevealCommands
{
    [Command("zone", "z", description: "Adds a persistent public reveal zone by map bitmap coordinates.", adminOnly: true)]
    public static void AddRevealZone(ChatCommandContext ctx, int x, int y, int radius = 1)
    {
        Core.RevealService.AddPublicRevealZone(x, y, radius, $"Reveal Zone {x},{y}");
        ctx.Reply($"Added public reveal zone at ({x}, {y}) radius {radius} and applied it to connected users.");
    }

    [Command("pos", "p", description: "Shows your world position and reveal bitmap position.", adminOnly: true)]
    public static void PrintPosition(ChatCommandContext ctx)
    {
        var pos = ctx.Event.SenderCharacterEntity.Read<Translation>().Value;
        var (bitmapX, bitmapY) = Core.RevealService.WorldPositionToRevealBitmap(pos);

        ctx.Reply(
            $"World position: X={pos.x:F2}, Y={pos.y:F2}, Z={pos.z:F2}\n" +
            $"Reveal bitmap position: X={bitmapX}, Y={bitmapY}\n" +
            $"Use: \".rv zone {bitmapX} {bitmapY} 1\" to reveal here with a radius of 1 unit\n" +
            "Or simply use \".rv here 1\" to reveal at current player location with a radius of 1"
        );

        Core.Log.LogInfo(
            $"Position for {ctx.Event.User.CharacterName}: " +
            $"World X={pos.x:F4}, Y={pos.y:F4}, Z={pos.z:F4}; " +
            $"Reveal bitmap X={bitmapX}, Y={bitmapY}"
        );
    }

    [Command("here", "h", description: "Adds a persistent public reveal zone centered on your current position.", adminOnly: true)]
    public static void AddRevealZoneHere(ChatCommandContext ctx, int radius = 1)
    {
        var pos = ctx.Event.SenderCharacterEntity.Read<Translation>().Value;
        var (x, y) = Core.RevealService.WorldPositionToRevealBitmap(pos);

        Core.RevealService.AddPublicRevealZone(
            x,
            y,
            radius,
            $"Reveal Zone {x},{y}"
        );

        ctx.Reply($"Added public reveal zone at your position: bitmap ({x}, {y}) radius {radius}.");
    }

    [Command("closest", "c", description: "Adds a persistent public reveal zone centered on the closest spawned waygate.", adminOnly: true)]
    public static void AddRevealZoneAtClosestWaygate(ChatCommandContext ctx, int radius = 1)
    {
        if (Core.RevealService.AddRevealZoneAtClosestWaygate(ctx.Event.SenderCharacterEntity, radius))
        {
            ctx.Reply($"Added public reveal zone at closest waygate with radius {radius}.");
        }
        else
        {
            ctx.Reply("Could not add reveal zone at closest waygate. Check BepInEx/LogOutput.log for details.");
        }
    }

    [Command("list", "l", description: "Lists saved public reveal zones.", adminOnly: true)]
    public static void ListRevealZones(ChatCommandContext ctx)
    {
        ctx.Reply(Core.RevealService.GetPublicRevealZoneListText());
    }

    [Command("remove", "r", description: "Removes a saved public reveal zone by index, or the closest one if no index is provided.", adminOnly: true)]
    public static void RemoveRevealZone(ChatCommandContext ctx, int index = 0)
    {
        Core.RevealService.RemovePublicRevealZone(ctx.Event.SenderCharacterEntity, index, out var message);
        ctx.Reply(message);
    }
}
