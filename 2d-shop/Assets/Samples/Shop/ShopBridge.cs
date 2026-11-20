using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Arcade.Common;
using Exoforge.Client;
using Exoforge.Client.Unity;
using UnityEngine;

namespace Arcade.Shop;

/// <summary>
/// The only component of the scene that calls <c>ExoServices.*</c>. Delete it and the shop UI still
/// opens, it just has nothing to show.
/// </summary>
[RequireComponent(typeof(ShopGame))]
public class ShopBridge : MonoBehaviour
{
    private static readonly Color Ok = new(0.2f, 0.9f, 0.3f);

    private ShopGame _game = null!;

    private void Awake() => _game = GetComponent<ShopGame>();

    private void OnEnable()
    {
        _game.RefreshRequested += OnRefresh;
        _game.BuyRequested += OnBuy;
        _game.RenameRequested += OnRename;
    }

    private void OnDisable()
    {
        _game.RefreshRequested -= OnRefresh;
        _game.BuyRequested -= OnBuy;
        _game.RenameRequested -= OnRename;
    }

    private void OnRefresh()
    {
        _game.SetStatus("Conectando ao Exoforge...", Color.yellow);
        SamplePlayer.WhenReady(session =>
        {
            _game.SetPlayerName(string.IsNullOrEmpty(session.DisplayName) ? session.PlayerId : session.DisplayName);
            _ = Run("carregar loja", LoadAsync);
        });
    }

    private async void OnBuy(string itemId)
    {
        await Run("comprar", async () =>
        {
            await ExoServices.LojaItens.ComprarItemAsync(itemId);
            await LoadAsync();
            _game.SetStatus("Compra concluída.", Ok);
        });
    }

    private async void OnRename(string name)
    {
        await Run("alterar nome", async () =>
        {
            var session = await ExoforgeSDK.Auth.SetDisplayName(name);
            _game.SetPlayerName(session.DisplayName);
            _game.SetStatus($"Nome alterado para '{session.DisplayName}'.", Ok);
        });
    }

    private async Task LoadAsync()
    {
        var offers = await ExoServices.LojaItens.ListarItensAsync();
        var inventory = await ExoServices.Inventario.ObterInventarioAsync();

        var items = offers.Select(o => new ShopItem(o.Id, o.ItemId, o.Nome, o.Tipo, (int)o.Preco)).ToList();
        _game.Show(items, new Inventory((int)inventory.Ouro, ParseOwned(inventory.Itens)));
    }

    // The generator types nested lists as raw JSON, so the owned items are read from it by hand.
    private static IReadOnlyList<OwnedItem> ParseOwned(JsonElement itens)
    {
        if (itens.ValueKind != JsonValueKind.Array) return Array.Empty<OwnedItem>();

        return itens.EnumerateArray()
            .Select(i => new OwnedItem(
                i.GetProperty("item_id").GetString() ?? "",
                i.GetProperty("nome").GetString() ?? "",
                i.GetProperty("tipo").GetString() ?? "",
                i.GetProperty("quantidade").GetInt32()))
            .ToList();
    }

    private async Task Run(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ExoActionException ex)
        {
            _game.SetStatus($"{what} recusado [{ex.Code}]: {ex.Message}", Color.red);
            Debug.LogWarning($"[Arcade] shop {what}: {ex.Code} {ex.Message}");
        }
        catch (Exception ex)
        {
            _game.SetStatus($"{what} falhou: {ex.Message}", Color.red);
            Debug.LogError($"[Arcade] shop {what}: {ex}");
        }
    }
}
