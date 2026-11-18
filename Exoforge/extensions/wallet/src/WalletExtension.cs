using System;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

[ExoExtension("wallet", Version = "0.1.0", Category = "Game", Title = "Wallet", Icon = "🪙", Resources = [typeof(WalletBalance)])]
public class WalletExtension
{
    [ExoAction(Scope = "player")]
    public WalletBalance GetBalance() => Read(Caller());

    [ExoAction(Scope = "server")]
    public WalletBalance Credit(string playerId, int amount, string reason)
    {
        if (amount <= 0) throw new ExoActionException("invalid_amount", "Amount must be positive.");

        var balance = Read(playerId);
        int total;
        try { total = checked(balance.Coins + amount); }
        catch (OverflowException) { throw new ExoActionException("balance_overflow", "The balance cannot hold that many coins."); }

        return Save(balance with { Coins = total });
    }

    [ExoAction(Scope = "server")]
    public WalletBalance Debit(string playerId, int amount, string reason)
    {
        if (amount <= 0) throw new ExoActionException("invalid_amount", "Amount must be positive.");

        var balance = Read(playerId);
        if (balance.Coins < amount)
            throw new ExoActionException("insufficient_funds", $"Needs {amount} coins, has {balance.Coins}.");

        return Save(balance with { Coins = balance.Coins - amount });
    }

    private static string Caller()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");
        return playerId;
    }

    private static WalletBalance Read(string playerId) =>
        Exo.Database.Get<WalletBalance>(playerId) ?? new WalletBalance(playerId, 0, DateTime.UtcNow);

    private static WalletBalance Save(WalletBalance balance)
    {
        var saved = balance with { UpdatedAt = DateTime.UtcNow };
        Exo.Database.Put(saved);
        return saved;
    }
}

[ExoResource("wallet_balances", PrimaryKey = nameof(PlayerId))]
public record WalletBalance(
    [property: ExoColumn(Sortable = true, Filterable = true)] string PlayerId,
    [property: ExoColumn(Sortable = true)] int Coins,
    DateTime UpdatedAt
);
