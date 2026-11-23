using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("loja_itens", Version = "0.2.0", Resources = [typeof(LojaOferta)])]
public class LojaItensExtension
{
    [ExoAction(Scope = "player")]
    public async Task<IReadOnlyList<LojaItemDetalhado>> ListarItens()
    {
        var ofertas = ExoServices.Database.Query<LojaOferta>(
            "SELECT id, item_id, ativo FROM loja_ofertas WHERE ativo = true"
        );

        var catalogo = (await ExoServices.Items.ListarCatalogoAsync() ?? new List<Item>())
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
    public async Task<InventarioDetalhado> ComprarItem(string itemId)
    {
        var playerId = ExoServices.Context.CallerPlayerId
            ?? throw new ExoActionException("unauthorized", "Jogador não autenticado.");

        var oferta = ExoServices.Database.QuerySingle<LojaOferta>(
            "SELECT id, item_id, ativo FROM loja_ofertas WHERE item_id = $1 AND ativo = true LIMIT 1",
            itemId
        ) ?? throw new ExoActionException("item_nao_encontrado", "Item não encontrado na loja!");

        var item = await ExoServices.Items.ObterItemAsync(new ItemsObterItemRequest { ItemId = oferta.ItemId })
            ?? throw new ExoActionException("item_nao_encontrado", "Item não encontrado no catálogo!");

        return await ExoServices.Inventario.AddItemAsync(new InventarioAddItemRequest
        {
            PlayerId = playerId,
            ItemId = item.Id,
            Cost = item.Preco
        }) ?? throw new ExoActionException("inventario_indisponivel", "Inventário indisponível.");
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

public class LojaItemDetalhado
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
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
