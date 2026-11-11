using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles the Main Menu network buttons for hosting and joining a multiplayer game
/// using Unity Relay for NAT-traversal and WebGL compatibility.
///
/// Flow:
/// 1. Host clicks "Host" → creates Relay allocation → gets join code → starts host
///    → shows LobbyHostSide panel with the join code displayed.
/// 2. Client clicks "Join" → shows LobbyClientSide panel with a code input field.
/// 3. Client enters code and clicks "Join Game" → joins Relay allocation → starts client
///    → hides LobbyClientSide → shows LobbyHostSide panel.
/// 4. In the LobbyHostSide panel, host clicks "Start Game" → loads Game scene for everyone.
///
/// SETUP:
/// 1. Link your project in Edit → Project Settings → Services.
/// 2. Enable Relay in the Unity Dashboard (dashboard.unity3d.com).
/// 3. On the NetworkManager's UnityTransport component, enable "Use WebSockets" and "Use Encryption".
/// 4. Assign all Inspector references on this component.
/// </summary>
public class NetworkUI : MonoBehaviour
{
    [Header("Main Menu Buttons")]
    [Tooltip("Button that starts the game as Host (in the main menu).")]
    public Button hostButton;

    [Tooltip("Button that opens the Join panel (in the main menu).")]
    public Button joinButton;

    [Header("Username")]
    [Tooltip("Modal panel that forces the player to enter a username before hosting/joining.")]
    public GameObject playerInputPanel;

    [Tooltip("InputField where the player types their display name before hosting/joining.")]
    public InputField usernameInput;

    [Tooltip("Button inside PlayerInput that validates and confirms the username.")]
    public Button confirmUsernameButton;

    [Tooltip("Text element displaying the confirmed player's username on the Main Menu.")]
    public Text playerNameText;

    public static readonly int MaxUsernameBytes = FixedString32Bytes.UTF8MaxLengthInBytes;

    /// <summary>
    /// The locally entered username, stored statically so LobbyNetwork and NetworkPlayer
    /// can read it after connecting. NOT used for network sync — just a local session value.
    /// </summary>
    public static string LocalUsername = "";

    [Header("Panel References")]
    [Tooltip("The main menu panel (Buttons, Title, etc.) to hide when entering the lobby.")]
    public GameObject mainMenuPanel;

    [Tooltip("The host-side lobby panel showing player list, join code, and Start Game button.")]
    public GameObject lobbyHostPanel;

    [Tooltip("The client-side lobby panel showing the join code input field.")]
    public GameObject lobbyClientPanel;

    [Tooltip("LobbyUI controller managing the player list and Start Game button.")]
    public LobbyUI lobbyUI;

    [Header("Relay UI — Host Side")]
    [Tooltip("Text element inside LobbyHostSide that displays the Relay join code.")]
    public Text joinCodeDisplayText;

    [Header("Relay UI — Client Side")]
    [Tooltip("InputField inside LobbyClientSide where the client enters the join code.")]
    public InputField joinCodeInputField;

    [Tooltip("Button inside LobbyClientSide that triggers the Relay join.")]
    public Button clientJoinButton;

    [Header("Relay Settings")]
    [Tooltip("Maximum number of OTHER players (not counting the host). 3 means 4 total.")]
    public int maxRelayConnections = 3;

    // Tracks whether Unity Services have been initialized this session
    private static bool servicesInitialized = false;

    // The generated Relay join code, stored for reference
    private string currentJoinCode = "";

    private string defaultPlaceholderText = "Enter Username";
    private Color defaultPlaceholderColor = new Color(0.1960784f, 0.1960784f, 0.1960784f, 0.5f);
    private bool hasCachedPlaceholder = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        LocalUsername = string.Empty;
        servicesInitialized = false;
    }

    private void Start()
    {
        // Ensure the NetworkManager persists when we load scenes
        if (NetworkManager.Singleton != null)
        {
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
        }

        ResolveUsernameReferences();

        // Wire up button listeners
        if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
        if (clientJoinButton != null) clientJoinButton.onClick.AddListener(OnClientJoinClicked);
        if (confirmUsernameButton != null) confirmUsernameButton.onClick.AddListener(OnConfirmUsernameClicked);

        if (usernameInput != null)
        {
            if (usernameInput.characterLimit <= 0)
            {
                usernameInput.characterLimit = MaxUsernameBytes;
            }

            if (usernameInput.placeholder is Text placeholderText)
            {
                defaultPlaceholderText = placeholderText.text;
                defaultPlaceholderColor = placeholderText.color;
                hasCachedPlaceholder = true;
            }

            usernameInput.onValueChanged.AddListener(OnUsernameInputChanged);
            usernameInput.onSubmit.AddListener(OnUsernameSubmitted);
        }

        // Ensure lobby panels start hidden
        if (lobbyHostPanel != null) lobbyHostPanel.SetActive(false);
        if (lobbyClientPanel != null) lobbyClientPanel.SetActive(false);

        if (HasConfirmedUsername())
        {
            LocalUsername = SanitizeUsername(LocalUsername);
            ApplyConfirmedUsernameUI();
        }
        else
        {
            ShowUsernamePrompt();
        }
    }

    // ─────────────────────────────────────────────
    //  Unity Services Initialization
    // ─────────────────────────────────────────────

    /// <summary>
    /// Initializes Unity Gaming Services and signs in anonymously.
    /// Safe to call multiple times — only runs once per session.
    /// </summary>
    private async System.Threading.Tasks.Task InitializeServicesAsync()
    {
        if (!servicesInitialized)
        {
            Debug.Log("[NetworkUI] Initializing Unity Services...");
            await UnityServices.InitializeAsync();
            servicesInitialized = true;
            Debug.Log("[NetworkUI] Unity Services initialized successfully.");
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            Debug.Log("[NetworkUI] Signing in anonymously...");
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[NetworkUI] Signed in. Player ID: {AuthenticationService.Instance.PlayerId}");
        }
    }

    // ─────────────────────────────────────────────
    //  HOST Flow
    // ─────────────────────────────────────────────

    /// <summary>
    /// Host button clicked: create Relay allocation, get join code, configure transport, start host.
    /// </summary>
    private async void OnHostClicked()
    {
        if (!EnsureConfirmedUsername())
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] NetworkManager.Singleton is null.");
            return;
        }

        // Prevent double-clicks
        SetButtonsInteractable(false);

        try
        {
            // 1. Initialize Unity Services + Authentication
            await InitializeServicesAsync();

            // 2. Create Relay allocation (maxRelayConnections = slots for OTHER players)
            Debug.Log($"[NetworkUI] Creating Relay allocation for {maxRelayConnections} connections...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxRelayConnections);
            Debug.Log($"[NetworkUI] Relay allocation created. AllocationId: {allocation.AllocationId}");

            // 3. Get the join code
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[NetworkUI] Relay join code: {currentJoinCode}");

            // 4. Configure UnityTransport with Relay data (wss for WebGL compatibility)
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[NetworkUI] UnityTransport component not found on NetworkManager!");
                SetButtonsInteractable(true);
                return;
            }

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "wss");
            transport.SetRelayServerData(relayServerData);
            Debug.Log("[NetworkUI] UnityTransport configured with Relay server data (wss).");

            // 5. Start Host
            bool started = NetworkManager.Singleton.StartHost();

            if (started)
            {
                Debug.Log($"[NetworkUI] Host started as '{LocalUsername}' via Relay. Join code: {currentJoinCode}");
                ShowLobby(true);
            }
            else
            {
                Debug.LogError("[NetworkUI] Failed to start Host after Relay allocation.");
                SetButtonsInteractable(true);
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[NetworkUI] Relay error during host: {e.Message}");
            SetButtonsInteractable(true);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NetworkUI] Unexpected error during host: {e.Message}");
            SetButtonsInteractable(true);
        }
    }

    // ─────────────────────────────────────────────
    //  CLIENT Flow — Step 1: Show code input panel
    // ─────────────────────────────────────────────

    /// <summary>
    /// Main menu Join button: shows the LobbyClientSide panel where the player enters a code.
    /// Does NOT connect yet — that happens when they click "Join Game" inside the panel.
    /// </summary>
    private void OnJoinClicked()
    {
        if (!EnsureConfirmedUsername())
        {
            return;
        }

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        if (lobbyClientPanel != null)
        {
            lobbyClientPanel.SetActive(true);
            Debug.Log("[NetworkUI] Showing client join panel. Waiting for join code input...");
        }
        else
        {
            Debug.LogError("[NetworkUI] lobbyClientPanel is not assigned in Inspector!");
        }
    }

    // ─────────────────────────────────────────────
    //  CLIENT Flow — Step 2: Connect via Relay
    // ─────────────────────────────────────────────

    /// <summary>
    /// "Join Game" button inside LobbyClientSide: reads the join code, connects via Relay.
    /// </summary>
    private async void OnClientJoinClicked()
    {
        if (!EnsureConfirmedUsername())
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] NetworkManager.Singleton is null.");
            return;
        }

        // Read and validate the join code
        string joinCode = (joinCodeInputField != null) ? joinCodeInputField.text.Trim().ToUpper() : "";

        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("[NetworkUI] Join code is empty. Please enter a valid code.");
            return;
        }

        // Prevent double-clicks
        SetButtonsInteractable(false);

        try
        {
            // 1. Initialize Unity Services + Authentication
            await InitializeServicesAsync();

            // 2. Join the Relay allocation using the code
            Debug.Log($"[NetworkUI] Joining Relay with code: {joinCode}...");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            Debug.Log($"[NetworkUI] Joined Relay allocation. AllocationId: {joinAllocation.AllocationId}");

            // 3. Configure UnityTransport with Relay data (wss for WebGL compatibility)
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[NetworkUI] UnityTransport component not found on NetworkManager!");
                SetButtonsInteractable(true);
                return;
            }

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "wss");
            transport.SetRelayServerData(relayServerData);
            Debug.Log("[NetworkUI] UnityTransport configured with Relay server data (wss).");

            // 4. Start Client
            bool started = NetworkManager.Singleton.StartClient();

            if (started)
            {
                Debug.Log($"[NetworkUI] Client started as '{LocalUsername}' via Relay.");
                ShowLobby(false);
            }
            else
            {
                Debug.LogError("[NetworkUI] Failed to start Client after Relay join.");
                SetButtonsInteractable(true);
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[NetworkUI] Relay error during join: {e.Message}");
            SetButtonsInteractable(true);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NetworkUI] Unexpected error during join: {e.Message}");
            SetButtonsInteractable(true);
        }
    }

    // ─────────────────────────────────────────────
    //  Panel Management
    // ─────────────────────────────────────────────

    /// <summary>
    /// Hides menu/client panels and shows the host-side lobby panel.
    /// Both host and connected clients see LobbyHostSide once connected.
    /// </summary>
    private void ShowLobby(bool isHost)
    {
        // Hide everything else
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyClientPanel != null) lobbyClientPanel.SetActive(false);

        // Show the host-side lobby (used by both host and client after connecting)
        if (lobbyHostPanel != null)
        {
            lobbyHostPanel.SetActive(true);
        }

        // Locate LobbyUI (lives on its own Lobby GameObject, not always on lobbyHostPanel)
        LobbyUI targetLobbyUI = lobbyUI != null
            ? lobbyUI
            : (lobbyHostPanel != null ? lobbyHostPanel.GetComponent<LobbyUI>() : null);
        if (targetLobbyUI == null)
        {
            targetLobbyUI = FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
        }

        if (targetLobbyUI != null)
        {
            targetLobbyUI.Initialize(isHost);
        }
        else
        {
            Debug.LogWarning("[NetworkUI] LobbyUI not found — Start Game button and player list won't initialize.");
        }

        // Display the join code for the host only; hide placeholder text for clients
        if (joinCodeDisplayText != null)
        {
            joinCodeDisplayText.gameObject.SetActive(isHost);
            if (isHost)
            {
                joinCodeDisplayText.text = $"JOIN CODE: {currentJoinCode}";
                Debug.Log($"[NetworkUI] Join code displayed: {currentJoinCode}");
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Username Prompt & Utilities
    // ─────────────────────────────────────────────

    public static bool HasConfirmedUsername()
    {
        return !string.IsNullOrWhiteSpace(LocalUsername);
    }

    public static string SanitizeUsername(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string trimmed = raw.Trim();
        while (trimmed.Length > 0 && Encoding.UTF8.GetByteCount(trimmed) > MaxUsernameBytes)
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        return trimmed;
    }

    /// <summary>
    /// Validates the entered username on Confirm/Submit, stores it in LocalUsername,
    /// closes the PlayerInput panel, displays the name in PlayerName, and unlocks menu actions.
    /// </summary>
    public void OnConfirmUsernameClicked()
    {
        string raw = (usernameInput != null) ? usernameInput.text : string.Empty;
        string sanitized = SanitizeUsername(raw);

        if (string.IsNullOrEmpty(sanitized))
        {
            LocalUsername = string.Empty;

            if (usernameInput != null)
            {
                usernameInput.text = string.Empty;
                usernameInput.Select();
                usernameInput.ActivateInputField();
            }

            SetPlaceholderWarning();
            SetButtonsInteractable(false);
            Debug.LogWarning("[NetworkUI] Username is required before continuing.");
            return;
        }

        LocalUsername = sanitized;
        ApplyConfirmedUsernameUI();
        Debug.Log($"[NetworkUI] Username confirmed: '{LocalUsername}'");
    }

    private void OnUsernameSubmitted(string _)
    {
        OnConfirmUsernameClicked();
    }

    private void OnUsernameInputChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            RestorePlaceholder();
        }
    }

    private bool EnsureConfirmedUsername()
    {
        if (HasConfirmedUsername())
        {
            return true;
        }

        Debug.LogWarning("[NetworkUI] Player must enter and confirm a username before hosting or joining.");
        ShowUsernamePrompt();
        SetPlaceholderWarning();
        return false;
    }

    private void ShowUsernamePrompt()
    {
        SetButtonsInteractable(false);

        if (playerNameText != null)
        {
            playerNameText.text = string.Empty;
        }

        if (playerInputPanel != null)
        {
            playerInputPanel.SetActive(true);
        }

        if (usernameInput != null)
        {
            usernameInput.text = string.Empty;
            usernameInput.Select();
            usernameInput.ActivateInputField();
        }
    }

    private void ApplyConfirmedUsernameUI()
    {
        RestorePlaceholder();

        if (usernameInput != null)
        {
            usernameInput.text = LocalUsername;
        }

        if (playerInputPanel != null)
        {
            playerInputPanel.SetActive(false);
        }

        if (playerNameText != null)
        {
            playerNameText.text = LocalUsername;
            playerNameText.gameObject.SetActive(true);
        }

        SetButtonsInteractable(true);
    }

    private void SetPlaceholderWarning()
    {
        if (usernameInput != null && usernameInput.placeholder is Text placeholderText)
        {
            placeholderText.text = "NAME REQUIRED!";
            placeholderText.color = new Color(0.8f, 0.12f, 0.12f, 0.9f);
        }
    }

    private void RestorePlaceholder()
    {
        if (!hasCachedPlaceholder) return;

        if (usernameInput != null && usernameInput.placeholder is Text placeholderText)
        {
            placeholderText.text = defaultPlaceholderText;
            placeholderText.color = defaultPlaceholderColor;
        }
    }

    private void ResolveUsernameReferences()
    {
        if (playerInputPanel == null && usernameInput != null && usernameInput.transform.parent != null)
        {
            playerInputPanel = usernameInput.transform.parent.gameObject;
        }

        if (confirmUsernameButton == null && playerInputPanel != null)
        {
            confirmUsernameButton = playerInputPanel.GetComponentInChildren<Button>(true);
        }

        if (playerNameText == null)
        {
            Canvas menuCanvas = hostButton != null ? hostButton.GetComponentInParent<Canvas>(true) : null;
            if (menuCanvas != null)
            {
                Transform found = menuCanvas.transform.Find("PlayerName");
                if (found != null)
                {
                    playerNameText = found.GetComponent<Text>();
                }
            }
        }
    }

    /// <summary>
    /// Enables or disables all connection buttons to prevent connecting before a username
    /// is confirmed or double-clicking during async operations.
    /// </summary>
    private void SetButtonsInteractable(bool interactable)
    {
        bool canConnect = interactable && HasConfirmedUsername();
        if (hostButton != null) hostButton.interactable = canConnect;
        if (joinButton != null) joinButton.interactable = canConnect;
        if (clientJoinButton != null) clientJoinButton.interactable = canConnect;
    }

    private void OnDestroy()
    {
        if (hostButton != null) hostButton.onClick.RemoveListener(OnHostClicked);
        if (joinButton != null) joinButton.onClick.RemoveListener(OnJoinClicked);
        if (clientJoinButton != null) clientJoinButton.onClick.RemoveListener(OnClientJoinClicked);
        if (confirmUsernameButton != null) confirmUsernameButton.onClick.RemoveListener(OnConfirmUsernameClicked);

        if (usernameInput != null)
        {
            usernameInput.onValueChanged.RemoveListener(OnUsernameInputChanged);
            usernameInput.onSubmit.RemoveListener(OnUsernameSubmitted);
        }
    }
}
