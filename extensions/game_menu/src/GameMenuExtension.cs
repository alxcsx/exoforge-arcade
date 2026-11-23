using System;
using System.Threading.Tasks;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("game_menu", Version = "0.1.0", Category = "Game", Title = "Game Menu", Icon = "🎮",
    Resources = [typeof(MenuNews), typeof(MenuGift), typeof(PlayerSettings), typeof(GiftClaim)])]
public class GameMenuExtension
{
    private const string GiftId = "welcome";

    public void OnExtensionStart()
    {
        if (Exo.Database.All<MenuNews>().Count == 0)
            Exo.Database.Put(new MenuNews("welcome", "Welcome to Exoforge Arcade! Pick a game.", 1));

        if (Exo.Database.Get<MenuGift>(GiftId) == null)
            Exo.Database.Put(new MenuGift(GiftId, 0, "Welcome gift. Coins must be above zero."));
    }

    [ExoAction(Scope = "player")]
    public PlayerSettings GetSettings() =>
        Exo.Database.Get<PlayerSettings>(CallerId()) ?? new PlayerSettings(CallerId(), 80, false);

    [ExoAction(Scope = "player")]
    public PlayerSettings SaveSettings(int volume, bool colorBlind)
    {
        if (volume is < 0 or > 100)
            throw new ExoActionException("invalid_volume", "Volume must be between 0 and 100.");

        var settings = new PlayerSettings(CallerId(), volume, colorBlind);
        Exo.Database.Put(settings);
        return settings;
    }

    [ExoAction(Scope = "player")]
    public async Task<GiftReceipt> ClaimGift()
    {
        var playerId = CallerId();
        if (Exo.Database.Get<GiftClaim>(playerId) != null)
            throw new ExoActionException("already_claimed", "The welcome gift was already claimed.");

        var gift = Exo.Database.Get<MenuGift>(GiftId)
            ?? throw new ExoActionException("gift_missing", "There is no welcome gift right now.");

        Exo.Logger.Info($"claiming gift '{gift.Id}' worth {gift.Coins} for {playerId}");

        WalletBalance? credited;
        try
        {
            credited = await ExoServices.Wallet.CreditAsync(
                new WalletCreditRequest { PlayerId = playerId, Amount = gift.Coins, Reason = "welcome_gift" });
        }
        catch (ExoActionException ex)
        {
            Exo.Logger.Info($"wallet refused gift '{gift.Id}' for {playerId}: {ex.Code} {ex.Message}");
            throw new ExoActionException("wallet_rejected",
                $"The wallet refused the welcome gift ({ex.Code}): {ex.Message}");
        }

        var balance = credited
            ?? throw new ExoActionException("wallet_rejected", "The wallet did not answer with a balance.");

        Exo.Database.Put(new GiftClaim(playerId, gift.Coins, DateTime.UtcNow));
        return new GiftReceipt(gift.Coins, (int)balance.Coins);
    }

    private static string CallerId() =>
        Exo.Context.CallerPlayerId
        ?? throw new ExoActionException("unauthorized", "A player session is required.");
}

[ExoResource("menu_news", PrimaryKey = nameof(Id), AllowPlayerRead = true)]
public record MenuNews(
    [property: ExoColumn(Label = "Id", Filterable = true)] string Id,
    [property: ExoColumn(Label = "Headline")] string Headline,
    [property: ExoColumn(Label = "Priority", Sortable = true)] int Priority
);

[ExoResource("menu_gifts", PrimaryKey = nameof(Id))]
public record MenuGift(
    [property: ExoColumn(Label = "Id", Filterable = true)] string Id,
    [property: ExoColumn(Label = "Coins", Sortable = true)] int Coins,
    [property: ExoColumn(Label = "Note")] string Note
);

[ExoResource("player_settings", PrimaryKey = nameof(PlayerId))]
public record PlayerSettings(
    [property: ExoColumn(Label = "Player ID", Filterable = true, Role = "user_id")] string PlayerId,
    [property: ExoColumn(Label = "Volume")] int Volume,
    [property: ExoColumn(Label = "Colour-blind mode")] bool ColorBlind
);

[ExoResource("gift_claims", PrimaryKey = nameof(PlayerId))]
public record GiftClaim(
    [property: ExoColumn(Label = "Player ID", Filterable = true, Role = "user_id")] string PlayerId,
    [property: ExoColumn(Label = "Coins")] int Coins,
    [property: ExoColumn(Label = "Claimed At", Sortable = true)] DateTime ClaimedAt
);

public record GiftReceipt(int Coins, int Balance);
