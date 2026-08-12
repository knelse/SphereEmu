using Godot;
using SphServer.Server.UI.ConnectedClients;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Full-window admin overlay: PlayerList | PlayerMenu | AdminActions.
///     Scales down uniformly when window width is below the project default (1920).
/// </summary>
public partial class AdminUiRoot : Control
{
    private const float DesignWidth = 1920f;

    private Control? scaleRoot;
    private float appliedScale = 1f;
    private ConnectedClientsUI? playerList;
    private CharacterStatsPanel? statsPanel;
    private PersonaPanel? personaPanel;
    private Locale locale = Locale.Russian;
    private readonly Dictionary<Locale, Button> localeButtons = new();
    private ushort? selectedClientId;
    private Button? changeGenderButton;
    private Button? kickButton;
    private Button? banButton;
    private Button? teleportButton;
    private TeleportDestinationWindow? teleportWindow;
    private ItemDetailsPopupHost? itemDetailsHost;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Logical layout lives here at design size; Scale shrinks it when the window is narrower than DesignWidth.
        scaleRoot = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(scaleRoot);

        var layout = new HBoxContainer();
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 8);
        layout.MouseFilter = MouseFilterEnum.Ignore;
        scaleRoot.AddChild(layout);

        Resized += ApplyUiScale;
        CallDeferred(nameof(ApplyUiScale));

        // Left: player list
        playerList = new ConnectedClientsUI
        {
            Name = "ConnectedClients",
            CustomMinimumSize = new Vector2(280, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Columns = 3,
            ColumnTitlesVisible = true,
            HideFolding = true,
            HideRoot = true,
            ScrollHorizontalEnabled = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        var popup = new ConnectedClientsPopupUI { Name = "ConnectedClientPopup" };
        playerList.AddChild(popup);
        playerList.ClientSelected += OnClientSelected;
        layout.AddChild(playerList);

        // Center: PlayerMenu
        var playerMenu = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        playerMenu.AddThemeConstantOverride("separation", 6);
        layout.AddChild(playerMenu);

        var localeBar = new HBoxContainer();
        localeBar.AddThemeConstantOverride("separation", 4);
        foreach (var (loc, label) in new (Locale, string)[]
                 {
                     (Locale.Russian, "RU"),
                     (Locale.English, "EN"),
                     (Locale.German, "DE"),
                     (Locale.French, "FR"),
                     (Locale.Italian, "IT"),
                     (Locale.Portuguese, "PT"),
                     (Locale.Spanish, "ES")
                 })
        {
            var button = new Button { Text = label, ToggleMode = true, ButtonPressed = loc == locale };
            var captured = loc;
            button.Pressed += () => SetLocale(captured);
            localeButtons[loc] = button;
            localeBar.AddChild(button);
        }

        localeBar.AddChild(new Control { CustomMinimumSize = new Vector2(50, 0) });

        changeGenderButton = new Button { Text = "Change Gender", Disabled = true };
        changeGenderButton.Pressed += ToggleGender;
        localeBar.AddChild(changeGenderButton);

        playerMenu.AddChild(localeBar);

        var panels = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        panels.AddThemeConstantOverride("separation", 8);
        statsPanel = new CharacterStatsPanel();
        personaPanel = new PersonaPanel();
        panels.AddChild(statsPanel);
        panels.AddChild(personaPanel);
        playerMenu.AddChild(panels);

        // Popup layer above UI content (same scale), ignores empty space so clicks pass through.
        itemDetailsHost = new ItemDetailsPopupHost { Name = "ItemDetailsPopupHost" };
        scaleRoot.AddChild(itemDetailsHost);
        itemDetailsHost.SetLocale(locale);
        personaPanel.SetPopupHost(itemDetailsHost);

        // Right of PlayerMenu: admin actions
        var adminActions = new VBoxContainer
        {
            Name = "AdminActions",
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop
        };
        adminActions.AddThemeConstantOverride("separation", 6);
        adminActions.AddChild(new Label
        {
            Text = "Admin actions",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        kickButton = new Button { Text = "Kick", Disabled = true };
        kickButton.Pressed += OnKickPressed;
        adminActions.AddChild(kickButton);

        banButton = new Button { Text = "Ban", Disabled = true };
        banButton.Pressed += OnBanPressed;
        adminActions.AddChild(banButton);

        teleportButton = new Button { Text = "Teleport", Disabled = true };
        teleportButton.Pressed += OnTeleportPressed;
        adminActions.AddChild(teleportButton);

        layout.AddChild(adminActions);

        teleportWindow = new TeleportDestinationWindow { Name = "TeleportDestinationWindow" };
        AddChild(teleportWindow);

        statsPanel.SetLocale(locale);
        statsPanel.SetSelectedClient(null);
        personaPanel.SetSelectedClient(null);
        UpdateActionButtons();

        ClientStateEvents.CharacterChanged += OnClientStateCharacterChanged;
        ClientStateEvents.RosterChanged += OnClientStateRosterChanged;
    }

    public override void _ExitTree()
    {
        ClientStateEvents.CharacterChanged -= OnClientStateCharacterChanged;
        ClientStateEvents.RosterChanged -= OnClientStateRosterChanged;
    }

    private void OnClientStateCharacterChanged(ushort clientId)
    {
        if (selectedClientId == clientId)
        {
            CallDeferred(nameof(UpdateActionButtons));
        }
    }

    private void OnClientStateRosterChanged()
    {
        if (selectedClientId is not null && ActiveClients.Get(selectedClientId.Value) is null)
        {
            selectedClientId = null;
        }

        CallDeferred(nameof(UpdateActionButtons));
        if (changeGenderButton is not null)
        {
            changeGenderButton.Disabled = selectedClientId is null
                                          || ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter is null;
        }
    }

    private void ApplyUiScale()
    {
        if (scaleRoot is null)
        {
            return;
        }

        var width = Size.X;
        if (width <= 1f)
        {
            width = GetViewportRect().Size.X;
        }

        var s = Mathf.Clamp(width / DesignWidth, 0.01f, 1f);
        var logicalSize = Size / s;
        if (Mathf.IsEqualApprox(s, appliedScale)
            && Mathf.IsEqualApprox(scaleRoot.Size.X, logicalSize.X)
            && Mathf.IsEqualApprox(scaleRoot.Size.Y, logicalSize.Y))
        {
            return;
        }

        appliedScale = s;
        scaleRoot.Scale = new Vector2(s, s);
        scaleRoot.Position = Vector2.Zero;
        scaleRoot.Size = logicalSize;
    }

    private void SetLocale(Locale newLocale)
    {
        locale = newLocale;
        foreach (var (loc, button) in localeButtons)
        {
            button.SetPressedNoSignal(loc == locale);
        }

        statsPanel?.SetLocale(locale);
        itemDetailsHost?.SetLocale(locale);
    }

    private void OnClientSelected(ushort clientId)
    {
        // 0 is reserved as "cleared" (real client ids start at 0x4F6F)
        selectedClientId = clientId == 0 ? null : clientId;
        if (changeGenderButton is not null)
        {
            changeGenderButton.Disabled = selectedClientId is null
                                          || ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter is null;
        }

        UpdateActionButtons();
        statsPanel?.SetSelectedClient(selectedClientId);
        statsPanel?.SetLocale(locale);
        personaPanel?.SetSelectedClient(selectedClientId);
    }

    private void UpdateActionButtons()
    {
        var hasClient = selectedClientId is not null;
        var hasCharacter = hasClient
                           && ActiveClients.Get(selectedClientId!.Value)?.CurrentCharacter is not null;
        if (kickButton is not null)
        {
            kickButton.Disabled = !hasClient;
        }

        if (banButton is not null)
        {
            banButton.Disabled = !hasClient;
        }

        if (teleportButton is not null)
        {
            teleportButton.Disabled = !hasCharacter;
        }
    }

    private void OnKickPressed()
    {
        if (selectedClientId is null)
        {
            return;
        }

        AdminClientActions.Kick(selectedClientId.Value);
    }

    private void OnBanPressed()
    {
        if (selectedClientId is null)
        {
            return;
        }

        AdminClientActions.Ban(selectedClientId.Value);
    }

    private void OnTeleportPressed()
    {
        if (selectedClientId is null || teleportWindow is null)
        {
            return;
        }

        if (ActiveClients.Get(selectedClientId.Value)?.CurrentCharacter is null)
        {
            return;
        }

        teleportWindow.OpenForClient(selectedClientId.Value);
    }

    private void ToggleGender()
    {
        if (selectedClientId is null)
        {
            return;
        }

        var client = ActiveClients.Get(selectedClientId.Value);
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return;
        }

        character.IsGenderFemale = !character.IsGenderFemale;
        AdminActionLog.Info(client,
            $"changed gender to {(character.IsGenderFemale ? "female" : "male")} (server/UI only)");

        statsPanel?.SetSelectedClient(selectedClientId);
        personaPanel?.SetSelectedClient(selectedClientId);
    }
}
