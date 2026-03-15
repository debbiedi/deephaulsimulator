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

    [Header("Ana Menü Lobi Oluşturma Ayarları")]
    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Toggle isPrivateToggle; // [YENİ] Gizli oda yapılandırması
    [SerializeField] private TMP_InputField createPasswordInput; // [YENİ] Şifre belirleme alanı

    [Header("Ana Menü Diğer Butonlar")]
    [SerializeField] private Button findLobbyButton;
    [SerializeField] private Button quitButton;

    [Header("Şifre Sorma Paneli (Popup)")] // [YENİ] Şifreli oda için popup arayüzü
    [SerializeField] private GameObject passwordPromptPanel;
    [SerializeField] private TMP_InputField joinPasswordInput;
    [SerializeField] private Button confirmJoinButton;
    [SerializeField] private Button cancelJoinButton;
    [SerializeField] private TextMeshProUGUI passwordErrorText;

    [Header("Lobi Paneli İçi")]
    [SerializeField] private TextMeshProUGUI lobbyTitleText;
    [SerializeField] private Transform playerListContent; // [YENİ] playerListText yerine Container
    [SerializeField] private GameObject playerLobbyItemPrefab; // [YENİ] İçinde: Text(Oyuncu Adı) ve Button(Kick)
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button inviteFriendsButton; // [YENİ] Steam arayüzü davet et butonu
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
    
    // Şifreli odaya katılırken kullanılacak geçici hafıza
    private CSteamID _pendingJoinLobbyId;
    private string _expectedPassword;

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

        if (passwordPromptPanel != null) passwordPromptPanel.SetActive(false);

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
        if (createLobbyButton != null) createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);
        if (findLobbyButton != null) findLobbyButton.onClick.AddListener(OnFindLobbyClicked);
        if (quitButton != null) quitButton.onClick.AddListener(() => Application.Quit());

        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (leaveLobbyButton != null) leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);
        if (refreshButton != null) refreshButton.onClick.AddListener(RefreshLobbyList);
        if (backButton != null) backButton.onClick.AddListener(() => ShowPanel(mainMenuPanel));

        // Davet ve Şifre butonları
        if (inviteFriendsButton != null) inviteFriendsButton.onClick.AddListener(() => SteamLobbyManager.Instance?.InviteFriends());
        if (confirmJoinButton != null) confirmJoinButton.onClick.AddListener(OnConfirmJoinClicked);
        if (cancelJoinButton != null) cancelJoinButton.onClick.AddListener(OnCancelJoinClicked);
    }

    // ==================== Ana Menü Butonları ====================

    private void OnCreateLobbyClicked()
    {
        bool isPrivate = isPrivateToggle != null && isPrivateToggle.isOn;
        string pwd = createPasswordInput != null ? createPasswordInput.text : "";
        SteamLobbyManager.Instance?.CreatePublicLobby(isPrivate, pwd);
    }

    private void OnFindLobbyClicked()
    {
        ShowPanel(lobbyListPanel);
        RefreshLobbyList();
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
    }

    // ==================== Lobi Listesi ====================

    private void RefreshLobbyList()
    {
        if (!SteamManager.Initialized) return;

        // Mevcut listeyi temizle
        if (lobbyListContent != null)
        {
            foreach (Transform child in lobbyListContent) Destroy(child.gameObject);
        }

        if (SteamLobbyManager.Instance != null)
        {
            var handle = SteamLobbyManager.Instance.RequestLobbyList();
            _lobbyMatchListCallResult.Set(handle);
        }

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
            string isPrivateStr = SteamMatchmaking.GetLobbyData(lobbyId, "is_private");
            string actualPassword = SteamMatchmaking.GetLobbyData(lobbyId, "password");
            
            bool isPrivate = (isPrivateStr == "true");
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int maxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);

            // Lobi listesi item'ı oluştur
            if (lobbyListContent != null && lobbyListItemPrefab != null)
            {
                GameObject item = Instantiate(lobbyListItemPrefab, lobbyListContent);
                TextMeshProUGUI itemText = item.GetComponentInChildren<TextMeshProUGUI>();
                
                if (itemText != null)
                {
                    string lockIcon = isPrivate ? "🔒 " : "";
                    itemText.text = $"{lockIcon}{hostName} ({memberCount}/{maxMembers})";
                }

                Button joinBtn = item.GetComponentInChildren<Button>();
                if (joinBtn != null)
                {
                    joinBtn.onClick.AddListener(() => {
                        if (isPrivate) ShowPasswordPrompt(lobbyId, actualPassword);
                        else SteamLobbyManager.Instance?.JoinLobby(lobbyId);
                    });
                }
            }
        }
    }
    
    // ==================== Şifre İşlemleri ====================

    private void ShowPasswordPrompt(CSteamID lobbyId, string actualPassword)
    {
        _pendingJoinLobbyId = lobbyId;
        _expectedPassword = actualPassword;

        if (passwordPromptPanel != null) passwordPromptPanel.SetActive(true);
        if (joinPasswordInput != null) joinPasswordInput.text = "";
        if (passwordErrorText != null) passwordErrorText.text = "";
    }

    private void OnConfirmJoinClicked()
    {
        if (joinPasswordInput != null && joinPasswordInput.text == _expectedPassword)
        {
            if (passwordPromptPanel != null) passwordPromptPanel.SetActive(false);
            SteamLobbyManager.Instance?.JoinLobby(_pendingJoinLobbyId);
        }
        else if (passwordErrorText != null)
        {
            passwordErrorText.text = "Yanlış Şifre!";
        }
    }

    private void OnCancelJoinClicked()
    {
        if (passwordPromptPanel != null) passwordPromptPanel.SetActive(false);
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
            lobbyTitleText.text = $"{SteamLobbyManager.Instance.GetHostName()}'in Lobisi";
        }

        // Oyuncu listesi
        int count = SteamLobbyManager.Instance.GetLobbyMemberCount();
        if (playerCountText != null) playerCountText.text = $"Oyuncular: {count}/4";
        
        // Dinamik Oyuncu Prefablarını Üret (Eski text listesi yerine GameObject bazlı)
        if (playerListContent != null && playerLobbyItemPrefab != null)
        {
            foreach (Transform child in playerListContent) Destroy(child.gameObject);

            for (int i = 0; i < count; i++)
            {
                CSteamID memberId = SteamLobbyManager.Instance.GetLobbyMemberId(i);
                string name = SteamLobbyManager.Instance.GetLobbyMemberName(i);
                bool isMe = (memberId == SteamUser.GetSteamID());
                bool isRoomHost = (i == 0); // Steam lobi kurucusunu 0. index sayarız

                GameObject item = Instantiate(playerLobbyItemPrefab, playerListContent);
                
                // İsim Yazdır
                TextMeshProUGUI txt = item.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                {
                    string prefix = isRoomHost ? "👑 " : "🎮 ";
                    string suffix = isMe ? " (Sen)" : "";
                    txt.text = $"{prefix}{name}{suffix}";
                }

                // Kick Butonu Belirleme
                Button kickBtn = item.GetComponentInChildren<Button>();
                if (kickBtn != null)
                {
                    // Hostsa ve kendisi değilse [At] butonu gözüksün
                    if (SteamLobbyManager.Instance.IsHost && !isMe)
                    {
                        kickBtn.gameObject.SetActive(true);
                        kickBtn.onClick.AddListener(() => SteamLobbyManager.Instance.KickPlayer(memberId));
                    }
                    else
                    {
                        kickBtn.gameObject.SetActive(false);
                    }
                }
            }
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
