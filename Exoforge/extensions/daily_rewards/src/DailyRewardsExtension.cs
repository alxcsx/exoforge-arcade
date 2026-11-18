using System;
using System.Linq;
using System.Threading.Tasks;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoDependency("wallet")]
[ExoExtension("daily_rewards", Version = "0.1.0", Category = "Game", Title = "Daily Rewards", Icon = "🎁",
    Resources = [typeof(DailyReward), typeof(DailyClaim)])]
public class DailyRewardsExtension
{
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public void OnExtensionStart()
    {
        if (Exo.Database.All<DailyReward>().Count > 0) return;

        int[] coins = [100, 150, 200, 300, 400, 600, 1000];
        for (var i = 0; i < coins.Length; i++)
            Exo.Database.Put(new DailyReward(i + 1, coins[i]));
    }

    [ExoAction(Scope = "player")]
    public DailyStatus GetStatus()
    {
        var claim = Exo.Database.Get<DailyClaim>(Caller());
        return StatusOf(claim, Clock().Date);
    }

    [ExoAction(Scope = "player")]
    public async Task<DailyClaimResult> Claim()
    {
        var playerId = Caller();
        var today = Clock().Date;
        var claim = Exo.Database.Get<DailyClaim>(playerId);
        var status = StatusOf(claim, today);
        if (!status.CanClaim)
            throw new ExoActionException("already_claimed", "Today's reward was already claimed.");

        var rewards = Exo.Database.All<DailyReward>().OrderBy(r => r.Day).ToList();
        if (rewards.Count == 0)
            throw new ExoActionException("no_rewards", "The daily reward table is empty.");

        var reward = rewards[(status.NextDay - 1) % rewards.Count];
        var balance = await ExoServices.Wallet.CreditAsync(new WalletCreditRequest
        {
            PlayerId = playerId,
            Amount = reward.Coins,
            Reason = $"daily_reward_day_{status.NextDay}"
        }) ?? throw new ExoActionException("wallet_unavailable", "The wallet did not answer.");

        Exo.Database.Put(new DailyClaim(playerId, status.NextDay, today));
        return new DailyClaimResult(status.NextDay, reward.Coins, balance.Coins);
    }

    private static DailyStatus StatusOf(DailyClaim? claim, DateTime today)
    {
        if (claim == null) return new DailyStatus(0, 1, true);
        if (claim.LastClaimedOn.Date == today) return new DailyStatus(claim.Streak, claim.Streak + 1, false);
        if (claim.LastClaimedOn.Date == today.AddDays(-1)) return new DailyStatus(claim.Streak, claim.Streak + 1, true);
        return new DailyStatus(0, 1, true);
    }

    private static string Caller()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");
        return playerId;
    }
}

public record DailyStatus(int Streak, int NextDay, bool CanClaim);
public record DailyClaimResult(int Day, int Coins, long Balance);

[ExoResource("daily_reward_table", PrimaryKey = nameof(Day), AllowPlayerRead = true)]
public record DailyReward(
    [property: ExoColumn(Sortable = true)] int Day,
    int Coins
);

[ExoResource("daily_claims", PrimaryKey = nameof(PlayerId))]
public record DailyClaim(
    [property: ExoColumn(Filterable = true)] string PlayerId,
    int Streak,
    DateTime LastClaimedOn
);
