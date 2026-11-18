using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Xunit;

public class AdventureWorldTests
{
    [Fact]
    public void Kills_accumulate_per_player_and_emit_events()
    {
        using var host = ExoTestHost.Start("adventure_world");
        var world = new AdventureWorldExtension();

        world.ReportKill("p1", "Ada", "slime", "cave");
        var progress = world.ReportKill("p1", "Ada", "slime", "cave");
        world.ReportKill("p2", "Bob", "slime", "cave");

        Assert.Equal(2, progress.Kills);
        Assert.Equal(1, Exo.Database.Get<AdventurerProgress>("p2")!.Kills);
        Assert.Equal(3, host.Events.EmittedEvents.Count);
    }
}

public class AdventureWorldProgressTests
{
    [Fact]
    public void Chests_and_zones_accumulate()
    {
        using var host = ExoTestHost.Start("adventure_world");
        var world = new AdventureWorldExtension();

        world.ReportChest("p1", "Ada", "cave", 25);
        world.ReportChest("p1", "Ada", "cave", 10);
        var progress = world.ReportZone("p1", "Ada", "crypt");

        Assert.Equal(2, progress.Chests);
        Assert.Equal(35, progress.Gold);
        Assert.Equal(1, progress.Zones);
        Assert.Equal("crypt", progress.Zone);
        Assert.Equal(3, host.Events.EmittedEvents.Count);
    }

    [Fact]
    public void Report_without_player_is_refused()
    {
        using var host = ExoTestHost.Start("adventure_world");
        Assert.Throws<ExoActionException>(() => new AdventureWorldExtension().ReportKill("", "Ada", "slime", "cave"));
    }

    [Fact]
    public void Get_progress_needs_a_player_session()
    {
        using var host = ExoTestHost.Start("adventure_world");
        Assert.Throws<ExoActionException>(() => new AdventureWorldExtension().GetProgress());
    }
}
