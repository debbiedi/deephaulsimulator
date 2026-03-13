using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Steamworks;
using FishNet;

/// <summary>
/// Ana menü / Lobi UI yöneticisi.
/// Canvas'taki butonları SteamLobbyManager'a bağlar.
/// 
/// Sahne: MainMenu
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("Paneller")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject lobbyListPanel;

    [Header("Ana Menü Butonları")]
    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Button friendsLobbyButton;
    [SerializeField] private Button findLobbyButton;
    [SerializeField] private Button quitButton;

    [Header("Lobi Paneli")]
    [SerializeField] private TextMeshProUGUI lobbyTitleText;
    [SerializeField] private TextMeshProUGUI playerListText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveLobbyButton;

    [Header("Lobi Listesi Paneli")]
    [SerializeField] private Transform lobbyListContent;
    [SerializeField] private GameObject lobbyListItemPrefab;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button backButton;

    [Header("Oyuncu Bilgisi")]
    [SerializeField] private TextMeshProUGUI playerNameText;

    [Header("Ayarlar")]
    [SerializeField] private string gameSceneName = "Sahne1";

    // Steam lobi listesi callback
    private CallResult<LobbyMatchList_t> _lobbyMatchListCallResult;

    private void Start()
    {
        // Steam adını göster
        if (SteamManager.Initialized && playerNameText != null)
        {
            playerNameText.text = SteamFriends.GetPersonaName();
        }

        // Buton event'lerini bağla
        SetupButtons();

        // Event'lere abone ol
        if (SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.OnLobbyCreated += OnLobbyCreated;
            SteamLobbyManager.Instance.OnLobbyJoined += OnLobbyJoined;
            SteamLobbyManager.Instance.OnLobbyLeft += OnLobbyLeft;
            SteamLobbyManager.Instance.OnPlayerJoined += OnPlayerChanged;
            SteamLobbyManager.Instance.OnPlayerLeft += OnPlayerChanged;
        }

        // Lobi listesi callback
        _lobbyMatchListCallResult = CallResult<LobbyMatchList_t>.Create(OnLobbyListReceived);

        // Başlangıçta ana menüyü göster
        ShowPanel(mainMenuPanel);
    }

    private void OnDestroy()
    {
        if (SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.OnLobbyCreated -= OnLobbyCreated;
            SteamLobbyManager.Instance.OnLobbyJoined -= OnLobbyJoined;
            SteamLobbyManager.Instance.OnLobbyLeft -= OnLobbyLeft;
            SteamLobbyManager.Instance.OnPlayerJoined -= OnPlayerChanged;
            SteamLobbyManager.Instance.OnPlayerLeft -= OnPlayerChanged;
        }
    }

    // ==================== Buton Ayarları ====================

    private void SetupButtons()
    {
        if (createLobbyButton != null)
            createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);

        if (friendsLobbyButton != null)
            friendsLobbyButton.onClick.AddListener(OnFriendsLobbyClicked);

        if (findLobbyButton != null)
            findLobbyButton.onClick.AddListener(OnFindLobbyClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameClicked);

        if (leaveLobbyButton != null)
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(OnRefreshClicked);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    // ==================== Ana Menü Butonları ====================

    private void OnCreateLobbyClicked()
    {
        Debug.Log("[LobbyUI] Herkese açık lobi oluşturuluyor...");
        SteamLobbyManager.Instance?.CreatePublicLobby();
    }

    private void OnFriendsLobbyClicked()
    {
        Debug.Log("[LobbyUI] Arkadaşlara özel lobi oluşturuluyor...");
        SteamLobbyManager.Instance?.CreateFriendsOnlyLobby();
    }

    private void OnFindLobbyClicked()
    {
        ShowPanel(lobbyListPanel);
        RefreshLobbyList();
    }

    private void OnQuitClicked()
    {
        Application.Quit();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    // ==================== Lobi Paneli Butonları ====================

    private void OnStartGameClicked()
    {
        if (SteamLobbyManager.Instance == null || !SteamLobbyManager.Instance.IsHost) return;

        Debug.Log("[LobbyUI] Oyun başlatılıyor!");

        // Lobi metadata güncelle - artık katılınamaz
        SteamMatchmaking.SetLobbyJoinable(SteamLobbyManager.Instance.CurrentLobbyId, false);

        // Oyun sahnesine geç (NetworkManager DontDestroyOnLoad olduğu için kalır)
        var sceneLoadData = new FishNet.Managing.Scened.SceneLoadData(gameSceneName);
        sceneLoadData.ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All;
        InstanceFinder.SceneManager.LoadGlobalScenes(sceneLoadData);
    }

    private void OnLeaveLobbyClicked()
    {
        SteamLobbyManager.Instance?.LeaveLobby();
        ShowPanel(mainMenuPanel);
    }

    // ==================== Lobi Listesi Butonları ====================

    private void OnRefreshClicked()
    {
        RefreshLobbyList();
    }

    private void OnBackClicked()
    {
        ShowPanel(mainMenuPanel);
    }

    // ==================== Lobi Listesi ====================

    private void RefreshLobbyList()
    {
        if (!SteamManager.Initialized) return;

        // Mevcut listeyi temizle
        if (lobbyListContent != null)
        {
            foreach (Transform child in lobbyListContent)
            {
                Destroy(child.gameObject);
            }
        }

        // Filtre
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", "DeepHaulSimulator", ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);

        var handle = SteamMatchmaking.RequestLobbyList();
        _lobbyMatchListCallResult.Set(handle);

        Debug.Log("[LobbyUI] Lobi listesi yenileniyor...");
    }

    private void OnLobbyListReceived(LobbyMatchList_t result, bool failure)
    {
        if (failure)
        {
            Debug.LogError("[LobbyUI] Lobi listesi alınamadı!");
            return;
        }

        Debug.Log($"[LobbyUI] {result.m_nLobbiesMatching} lobi bulundu.");

        for (int i = 0; i < result.m_nLobbiesMatching; i++)
        {
            CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
            string hostName = SteamMatchmaking.GetLobbyData(lobbyId, "host_name");
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int maxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);

            // Lobi listesi item'ı oluştur
            if (lobbyListContent != null && lobbyListItemPrefab != null)
            {
                GameObject item = Instantiate(lobbyListItemPrefab, lobbyListContent);
                
                // Item text'ini ayarla
                TextMeshProUGUI itemText = item.GetComponentInChildren<TextMeshProUGUI>();
                if (itemText != null)
                {
                    itemText.text = $"{hostName} ({memberCount}/{maxMembers})";
                }

                // Katılma butonu
                Button joinBtn = item.GetComponentInChildren<Button>();
                if (joinBtn != null)
                {
                    CSteamID capturedId = lobbyId; // Closure için
                    joinBtn.onClick.AddListener(() => {
                        SteamLobbyManager.Instance?.JoinLobby(capturedId);
                    });
                }
            }
            else
            {
                Debug.Log($"  Lobi: {hostName} ({memberCount}/{maxMembers}) - ID: {lobbyId}");
            }
        }
    }

    // ==================== Event Handler'lar ====================

    private void OnLobbyCreated()
    {
        ShowPanel(lobbyPanel);
        UpdateLobbyPanel();

        // Sadece host oyunu başlatabilir
        if (startGameButton != null)
            startGameButton.gameObject.SetActive(true);
    }

    private void OnLobbyJoined()
    {
        ShowPanel(lobbyPanel);
        UpdateLobbyPanel();

        // Client oyunu başlatamaz
        if (startGameButton != null)
            startGameButton.gameObject.SetActive(SteamLobbyManager.Instance.IsHost);
    }

    private void OnLobbyLeft()
    {
        ShowPanel(mainMenuPanel);
    }

    private void OnPlayerChanged(CSteamID _)
    {
        UpdateLobbyPanel();
    }

    // ==================== UI Güncelleme ====================

    private void UpdateLobbyPanel()
    {
        if (SteamLobbyManager.Instance == null || !SteamLobbyManager.Instance.IsInLobby) return;

        // Başlık
        if (lobbyTitleText != null)
        {
            string hostName = SteamLobbyManager.Instance.GetHostName();
            lobbyTitleText.text = $"{hostName}'in Lobisi";
        }

        // Oyuncu listesi
        int count = SteamLobbyManager.Instance.GetLobbyMemberCount();
        
        if (playerCountText != null)
        {
            playerCountText.text = $"Oyuncular: {count}/4";
        }

        if (playerListText != null)
        {
            string list = "";
            for (int i = 0; i < count; i++)
            {
                string name = SteamLobbyManager.Instance.GetLobbyMemberName(i);
                string prefix = (i == 0) ? "👑 " : "🎮 ";
                list += $"{prefix}{name}\n";
            }
            playerListText.text = list;
        }
    }

    // ==================== Panel Yönetimi ====================

    private void ShowPanel(GameObject panel)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(panel == mainMenuPanel);
        if (lobbyPanel != null) lobbyPanel.SetActive(panel == lobbyPanel);
        if (lobbyListPanel != null) lobbyListPanel.SetActive(panel == lobbyListPanel);
    }
}
