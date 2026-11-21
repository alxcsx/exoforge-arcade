using System.Collections.Generic;
using System.Linq;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("loja_itens", Version = "0.2.1", Resources = [typeof(LojaOferta)])]
[ExoDependency("database")]
[ExoDependency("items")]
[ExoDependency("inventario")]
public class LojaItensExtension
{
    // The typed table of this extension's own resource; the items and inventory live in other extensions
    // and are reached through their generated stubs.
    private static IDatabaseTable<LojaOferta> Ofertas => ExoServices.Database.Table<LojaOferta>();


    [ExoAction(Scope = "player")]
    public IReadOnlyList<LojaItemDetalhado> ListarItens()
    {
        // One query for the offers and one for every item they point at, loaded by the server.
        var ofertas = Ofertas.Include(o => o.Item).Where(o => o.Ativo).ToList();

        var resultado = new List<LojaItemDetalhado>(ofertas.Count);
        foreach (var oferta in ofertas)
        {
            if (oferta.Item is { } item)
            {
                resultado.Add(new LojaItemDetalhado
                {
                    Id = oferta.Id,
                    ItemId = oferta.ItemId,
                    Nome = item.Nome,
                    Tipo = item.Tipo,
                    Preco = item.Preco
                });
            }
        }

        return resultado;
    }

    [ExoAction(Scope = "player")]
    public InventarioResumo ComprarItem(string itemId)
    {
        var playerId = ExoServices.Context.CallerPlayerId
            ?? throw new ExoActionException("unauthorized", "Jogador não autenticado.");

        var oferta = Ofertas.FindBy(o => o.ItemId == itemId && o.Ativo)
            ?? throw new ExoActionException("item_nao_encontrado", "Item não encontrado na loja!");

        var item = ExoServices.Items.ObterItemAsync(new ItemsObterItemRequest { ItemId = oferta.ItemId }).GetAwaiter().GetResult()
            ?? throw new ExoActionException("item_nao_encontrado", "Item não encontrado no catálogo!");

        var inventario = ExoServices.Inventario
            .AddItemAsync(new InventarioAddItemRequest { PlayerId = playerId, ItemId = item.Id, Cost = item.Preco })
            .GetAwaiter().GetResult()
            ?? throw new ExoActionException("inventario_indisponivel", "Inventário indisponível.");

        return new InventarioResumo
        {
            Id = inventario.Id,
            Ouro = (int)inventario.Ouro,
            Itens = inventario.Itens.Select(i => new InventarioItemResumo
            {
                Id = i.Id,
                ItemId = i.ItemId,
                Quantidade = i.Quantidade,
                Nome = i.Nome,
                Tipo = i.Tipo,
                Preco = i.Preco
            }).ToList()
        };
    }

    [ExoAction(Scope = "studio")]
    public int SeedLoja()
    {
        var existentes = Ofertas.All();
        if (existentes.Count > 0)
            return existentes.Count;

        var ofertas = new List<LojaOferta>
        {
            new() { Id = "oferta_espada_ferro", ItemId = "espada_ferro", Ativo = true },
            new() { Id = "oferta_escudo_madeira", ItemId = "escudo_madeira", Ativo = true },
            new() { Id = "oferta_pocao_vida", ItemId = "pocao_vida", Ativo = true },
            new() { Id = "oferta_espada_lendaria", ItemId = "espada_lendaria", Ativo = true }
        };

        foreach (var oferta in ofertas)
        {
            Ofertas.Put(oferta);
        }

        return ofertas.Count;
    }
}

public class LojaItemDetalhado
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int Preco { get; set; }
}

public class InventarioResumo
{
    public string Id { get; set; } = "";
    public int Ouro { get; set; }
    public List<InventarioItemResumo> Itens { get; set; } = new();
}

public class InventarioItemResumo
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int Quantidade { get; set; }
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int Preco { get; set; }
}

[ExoResource("loja_ofertas", PrimaryKey = nameof(Id), AllowPlayerRead = true)]
public class LojaOferta
{
    [ExoColumn(Label = "ID", PrimaryKey = true)]
    public string Id { get; set; } = "";

    [ExoColumn(Label = "Item ID", ForeignKey = true, References = "itens.id")]
    public string ItemId { get; set; } = "";

    // Filled by Include; not a column.
    [ExoNavigation(nameof(ItemId))]
    public Item? Item { get; set; }

    [ExoColumn(Label = "Ativo")]
    public bool Ativo { get; set; } = true;
}
