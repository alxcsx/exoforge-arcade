using System.Linq;
using System.Text.Json;
using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Xunit;

namespace Exoforge.Extensions.Tests;

public class AchievementsTests
{
    private static JsonElement Event(string playerId) => JsonSerializer.SerializeToElement(new { player_id = playerId });

    private static AchievementsExtension Started(out ExoTestContext ctx)
    {
        ctx = ExoTestHost.Start("achievements");
        var ext = ctx.Create<AchievementsExtension>();
        ext.OnExtensionStart();
        return ext;
    }

    [Fact]
    public void Startup_seeds_definitions_once()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;
        ext.OnExtensionStart();

        Assert.Equal(5, Exo.Database.All<AchievementDef>().Count());
    }

    [Fact]
    public void Unlocks_at_the_threshold_and_exactly_once()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        for (var i = 0; i < 6; i++) ext.OnEvent("enemy_slain", Event("p1"));

        Assert.Single(Exo.Database.All<AchievementUnlock>(), u => u.AchievementId == "slime_slayer");
        Assert.Single(ctx.Events.EmittedEvents, e => e.Payload is AchievementUnlocked);
    }

    [Fact]
    public void Below_the_threshold_nothing_unlocks()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        ext.OnEvent("enemy_slain", Event("p1"));
        ext.OnEvent("enemy_slain", Event("p1"));

        Assert.Empty(Exo.Database.All<AchievementUnlock>());
    }

    [Fact]
    public void Players_progress_separately()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        ext.OnEvent("enemy_slain", Event("p1"));
        ext.OnEvent("enemy_slain", Event("p2"));
        ext.OnEvent("enemy_slain", Event("p1"));
        ext.OnEvent("enemy_slain", Event("p2"));
        ext.OnEvent("enemy_slain", Event("p1"));

        var unlock = Assert.Single(Exo.Database.All<AchievementUnlock>());
        Assert.Equal("p1", unlock.PlayerId);
    }

    [Theory]
    [InlineData("chest_opened", "treasure_hunter")]
    [InlineData("trade_made", "diamond_hands")]
    [InlineData("level_published", "level_maker")]
    public void Each_source_unlocks_its_achievement(string eventName, string achievementId)
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        ext.OnEvent(eventName, Event("p1"));

        Assert.Contains(Exo.Database.All<AchievementUnlock>(), u => u.AchievementId == achievementId);
    }

    [Fact]
    public void A_row_added_in_studio_is_honoured_and_unknown_events_are_ignored()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;
        Exo.Database.Put(new AchievementDef("zone_hopper", "Zone Hopper", "Enter a room.", "zone_entered", 1));

        ext.OnEvent("zone_entered", Event("p1"));
        ext.OnEvent("something_else", Event("p1"));
        ext.OnEvent("achievement_unlocked", Event("p1"));

        Assert.Contains(Exo.Database.All<AchievementUnlock>(), u => u.AchievementId == "zone_hopper");
        Assert.DoesNotContain(Exo.Database.All<AchievementProgress>(), p => p.Trigger == "something_else");
    }

    [Fact]
    public void An_event_without_a_player_is_ignored()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        ext.OnEvent("enemy_slain", JsonSerializer.SerializeToElement(new { name = "nobody" }));

        Assert.Empty(Exo.Database.All<AchievementProgress>());
    }

    [Fact]
    public void List_shows_the_callers_progress_and_unlocks_only()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        ext.OnEvent("enemy_slain", Event("p1"));
        for (var i = 0; i < 3; i++) ext.OnEvent("enemy_slain", Event("p2"));

        ctx.CallerPlayerId = "p1";
        var slime = Assert.Single(ext.List(), s => s.Id == "slime_slayer");
        Assert.Equal((1, 3, false), (slime.Progress, slime.Needed, slime.Unlocked));

        ctx.CallerPlayerId = "p2";
        slime = Assert.Single(ext.List(), s => s.Id == "slime_slayer");
        Assert.Equal((3, 3, true), (slime.Progress, slime.Needed, slime.Unlocked));
    }

    [Fact]
    public void List_returns_every_definition_and_requires_a_player_session()
    {
        var ext = Started(out var ctx);
        using var _ = ctx;

        var unauthorized = Assert.Throws<ExoActionException>(ext.List);
        Assert.Equal("unauthorized", unauthorized.Code);

        ctx.CallerPlayerId = "p1";
        var all = ext.List();
        Assert.Equal(5, all.Length);
        Assert.DoesNotContain(all, s => s.Unlocked || s.Progress != 0);
    }
}
