using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Xunit;

public class WalletTests
{
    private static (ExoTestContext Host, WalletExtension Wallet) Start(string? caller = "p1")
    {
        var host = ExoTestHost.Start("wallet");
        host.CallerPlayerId = caller;
        return (host, host.Create<WalletExtension>());
    }

    [Fact]
    public void Credit_adds_coins()
    {
        var (host, wallet) = Start();
        using (host)
        {
            wallet.Credit("p1", 40, "test");
            var balance = wallet.Credit("p1", 10, "test");

            Assert.Equal(50, balance.Coins);
            Assert.Equal(50, host.Database.Get<WalletBalance>("wallet_balances", "p1")!.Coins);
        }
    }

    [Fact]
    public void Player_reads_only_their_own_balance()
    {
        var (host, wallet) = Start("p1");
        using (host)
        {
            wallet.Credit("p1", 5, "test");
            wallet.Credit("p2", 99, "test");

            Assert.Equal(5, wallet.GetBalance().Coins);
        }
    }

    [Fact]
    public void Debit_without_funds_fails_and_leaves_the_balance()
    {
        var (host, wallet) = Start();
        using (host)
        {
            wallet.Credit("p1", 30, "test");

            var error = Assert.Throws<ExoActionException>(() => wallet.Debit("p1", 31, "test"));

            Assert.Equal("insufficient_funds", error.Code);
            Assert.Equal(30, wallet.GetBalance().Coins);
        }
    }

    [Fact]
    public void Debit_never_goes_negative()
    {
        var (host, wallet) = Start();
        using (host)
        {
            wallet.Credit("p1", 10, "test");
            wallet.Debit("p1", 10, "test");

            Assert.Throws<ExoActionException>(() => wallet.Debit("p1", 1, "test"));
            Assert.Equal(0, wallet.GetBalance().Coins);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_amounts_are_rejected(int amount)
    {
        var (host, wallet) = Start();
        using (host)
        {
            Assert.Equal("invalid_amount", Assert.Throws<ExoActionException>(() => wallet.Credit("p1", amount, "t")).Code);
            Assert.Equal("invalid_amount", Assert.Throws<ExoActionException>(() => wallet.Debit("p1", amount, "t")).Code);
        }
    }

    [Fact]
    public void Credit_that_would_overflow_is_rejected()
    {
        var (host, wallet) = Start();
        using (host)
        {
            wallet.Credit("p1", int.MaxValue, "test");

            Assert.Equal("balance_overflow", Assert.Throws<ExoActionException>(() => wallet.Credit("p1", 1, "test")).Code);
            Assert.Equal(int.MaxValue, wallet.GetBalance().Coins);
        }
    }

    [Fact]
    public void Balance_needs_a_signed_in_player()
    {
        var (host, wallet) = Start(caller: null);
        using (host)
            Assert.Equal("unauthorized", Assert.Throws<ExoActionException>(() => wallet.GetBalance()).Code);
    }
}
