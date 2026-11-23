using System.Collections.Generic;
using Exoforge.Plugin.SDK;

namespace Exoforge.Extensions;

[ExoExtension("items", Version = "0.1.0", Resources = [typeof(Item)])]
public class ItemsExtension
{
    [ExoAction(Scope = "player")]
    public IReadOnlyList<Item> ListarCatalogo()
    {
        return ExoServices.Database.All<Item>();
    }

    [ExoAction(Scope = "player")]
    public Item ObterItem(string itemId)
    {
        return ExoServices.Database.Get<Item>(itemId)
            ?? throw new ExoActionException("item_nao_encontrado", "Item não encontrado!");
    }

    [ExoAction(Scope = "studio")]
    public int SeedItens()
    {
        var existentes = ExoServices.Database.All<Item>();
        if (existentes.Count > 0)
            return existentes.Count;

        var itensIniciais = new List<Item>
        {
            new() { Id = "espada_ferro", Nome = "Espada de Ferro", Tipo = "Arma", Preco = 150 },
            new() { Id = "escudo_madeira", Nome = "Escudo de Madeira", Tipo = "Armadura", Preco = 100 },
            new() { Id = "pocao_vida", Nome = "Poção de Vida", Tipo = "Consumível", Preco = 50 },
            new() { Id = "espada_lendaria", Nome = "Espada Lendária", Tipo = "Arma", Preco = 500 }
        };

        foreach (var item in itensIniciais)
        {
            ExoServices.Database.Put(item.Id, item);
        }

        return itensIniciais.Count;
    }
}

[ExoResource("itens", PrimaryKey = nameof(Id), AllowPlayerRead = true)]
public class Item
{
    [ExoColumn(Label = "ID", PrimaryKey = true)]
    public string Id { get; set; } = "";

    [ExoColumn(Label = "Nome")]
    public string Nome { get; set; } = "";

    [ExoColumn(Label = "Tipo")]
    public string Tipo { get; set; } = "";

    [ExoColumn(Label = "Preco")]
    public int Preco { get; set; }
}
