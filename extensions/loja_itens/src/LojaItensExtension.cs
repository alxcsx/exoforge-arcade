using System.Collections.Generic;
using System.Linq;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

[ExoExtension("loja_itens", Version = "0.2.0", Resources = [typeof(LojaOferta)])]
[ExoDependency("database")]
[ExoDependency("items")]
[ExoDependency("inventario")]
public class LojaItensExtension
{
    [ExoAction(Scope = "player")]
    public IReadOnlyList<LojaItemDetalhado> ListarItens()
    {
        var ofertas = ExoServices.Database.Query<LojaOferta>(
            "SELECT id, item_id, ativo FROM loja_ofertas WHERE ativo = true"
        );

        var catalogo = ExoServices.Actions.CallAction<List<ItemCatalogoLoja>>("items", "listar_catalogo", null)
            .ToDictionary(i => i.Id, i => i);

        var resultado = new List<LojaItemDetalhado>(ofertas.Count);
        foreach (var oferta in ofertas)
        {
            if (catalogo.TryGetValue(oferta.ItemId, out var info))
            {
                resultado.Add(new LojaItemDetalhado
                {
                    Id = oferta.Id,
                    ItemId = oferta.ItemId,
                    Nome = info.Nome,
                    Tipo = info.Tipo,
                    Preco = info.Preco
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

        var oferta = ExoServices.Database.QuerySingle<LojaOferta>(
            "SELECT id, item_id, ativo FROM loja_ofertas WHERE item_id = $1 AND ativo = true LIMIT 1",
            itemId
        ) ?? throw new ExoActionException("item_nao_encontrado", "Item não encontrado na loja!");

        var item = ExoServices.Actions.CallAction<ItemCatalogoLoja>(
            "items",
            "obter_item",
            new { item_id = oferta.ItemId }
        );

        return ExoServices.Actions.CallAction<InventarioResumo>(
            "inventario",
            "add_item",
            new { player_id = playerId, item_id = item.Id, cost = item.Preco }
        );
    }

    [ExoAction(Scope = "studio")]
    public int SeedLoja()
    {
        var existentes = ExoServices.Database.All<LojaOferta>();
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
            ExoServices.Database.Put(oferta.Id, oferta);
        }

        return ofertas.Count;
    }
}

public class ItemCatalogoLoja
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int Preco { get; set; }
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

    [ExoColumn(Label = "Ativo")]
    public bool Ativo { get; set; } = true;
}
