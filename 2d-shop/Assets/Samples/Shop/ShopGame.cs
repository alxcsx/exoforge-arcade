using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Shop;

public record ShopItem(string Id, string ItemId, string Nome, string Tipo, int Preco);

public record OwnedItem(string ItemId, string Nome, string Tipo, int Quantidade);

public record Inventory(int Ouro, IReadOnlyList<OwnedItem> Itens);

/// <summary>
/// Interface 2D em grid da loja de itens. Não conhece o Exoforge: pede coisas por eventos
/// (<see cref="RefreshRequested"/>, <see cref="BuyRequested"/>, <see cref="RenameRequested"/>) e mostra o
/// que recebe em <see cref="Show"/>. O <c>ShopBridge</c> da cena é quem fala com o servidor.
/// </summary>
public class ShopGame : MonoBehaviour
{
    [Header("UI References (Opcional - Criado automaticamente se vazio)")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private Text txtPlayerName;
    [SerializeField] private InputField inputPlayerName;
    [SerializeField] private Text txtOuro;
    [SerializeField] private Text txtStatus;
    [SerializeField] private Transform itensContainer;
    [SerializeField] private Transform inventarioContainer;

    public event Action RefreshRequested;
    public event Action<string> BuyRequested;
    public event Action<string> RenameRequested;

    private IReadOnlyList<ShopItem> _itensDisponiveis = Array.Empty<ShopItem>();
    private Inventory _inventarioAtual;

    // Popup de confirmação
    private GameObject _popupOverlay;
    private Text _txtPopupMensagem;
    private ShopItem _itemPendente;

    private void Start()
    {
        if (targetCanvas == null)
        {
            CriarInterface2D();
        }

        RefreshRequested?.Invoke();
    }

    public void SetPlayerName(string nome)
    {
        if (txtPlayerName != null) txtPlayerName.text = $"👤 {nome}";
        if (inputPlayerName != null) inputPlayerName.text = "";
    }

    public void SetStatus(string msg, Color cor) => AtualizarStatus(msg, cor);

    public void Show(IReadOnlyList<ShopItem> itens, Inventory inventario)
    {
        _itensDisponiveis = itens;
        _inventarioAtual = inventario;
        AtualizarUI();

        if (itens.Count == 0)
            AtualizarStatus("Loja vazia. Execute 'seed_itens' e 'seed_loja' no Exoforge Studio.", Color.yellow);
        else
            AtualizarStatus($"Loja pronta ({itens.Count} itens disponíveis).", Color.white);
    }

    public void AlterarNome()
    {
        if (inputPlayerName == null || string.IsNullOrWhiteSpace(inputPlayerName.text))
            return;

        RenameRequested?.Invoke(inputPlayerName.text.Trim());
    }

    private void ConfirmarCompra()
    {
        var item = _itemPendente;
        FecharPopupConfirmacao();

        if (item == null) return;

        AtualizarStatus($"Comprando {item.Nome}...", Color.yellow);
        BuyRequested?.Invoke(item.ItemId);
    }

    private void AbrirPopupConfirmacao(ShopItem item)
    {
        _itemPendente = item;
        if (_txtPopupMensagem != null)
        {
            _txtPopupMensagem.text = $"Deseja comprar <b>{item.Nome}</b>\n<color=#AAAAAA>[{item.Tipo}]</color>\npor <color=#FFD933><b>{item.Preco} Ouro</b></color>?";
        }
        if (_popupOverlay != null)
        {
            _popupOverlay.SetActive(true);
        }
    }

    private void FecharPopupConfirmacao()
    {
        _itemPendente = null;
        if (_popupOverlay != null)
        {
            _popupOverlay.SetActive(false);
        }
    }

    private void AtualizarUI()
    {
        if (txtOuro != null && _inventarioAtual != null)
        {
            txtOuro.text = $"🪙 Ouro: {_inventarioAtual.Ouro}";
        }

        if (itensContainer != null)
        {
            foreach (Transform child in itensContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (var item in _itensDisponiveis)
            {
                CriarCardItem(item);
            }

            if (itensContainer is RectTransform rt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
        }

        AtualizarInventarioUI();
    }

    private void AtualizarInventarioUI()
    {
        if (inventarioContainer == null) return;

        foreach (Transform child in inventarioContainer)
        {
            Destroy(child.gameObject);
        }

        if (_inventarioAtual?.Itens == null || _inventarioAtual.Itens.Count == 0)
        {
            CriarLinhaInventario("Nenhum item no inventário.", "");
            return;
        }

        foreach (var item in _inventarioAtual.Itens)
        {
            string qtd = item.Quantidade > 1 ? $" x{item.Quantidade}" : "";
            CriarLinhaInventario($"<b>{item.Nome}</b>{qtd}", item.Tipo);
        }

        if (inventarioContainer is RectTransform rt)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private void CriarLinhaInventario(string titulo, string tipo)
    {
        var row = new GameObject("InvItem", typeof(RectTransform));
        row.transform.SetParent(inventarioContainer, false);

        var rowImg = row.AddComponent<Image>();
        rowImg.color = new Color(0.18f, 0.18f, 0.23f, 0.98f);

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = row.AddComponent<LayoutElement>();
        fitter.minHeight = 42;
        fitter.preferredHeight = 42;

        var txtGO = new GameObject("Label", typeof(RectTransform));
        txtGO.transform.SetParent(row.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.text = string.IsNullOrEmpty(tipo) ? titulo : $"{titulo}  <color=#888888>[{tipo}]</color>";

        var el = txtGO.AddComponent<LayoutElement>();
        el.flexibleWidth = 1;
        el.preferredHeight = 26;
    }

    private void AtualizarStatus(string msg, Color cor)
    {
        if (txtStatus != null)
        {
            txtStatus.text = msg;
            txtStatus.color = cor;
        }
        Debug.Log($"[Shop] {msg}");
    }

    private void CriarCardItem(ShopItem item)
    {
        var card = new GameObject($"Card_{item.Id}");
        card.transform.SetParent(itensContainer, false);

        var cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.18f, 0.18f, 0.23f, 0.98f);

        // Nome na diagonal na área superior do card
        var nomeGO = new GameObject("NomeDiagonal");
        nomeGO.transform.SetParent(card.transform, false);
        var txtNome = nomeGO.AddComponent<Text>();
        txtNome.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtNome.fontSize = 16;
        txtNome.fontStyle = FontStyle.Bold;
        txtNome.color = Color.white;
        txtNome.alignment = TextAnchor.MiddleCenter;
        txtNome.text = item.Nome;

        var nomeRect = nomeGO.GetComponent<RectTransform>();
        nomeRect.anchorMin = new Vector2(0.05f, 0.42f);
        nomeRect.anchorMax = new Vector2(0.95f, 0.92f);
        nomeRect.sizeDelta = Vector2.zero;
        nomeRect.localEulerAngles = new Vector3(0f, 0f, 16f);

        // Valor (Preço) acima do botão
        var precoGO = new GameObject("Preco");
        precoGO.transform.SetParent(card.transform, false);
        var txtPreco = precoGO.AddComponent<Text>();
        txtPreco.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtPreco.fontSize = 15;
        txtPreco.fontStyle = FontStyle.Bold;
        txtPreco.color = new Color(1f, 0.85f, 0.2f);
        txtPreco.alignment = TextAnchor.MiddleCenter;
        txtPreco.text = $"{item.Preco} Ouro";

        var precoRect = precoGO.GetComponent<RectTransform>();
        precoRect.anchorMin = new Vector2(0.05f, 0.28f);
        precoRect.anchorMax = new Vector2(0.95f, 0.44f);
        precoRect.sizeDelta = Vector2.zero;

        // Botão Comprar na base do card
        var btnGO = new GameObject("BtnComprar");
        btnGO.transform.SetParent(card.transform, false);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.22f, 0.55f, 0.9f);

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        ShopItem capturedItem = item;
        btn.onClick.AddListener(() => AbrirPopupConfirmacao(capturedItem));

        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.12f, 0.06f);
        btnRect.anchorMax = new Vector2(0.88f, 0.26f);
        btnRect.sizeDelta = Vector2.zero;

        var btnTxtGO = new GameObject("Text");
        btnTxtGO.transform.SetParent(btnGO.transform, false);
        var btnTxt = btnTxtGO.AddComponent<Text>();
        btnTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnTxt.fontSize = 14;
        btnTxt.fontStyle = FontStyle.Bold;
        btnTxt.color = Color.white;
        btnTxt.alignment = TextAnchor.MiddleCenter;
        btnTxt.text = "Comprar";

        var btnTxtRect = btnTxtGO.GetComponent<RectTransform>();
        btnTxtRect.anchorMin = Vector2.zero;
        btnTxtRect.anchorMax = Vector2.one;
        btnTxtRect.sizeDelta = Vector2.zero;
    }

    private void CriarInterface2D()
    {
        var canvasGO = new GameObject("Loja2D_Canvas");
        targetCanvas = canvasGO.AddComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventGO = new GameObject("EventSystem");
            eventGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        // Janela Principal da Loja
        var panelGO = new GameObject("LojaJanela");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.15f, 0.96f);

        var panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(480, 490);
        panelRect.anchoredPosition = new Vector2(-145, -24);

        // Topo: Título e Ouro
        var headerGO = new GameObject("Header");
        headerGO.transform.SetParent(panelGO.transform, false);
        var headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.sizeDelta = new Vector2(-30, 60);
        headerRect.anchoredPosition = new Vector2(0, -15);

        // Título
        var titleGO = new GameObject("Titulo");
        titleGO.transform.SetParent(headerGO.transform, false);
        var txtTitulo = titleGO.AddComponent<Text>();
        txtTitulo.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtTitulo.fontSize = 20;
        txtTitulo.fontStyle = FontStyle.Bold;
        txtTitulo.color = Color.white;
        txtTitulo.alignment = TextAnchor.MiddleLeft;
        txtTitulo.text = "🛒 Loja de Itens";
        var titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0);
        titleRect.anchorMax = new Vector2(0.5f, 1);
        titleRect.sizeDelta = Vector2.zero;

        // Saldo de Ouro
        var ouroGO = new GameObject("TxtOuro");
        ouroGO.transform.SetParent(headerGO.transform, false);
        txtOuro = ouroGO.AddComponent<Text>();
        txtOuro.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtOuro.fontSize = 18;
        txtOuro.fontStyle = FontStyle.Bold;
        txtOuro.color = new Color(1f, 0.85f, 0.2f);
        txtOuro.alignment = TextAnchor.MiddleRight;
        txtOuro.text = "🪙 Ouro: ---";
        var ouroRect = ouroGO.GetComponent<RectTransform>();
        ouroRect.anchorMin = new Vector2(0.5f, 0);
        ouroRect.anchorMax = new Vector2(1, 1);
        ouroRect.sizeDelta = Vector2.zero;

        // Grid de Itens (2 colunas)
        var gridGO = new GameObject("ItensGrid", typeof(RectTransform));
        gridGO.transform.SetParent(panelGO.transform, false);
        var gridRect = gridGO.GetComponent<RectTransform>();
        itensContainer = gridRect;
        gridRect.anchorMin = new Vector2(0, 0);
        gridRect.anchorMax = new Vector2(1, 1);
        gridRect.offsetMin = new Vector2(15, 60);
        gridRect.offsetMax = new Vector2(-15, -80);

        var gridLayout = gridGO.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(215, 165);
        gridLayout.spacing = new Vector2(15, 15);
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 2;
        gridLayout.childAlignment = TextAnchor.UpperCenter;

        // Barra de Status no rodapé
        var statusGO = new GameObject("TxtStatus");
        statusGO.transform.SetParent(panelGO.transform, false);
        txtStatus = statusGO.AddComponent<Text>();
        txtStatus.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtStatus.fontSize = 13;
        txtStatus.color = Color.gray;
        txtStatus.alignment = TextAnchor.MiddleCenter;
        txtStatus.text = "Iniciando loja...";
        var statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0, 0);
        statusRect.anchorMax = new Vector2(1, 0);
        statusRect.pivot = new Vector2(0.5f, 0);
        statusRect.sizeDelta = new Vector2(-30, 40);
        statusRect.anchoredPosition = new Vector2(0, 10);

        CriarJanelaInventario(canvasGO.transform);
        CriarBarraUsuario(canvasGO.transform);
        CriarPopupConfirmacao(canvasGO.transform);
    }

    private void CriarBarraUsuario(Transform canvasTransform)
    {
        var barGO = new GameObject("PlayerBar", typeof(RectTransform));
        barGO.transform.SetParent(canvasTransform, false);
        var barImg = barGO.AddComponent<Image>();
        barImg.color = new Color(0.12f, 0.12f, 0.15f, 0.96f);

        var barRect = barGO.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0.5f, 0.5f);
        barRect.anchorMax = new Vector2(0.5f, 0.5f);
        barRect.sizeDelta = new Vector2(765, 44);
        barRect.anchoredPosition = new Vector2(-2.5f, 255);

        // Nome do usuário
        var playerGO = new GameObject("TxtPlayerName", typeof(RectTransform));
        playerGO.transform.SetParent(barGO.transform, false);
        txtPlayerName = playerGO.AddComponent<Text>();
        txtPlayerName.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtPlayerName.fontSize = 15;
        txtPlayerName.fontStyle = FontStyle.Bold;
        txtPlayerName.color = Color.white;
        txtPlayerName.alignment = TextAnchor.MiddleLeft;
        txtPlayerName.text = "👤 Conectando...";
        var playerRect = playerGO.GetComponent<RectTransform>();
        playerRect.anchorMin = new Vector2(0, 0);
        playerRect.anchorMax = new Vector2(0.5f, 1);
        playerRect.offsetMin = new Vector2(15, 4);
        playerRect.offsetMax = new Vector2(-10, -4);

        // Input para novo nome
        var inputGO = new GameObject("InputPlayerName", typeof(RectTransform));
        inputGO.transform.SetParent(barGO.transform, false);
        var inputImg = inputGO.AddComponent<Image>();
        inputImg.color = new Color(0.20f, 0.20f, 0.25f, 1f);

        inputPlayerName = inputGO.AddComponent<InputField>();
        var inputRect = inputGO.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.52f, 0.14f);
        inputRect.anchorMax = new Vector2(0.82f, 0.86f);
        inputRect.sizeDelta = Vector2.zero;

        var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGO.transform.SetParent(inputGO.transform, false);
        var placeholder = placeholderGO.AddComponent<Text>();
        placeholder.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        placeholder.fontSize = 13;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.color = new Color(0.6f, 0.6f, 0.65f);
        placeholder.alignment = TextAnchor.MiddleLeft;
        placeholder.text = "Novo nome...";
        var phRect = placeholderGO.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(8, 2);
        phRect.offsetMax = new Vector2(-8, -2);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(inputGO.transform, false);
        var inputText = textGO.AddComponent<Text>();
        inputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        inputText.fontSize = 14;
        inputText.color = Color.white;
        inputText.alignment = TextAnchor.MiddleLeft;
        inputText.supportRichText = false;
        var txtRect = textGO.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = new Vector2(8, 2);
        txtRect.offsetMax = new Vector2(-8, -2);

        inputPlayerName.placeholder = placeholder;
        inputPlayerName.textComponent = inputText;

        // Botão Alterar Nome
        CriarBotaoPopup(barGO.transform, "Alterar Nome", new Color(0.22f, 0.55f, 0.9f),
            new Vector2(0.84f, 0.14f), new Vector2(0.98f, 0.86f), AlterarNome);
    }

    private void CriarJanelaInventario(Transform canvasTransform)
    {
        var invPanelGO = new GameObject("InventoryWindow", typeof(RectTransform));
        invPanelGO.transform.SetParent(canvasTransform, false);
        var invPanelImg = invPanelGO.AddComponent<Image>();
        invPanelImg.color = new Color(0.12f, 0.12f, 0.15f, 0.96f);

        var invPanelRect = invPanelGO.GetComponent<RectTransform>();
        invPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        invPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        invPanelRect.sizeDelta = new Vector2(270, 490);
        invPanelRect.anchoredPosition = new Vector2(245, -24);

        // Título da janela de Inventário
        var tituloGO = new GameObject("InventoryTitulo", typeof(RectTransform));
        tituloGO.transform.SetParent(invPanelGO.transform, false);
        var txtTitulo = tituloGO.AddComponent<Text>();
        txtTitulo.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtTitulo.fontSize = 19;
        txtTitulo.fontStyle = FontStyle.Bold;
        txtTitulo.color = Color.white;
        txtTitulo.alignment = TextAnchor.MiddleLeft;
        txtTitulo.text = "🎒 Inventário";

        var tituloRect = tituloGO.GetComponent<RectTransform>();
        tituloRect.anchorMin = new Vector2(0, 1);
        tituloRect.anchorMax = new Vector2(1, 1);
        tituloRect.pivot = new Vector2(0.5f, 1);
        tituloRect.sizeDelta = new Vector2(-30, 50);
        tituloRect.anchoredPosition = new Vector2(0, -15);

        // Lista de itens do inventário
        var listGO = new GameObject("InventoryContainer", typeof(RectTransform));
        listGO.transform.SetParent(invPanelGO.transform, false);
        var listRect = listGO.GetComponent<RectTransform>();
        inventarioContainer = listRect;
        listRect.anchorMin = new Vector2(0, 0);
        listRect.anchorMax = new Vector2(1, 1);
        listRect.offsetMin = new Vector2(15, 20);
        listRect.offsetMax = new Vector2(-15, -75);

        var listLayout = listGO.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 8;
        listLayout.childAlignment = TextAnchor.UpperCenter;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
    }

    private void CriarPopupConfirmacao(Transform canvasTransform)
    {
        _popupOverlay = new GameObject("PopupConfirmacao_Overlay");
        _popupOverlay.transform.SetParent(canvasTransform, false);
        var overlayImg = _popupOverlay.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.72f);

        var overlayRect = _popupOverlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.sizeDelta = Vector2.zero;

        // Caixa do Popup
        var boxGO = new GameObject("PopupBox");
        boxGO.transform.SetParent(_popupOverlay.transform, false);
        var boxImg = boxGO.AddComponent<Image>();
        boxImg.color = new Color(0.16f, 0.16f, 0.20f, 1f);

        var boxRect = boxGO.GetComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = new Vector2(340, 200);

        // Título do Popup
        var tituloGO = new GameObject("PopupTitulo");
        tituloGO.transform.SetParent(boxGO.transform, false);
        var txtTitulo = tituloGO.AddComponent<Text>();
        txtTitulo.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtTitulo.fontSize = 18;
        txtTitulo.fontStyle = FontStyle.Bold;
        txtTitulo.color = Color.white;
        txtTitulo.alignment = TextAnchor.MiddleCenter;
        txtTitulo.text = "Confirmar Compra";
        var tituloRect = tituloGO.GetComponent<RectTransform>();
        tituloRect.anchorMin = new Vector2(0, 0.74f);
        tituloRect.anchorMax = new Vector2(1, 0.96f);
        tituloRect.sizeDelta = Vector2.zero;

        // Mensagem
        var msgGO = new GameObject("PopupMensagem");
        msgGO.transform.SetParent(boxGO.transform, false);
        _txtPopupMensagem = msgGO.AddComponent<Text>();
        _txtPopupMensagem.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtPopupMensagem.fontSize = 15;
        _txtPopupMensagem.color = new Color(0.9f, 0.9f, 0.9f);
        _txtPopupMensagem.alignment = TextAnchor.MiddleCenter;
        var msgRect = msgGO.GetComponent<RectTransform>();
        msgRect.anchorMin = new Vector2(0.08f, 0.30f);
        msgRect.anchorMax = new Vector2(0.92f, 0.72f);
        msgRect.sizeDelta = Vector2.zero;

        // Botão Cancelar
        CriarBotaoPopup(boxGO.transform, "Cancelar", new Color(0.45f, 0.45f, 0.50f),
            new Vector2(0.08f, 0.08f), new Vector2(0.46f, 0.26f), FecharPopupConfirmacao);

        // Botão Confirmar
        CriarBotaoPopup(boxGO.transform, "Confirmar", new Color(0.20f, 0.70f, 0.35f),
            new Vector2(0.54f, 0.08f), new Vector2(0.92f, 0.26f), ConfirmarCompra);

        _popupOverlay.SetActive(false);
    }

    private static void CriarBotaoPopup(Transform parent, string label, Color cor, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        var btnGO = new GameObject($"Btn_{label}");
        btnGO.transform.SetParent(parent, false);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = cor;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(onClick);

        var rect = btnGO.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = Vector2.zero;

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(btnGO.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.text = label;

        var txtRect = txtGO.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.sizeDelta = Vector2.zero;
    }
}
