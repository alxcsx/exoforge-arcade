using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("inventario", Version = "0.2.0", Resources = [typeof(Inventario), typeof(InventarioItem)])]
public class InventarioExtension
{
    [ExoAction(Scope = "player")]
    public async Task<InventarioDetalhado> ObterInventario()
    {
        var playerId = ExoServices.Context.CallerPlayerId
            ?? throw new ExoActionException("unauthorized", "Jogador não autenticado.");

        return await MontarInventarioDetalhado(playerId);
    }

    [ExoAction(Scope = "server")]
    public async Task<InventarioDetalhado> AddItem(string playerId, string itemId, int cost)
    {
        var carteira = ObterOuCriarCarteira(playerId);

        if (carteira.Ouro < cost)
            throw new ExoActionException("sem_ouro", "Sem ouro!");

        carteira.Ouro -= cost;
        ExoServices.Database.Put(carteira.Id, carteira);

        string entryId = $"{playerId}:{itemId}";
        var existente = ExoServices.Database.Get<InventarioItem>(entryId);
        if (existente == null)
        {
            existente = new InventarioItem
            {
                Id = entryId,
                PlayerId = playerId,
                ItemId = itemId,
                Quantidade = 1
            };
        }
        else
        {
            existente.Quantidade += 1;
        }

        ExoServices.Database.Put(existente.Id, existente);

        return await MontarInventarioDetalhado(playerId);
    }

    [ExoAction(Scope = "server")]
    public async Task<InventarioDetalhado> AddGold(string playerId, int amount)
    {
        var carteira = ObterOuCriarCarteira(playerId);
        carteira.Ouro += amount;
        ExoServices.Database.Put(carteira.Id, carteira);

        return await MontarInventarioDetalhado(playerId);
    }

    private async Task<InventarioDetalhado> MontarInventarioDetalhado(string playerId)
    {
        var carteira = ObterOuCriarCarteira(playerId);
        var linhas = ExoServices.Database.Query<InventarioItem>(
            "SELECT id, player_id, item_id, quantidade FROM inventario_itens WHERE player_id = $1",
            playerId
        );

        var catalogo = (await ExoServices.Items.ListarCatalogoAsync() ?? new List<Item>())
            .ToDictionary(i => i.Id, i => i);

        var detalhados = new List<InventarioItemDetalhado>(linhas.Count);
        foreach (var linha in linhas)
        {
            catalogo.TryGetValue(linha.ItemId, out var info);
            detalhados.Add(new InventarioItemDetalhado
            {
                Id = linha.Id,
                ItemId = linha.ItemId,
                Quantidade = linha.Quantidade,
                Nome = info?.Nome ?? linha.ItemId,
                Tipo = info?.Tipo ?? "",
                Preco = info?.Preco ?? 0
            });
        }

        return new InventarioDetalhado
        {
            Id = carteira.Id,
            Ouro = carteira.Ouro,
            Itens = detalhados
        };
    }

    private static Inventario ObterOuCriarCarteira(string playerId)
    {
        var inv = ExoServices.Database.Get<Inventario>(playerId);
        if (inv == null)
        {
            inv = new Inventario { Id = playerId, Ouro = 1000 };
            ExoServices.Database.Put(inv.Id, inv);
        }
        return inv;
    }
}

public class InventarioDetalhado
{
    public string Id { get; set; } = "";
    public int Ouro { get; set; }
    public List<InventarioItemDetalhado> Itens { get; set; } = new();
}

public class InventarioItemDetalhado
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int Quantidade { get; set; }
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int Preco { get; set; }
}

[ExoResource("inventario", PrimaryKey = nameof(Id))]
public class Inventario
{
    [ExoColumn(Label = "ID", PrimaryKey = true)]
    public string Id { get; set; } = "";

    [ExoColumn(Label = "Ouro")]
    public int Ouro { get; set; }
}

[ExoResource("inventario_itens", PrimaryKey = nameof(Id))]
public class InventarioItem
{
    [ExoColumn(Label = "ID", PrimaryKey = true)]
    public string Id { get; set; } = "";

    [ExoColumn(Label = "Player ID", Filterable = true, Role = "user_id")]
    public string PlayerId { get; set; } = "";

    [ExoColumn(Label = "Item ID", ForeignKey = true, References = "itens.id")]
    public string ItemId { get; set; } = "";

    [ExoColumn(Label = "Quantidade")]
    public int Quantidade { get; set; }
}
