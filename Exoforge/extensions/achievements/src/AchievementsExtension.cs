using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

[ExoExtension("achievements", Version = "0.1.0", Category = "Game", Title = "Achievements", Icon = "🏆",
    Resources = [typeof(AchievementDef), typeof(AchievementProgress), typeof(AchievementUnlock)])]
public class AchievementsExtension
{
    [ExoAction]
    public AchievementStatus[] List()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");

        var unlocked = Exo.Database.All<AchievementUnlock>().Where(u => u.PlayerId == playerId).ToDictionary(u => u.AchievementId);
        var progress = Exo.Database.All<AchievementProgress>().Where(p => p.PlayerId == playerId).ToDictionary(p => p.Trigger);

        return Exo.Database.All<AchievementDef>()
            .OrderBy(d => d.Id)
            .Select(d => new AchievementStatus(d.Id, d.Title, d.Description, d.Count,
                Math.Min(d.Count, progress.TryGetValue(d.Trigger, out var p) ? p.Count : 0), unlocked.ContainsKey(d.Id)))
            .ToArray();
    }

    public void OnExtensionStart()
    {
        if (Exo.Database.All<AchievementDef>().Any()) return;

        Exo.Database.Put(new AchievementDef("slime_slayer", "Slime Slayer", "Defeat 3 slimes.", "enemy_slain", 3));
        Exo.Database.Put(new AchievementDef("treasure_hunter", "Treasure Hunter", "Open a chest.", "chest_opened", 1));
        Exo.Database.Put(new AchievementDef("explorer", "Explorer", "Enter 2 rooms.", "zone_entered", 2));
        Exo.Database.Put(new AchievementDef("diamond_hands", "Diamond Hands", "Make your first trade.", "trade_made", 1));
        Exo.Database.Put(new AchievementDef("level_maker", "Level Maker", "Publish a Doodle Dash level.", "level_published", 1));
    }

    public void OnEvent(string name, JsonElement payload)
    {
        if (name == "achievement_unlocked") return;
        if (!payload.TryGetProperty("player_id", out var id) || id.GetString() is not { Length: > 0 } playerId) return;
        Apply(playerId, name);
    }

    private static void Apply(string playerId, string trigger)
    {
        if (!Exo.Database.All<AchievementDef>().Any(d => d.Trigger == trigger)) return;

        Exo.Logger.Info($"event {trigger} from {playerId}");
        var key = $"{playerId}:{trigger}";
        var count = (Exo.Database.Get<AchievementProgress>(key)?.Count ?? 0) + 1;
        Exo.Database.Put(new AchievementProgress(key, playerId, trigger, count));

        foreach (var def in Exo.Database.All<AchievementDef>().Where(d => d.Trigger == trigger && d.Count <= count))
        {
            var unlockKey = $"{playerId}:{def.Id}";
            if (Exo.Database.Get<AchievementUnlock>(unlockKey) != null) continue;

            Exo.Database.Put(new AchievementUnlock(unlockKey, playerId, def.Id, DateTime.UtcNow));
            Exo.Logger.Info($"unlocked {def.Id} for {playerId}");
            Exo.Events.EmitAsync(new AchievementUnlocked(playerId, def.Id, def.Title)).GetAwaiter().GetResult();
        }
    }
}

[ExoEvent("enemy_slain", Topic = "world:feed")]
public record EnemySlain(string PlayerId);
[ExoEvent("chest_opened", Topic = "world:feed")]
public record ChestOpened(string PlayerId);
[ExoEvent("zone_entered", Topic = "world:feed")]
public record ZoneEntered(string PlayerId);
[ExoEvent("trade_made", Topic = "market:trades")]
public record TradeMade(string PlayerId);
[ExoEvent("level_published", Topic = "level_share:feed")]
public record LevelPublished(string PlayerId);

[ExoEvent("achievement_unlocked", Topic = "achievements:feed")]
public record AchievementUnlocked(string PlayerId, string AchievementId, string Title);

[ExoResource("achievement_defs", PrimaryKey = nameof(Id), AllowPlayerRead = true)]
public record AchievementDef(
    [property: ExoColumn(Label = "Id")] string Id,
    [property: ExoColumn(Label = "Title", Sortable = true)] string Title,
    [property: ExoColumn(Label = "Description")] string Description,
    [property: ExoColumn(Label = "Event", Filterable = true)] string Trigger,
    [property: ExoColumn(Label = "Count needed", Sortable = true)] int Count);

[ExoResource("achievement_progress", PrimaryKey = nameof(Id))]
public record AchievementProgress(
    [property: ExoColumn(Label = "Id")] string Id,
    [property: ExoColumn(Label = "Player", Filterable = true, Role = "user_id")] string PlayerId,
    [property: ExoColumn(Label = "Event", Filterable = true)] string Trigger,
    [property: ExoColumn(Label = "Count", Sortable = true)] int Count);

[ExoResource("unlocks", PrimaryKey = nameof(Id))]
public record AchievementUnlock(
    [property: ExoColumn(Label = "Id")] string Id,
    [property: ExoColumn(Label = "Player", Filterable = true, Role = "user_id")] string PlayerId,
    [property: ExoColumn(Label = "Achievement", Filterable = true)] string AchievementId,
    [property: ExoColumn(Label = "Unlocked at", Sortable = true)] DateTime UnlockedAt);

public record AchievementStatus(string Id, string Title, string Description, int Needed, int Progress, bool Unlocked);
