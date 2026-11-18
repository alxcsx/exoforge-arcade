using System;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

[ExoExtension("adventure_world", Version = "0.1.0", Category = "Game", Title = "Adventure World", Icon = "🗡️")]
public class AdventureWorldExtension
{
    [ExoAction(Scope = "service")]
    public AdventurerProgress ReportKill(string playerId, string name, string enemy, string zone)
    {
        var progress = Load(playerId, name);
        progress = progress with { Kills = progress.Kills + 1, UpdatedAt = Now() };
        Exo.Database.Put(progress);
        Exo.Events.EmitAsync(new EnemySlain(playerId, name, enemy, zone, Now())).GetAwaiter().GetResult();
        return progress;
    }

    [ExoAction(Scope = "service")]
    public AdventurerProgress ReportChest(string playerId, string name, string zone, int gold)
    {
        var progress = Load(playerId, name);
        progress = progress with { Chests = progress.Chests + 1, Gold = progress.Gold + gold, UpdatedAt = Now() };
        Exo.Database.Put(progress);
        Exo.Events.EmitAsync(new ChestOpened(playerId, name, zone, gold, Now())).GetAwaiter().GetResult();
        return progress;
    }

    [ExoAction(Scope = "service")]
    public AdventurerProgress ReportZone(string playerId, string name, string zone)
    {
        var progress = Load(playerId, name);
        progress = progress with { Zones = progress.Zones + 1, Zone = zone, UpdatedAt = Now() };
        Exo.Database.Put(progress);
        Exo.Events.EmitAsync(new ZoneEntered(playerId, name, zone, Now())).GetAwaiter().GetResult();
        return progress;
    }

    [ExoAction(Scope = "player")]
    public AdventurerProgress GetProgress()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");

        return Load(playerId, "");
    }

    private static AdventurerProgress Load(string playerId, string name)
    {
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("invalid_player", "player_id is required.");

        var existing = Exo.Database.Get<AdventurerProgress>(playerId);
        if (existing == null) return new AdventurerProgress(playerId, name, 0, 0, 0, 0, "", Now());
        return string.IsNullOrEmpty(name) ? existing : existing with { Name = name };
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

[ExoResource("adventurer_progress")]
public record AdventurerProgress(string Id, string Name, int Kills, int Chests, int Zones, int Gold, string Zone, long UpdatedAt);

[ExoEvent("enemy_slain", Topic = "world:feed")]
public record EnemySlain(string PlayerId, string Name, string Enemy, string Zone, long Timestamp);

[ExoEvent("chest_opened", Topic = "world:feed")]
public record ChestOpened(string PlayerId, string Name, string Zone, int Gold, long Timestamp);

[ExoEvent("zone_entered", Topic = "world:feed")]
public record ZoneEntered(string PlayerId, string Name, string Zone, long Timestamp);
