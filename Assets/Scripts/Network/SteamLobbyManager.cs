using UnityEngine;
using Steamworks;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Broadcast;
using System;

/// <summary>
/// Oyuncuyu odadan atmak için kullanılacak FishNet mesajı (Broadcast)
/// </summary>
public struct KickBroadcast : IBroadcast
{
    public ulong TargetSteamId;
}

/// <summary>
/// Steam Lobby yönetim sistemi.
/// Lobi oluşturma, katılma, arkadaş davet etme ve FishNet bağlantılarını yönetir.
/// </summary>
public class SteamLobbyManager : MonoBehaviour
{
    public static SteamLobbyManager Instance { get; private set; }

    [Header("Lobi Ayarları")]
    [Tooltip("Maksimum oyuncu sayısı")]
    public int maxPlayers = 4;

    [Tooltip("Lobi görünürlüğü")]
    public ELobbyType lobbyType = ELobbyType.k_ELobbyTypePublic;

    // Mevcut lobi ID
    public CSteamID CurrentLobbyId { get; private set; }

    // Lobi aktif mi?
    public bool IsInLobby { get; private set; }

    // Host mu?
    public bool IsHost { get; private set; }

    // Geçici şifre bilgileri
    private bool _pendingIsPrivate;
    private string _pendingPassword;

    // Events
    public event Action OnLobbyCreated;
    public event Action OnLobbyJoined;
    public event Action OnLobbyLeft;
    public event Action<CSteamID> OnPlayerJoined;
    public event Action<CSteamID> OnPlayerLeft;

    // Steam Callbacks
    private Callback<LobbyCreated_t> _lobbyCreatedCallback;
    private Callback<LobbyEnter_t> _lobbyEnteredCallback;
    private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequestedCallback;
    private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;

    // FishNet referansı
    private NetworkManager _networkManager;

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Steam başlatılmış mı kontrol et
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobbyManager] Steam başlatılamadı! Steam açık olduğundan emin olun.");
            return;
        }

        _networkManager = InstanceFinder.NetworkManager;
        if (_networkManager == null)
        {
            Debug.LogError("[SteamLobbyManager] NetworkManager bulunamadı!");
            return;
        }

        // Steam callback'lerini kaydet
        _lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnSteamLobbyCreated);
        _lobbyEnteredCallback = Callback<LobbyEnter_t>.Create(OnSteamLobbyEntered);
        _lobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnSteamLobbyJoinRequested);
        _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnSteamLobbyChatUpdate);

        // FishNet bağlantı event'leri
        _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionStateChanged;
        _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionStateChanged;
        
        // Atılma (Kick) mesajını dinle
        _networkManager.ClientManager.RegisterBroadcast<KickBroadcast>(OnKickBroadcastReceived);

        string playerName = SteamFriends.GetPersonaName();
        Debug.Log($"[SteamLobbyManager] Steam başlatıldı! Oyuncu: {playerName}");
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionStateChanged;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionStateChanged;
            _networkManager.ClientManager.UnregisterBroadcast<KickBroadcast>(OnKickBroadcastReceived);
        }
    }

    private CallResult<LobbyCreated_t> _lobbyCreatedCallResult;

    // ==================== Lobi Oluşturma ====================

    /// <summary>
    /// Yeni bir Steam lobisi oluşturur.
    /// </summary>
    public void CreateLobby(bool isPrivate = false, string password = "")
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobbyManager] Steam başlatılmamış!");
            return;
        }

        if (IsInLobby)
        {
            Debug.LogWarning("[SteamLobbyManager] Zaten bir lobidesiniz! Önce çıkın.");
            return;
        }

        _pendingIsPrivate = isPrivate;
        _pendingPassword = password;

        Debug.Log($"[SteamLobbyManager] Lobi oluşturuluyor... (Tip: {lobbyType}, Max: {maxPlayers})");
        
        SteamAPICall_t handle = SteamMatchmaking.CreateLobby(lobbyType, maxPlayers);
        
        if (_lobbyCreatedCallResult == null)
            _lobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnCallResultLobbyCreated);
            
        _lobbyCreatedCallResult.Set(handle);
        Debug.Log("[SteamLobbyManager] Steam'e istek iletildi, CallResult bekleniyor...");
    }

    private void OnCallResultLobbyCreated(LobbyCreated_t callback, bool bIOFailure)
    {
        Debug.Log($"[SteamLobbyManager] CallResult Döndü! bIOFailure: {bIOFailure}, Result: {callback.m_eResult}");
        
        if (bIOFailure || callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"[SteamLobbyManager] Lobi oluşturulamadı (CallResult)! Hata Kodu: {callback.m_eResult} G/Ç Hatası: {bIOFailure}");
            return;
        }

        CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        IsInLobby = true;
        IsHost = true;

        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "game", "DeepHaulSimulator");
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "host_name", SteamFriends.GetPersonaName());
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "host_id", SteamUser.GetSteamID().ToString());
        
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "is_private", _pendingIsPrivate ? "true" : "false");
        if (_pendingIsPrivate)
        {
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, "password", _pendingPassword);
        }

        Debug.Log("[SteamLobbyManager] Sunucu bağlantısı başlatılıyor...");
        
        try 
        {
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();
            Debug.Log("[SteamLobbyManager] FishNet sunucu+istemci başlatma emri verildi.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SteamLobbyManager] FishNet bağlantı başlatılırken HATA oluştu: {ex}");
        }

        try 
        {
            OnLobbyCreated?.Invoke();
            Debug.Log("[SteamLobbyManager] OnLobbyCreated Event'leri ateşlendi.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SteamLobbyManager] OnLobbyCreated Event'inde içsel bir HATA var: {ex}");
        }

        Debug.Log($"[SteamLobbyManager] ✅ Lobi oluşturuldu! ID: {CurrentLobbyId}");
    }

    /// <summary>
    /// Herkese açık lobi oluştur.
    /// </summary>
    public void CreatePublicLobby(bool isPrivate = false, string password = "")
    {
        lobbyType = ELobbyType.k_ELobbyTypePublic;
        CreateLobby(isPrivate, password);
    }

    /// <summary>
    /// Sadece arkadaşlara açık lobi oluştur. (Mevcut referanslar kırılmasın diye eklendi)
    /// </summary>
    public void CreateFriendsOnlyLobby()
    {
        lobbyType = ELobbyType.k_ELobbyTypeFriendsOnly;
        CreateLobby();
    }

    // ==================== Lobiye Katılma ====================

    /// <summary>
    /// Belirli bir Steam lobisine katılır.
    /// </summary>
    public void JoinLobby(CSteamID lobbyId)
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobbyManager] Steam başlatılmamış!");
            return;
        }

        if (IsInLobby)
        {
            LeaveLobby();
        }

        Debug.Log($"[SteamLobbyManager] Lobiye katılınıyor: {lobbyId}");
        SteamMatchmaking.JoinLobby(lobbyId);
    }

    /// <summary>
    /// Herkese açık lobileri listeler.
    /// </summary>
    public SteamAPICall_t RequestLobbyList()
    {
        if (!SteamManager.Initialized) return default;

        // Filtre: Aynı oyun
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", "DeepHaulSimulator", ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);

        Debug.Log("[SteamLobbyManager] Lobi listesi isteniyor...");
        return SteamMatchmaking.RequestLobbyList();
    }

    // ==================== Arkadaş Daveti ====================

    public void InviteFriends()
    {
        if (SteamManager.Initialized && IsInLobby)
        {
            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobbyId);
        }
    }

    // ==================== Oyuncu Atma ====================

    public void KickPlayer(CSteamID targetId)
    {
        if (!IsHost || targetId == SteamUser.GetSteamID()) return;

        // Bütün bağlı istemcilere Kick mesajı fırlat (İlgili kişi alınca sistemi kapatacak)
        KickBroadcast msg = new KickBroadcast() { TargetSteamId = targetId.m_SteamID };
        _networkManager.ServerManager.Broadcast(msg);
        Debug.Log($"[SteamLobbyManager] Oyuncu atma isteği gönderildi: {targetId}");
    }

    private void OnKickBroadcastReceived(KickBroadcast msg, Channel channel)
    {
        if (msg.TargetSteamId == SteamUser.GetSteamID().m_SteamID)
        {
            Debug.Log("[SteamLobbyManager] Sunucu tarafından odadan atıldınız!");
            LeaveLobby(); 
        }
    }

    // ==================== Lobiden Çıkma ====================

    /// <summary>
    /// Mevcut lobiden çıkar ve ağ bağlantısını kapatır.
    /// </summary>
    public void LeaveLobby()
    {
        if (!IsInLobby) return;

        // Ağ bağlantısını kapat
        if (IsHost)
        {
            _networkManager.ServerManager.StopConnection(true);
        }
        else
        {
            _networkManager.ClientManager.StopConnection();
        }

        // Steam lobisinden çık
        SteamMatchmaking.LeaveLobby(CurrentLobbyId);

        IsInLobby = false;
        IsHost = false;
        CurrentLobbyId = CSteamID.Nil;

        OnLobbyLeft?.Invoke();
        Debug.Log("[SteamLobbyManager] Lobiden çıkıldı.");
    }

    // ==================== Steam Callback'leri ====================

    /// <summary>
    /// Lobi başarıyla oluşturulduğunda çağrılır.
    /// </summary>
    private void OnSteamLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"[SteamLobbyManager] Lobi oluşturulamadı: {callback.m_eResult}");
            return;
        }

        CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        IsInLobby = true;
        IsHost = true;

        // Lobi metadata ayarla
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "game", "DeepHaulSimulator");
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "host_name", SteamFriends.GetPersonaName());
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "host_id", SteamUser.GetSteamID().ToString());
        
        // Gizli oda bilgilerini Steam'e belirle
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "is_private", _pendingIsPrivate ? "true" : "false");
        if (_pendingIsPrivate)
        {
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, "password", _pendingPassword);
        }

        // Host olarak FishNet server + client başlat
        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();

        OnLobbyCreated?.Invoke();
        Debug.Log($"[SteamLobbyManager] ✅ Lobi oluşturuldu! ID: {CurrentLobbyId}");
    }

    /// <summary>
    /// Bir lobiye girildiğinde çağrılır (oluşturan veya katılan için).
    /// </summary>
    private void OnSteamLobbyEntered(LobbyEnter_t callback)
    {
        CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        IsInLobby = true;

        // Eğer host değilsek, client olarak bağlan
        if (!IsHost)
        {
            // Host'un Steam ID'sini al
            string hostIdStr = SteamMatchmaking.GetLobbyData(CurrentLobbyId, "host_id");
            if (!string.IsNullOrEmpty(hostIdStr))
            {
                // FishySteamworks client adresini host'un Steam ID'si olarak ayarla
                _networkManager.ClientManager.StartConnection(hostIdStr);
                Debug.Log($"[SteamLobbyManager] ✅ Lobiye katılındı! Host'a bağlanılıyor: {hostIdStr}");
            }
        }

        OnLobbyJoined?.Invoke();

        // Lobideki oyuncuları logla
        int memberCount = SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyId);
        Debug.Log($"[SteamLobbyManager] Lobide {memberCount} oyuncu var.");
        for (int i = 0; i < memberCount; i++)
        {
            CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbyId, i);
            string memberName = SteamFriends.GetFriendPersonaName(memberId);
            Debug.Log($"  - {memberName} ({memberId})");
        }
    }

    /// <summary>
    /// Steam'den lobi daveti geldiğinde çağrılır (arkadaş davet etti).
    /// </summary>
    private void OnSteamLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        Debug.Log($"[SteamLobbyManager] Lobi daveti alındı: {callback.m_steamIDLobby}");
        JoinLobby(callback.m_steamIDLobby);
    }

    /// <summary>
    /// Lobi üye değişiklikleri (katılma/çıkma).
    /// </summary>
    private void OnSteamLobbyChatUpdate(LobbyChatUpdate_t callback)
    {
        CSteamID userId = new CSteamID(callback.m_ulSteamIDUserChanged);
        string playerName = SteamFriends.GetFriendPersonaName(userId);

        EChatMemberStateChange stateChange = (EChatMemberStateChange)callback.m_rgfChatMemberStateChange;

        if (stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeEntered))
        {
            Debug.Log($"[SteamLobbyManager] 🟢 {playerName} lobiye katıldı!");
            OnPlayerJoined?.Invoke(userId);
        }
        else if (stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeLeft) ||
                 stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeDisconnected))
        {
            Debug.Log($"[SteamLobbyManager] 🔴 {playerName} lobiden ayrıldı.");
            OnPlayerLeft?.Invoke(userId);
        }
    }

    // ==================== FishNet Bağlantı Event'leri ====================

    private void OnServerConnectionStateChanged(ServerConnectionStateArgs args)
    {
        Debug.Log($"[SteamLobbyManager] Server durumu: {args.ConnectionState}");
    }

    private void OnClientConnectionStateChanged(ClientConnectionStateArgs args)
    {
        Debug.Log($"[SteamLobbyManager] Client durumu: {args.ConnectionState}");

        if (args.ConnectionState == LocalConnectionState.Stopped && IsInLobby && !IsHost)
        {
            Debug.Log("[SteamLobbyManager] Sunucuyla bağlantı kesildi.");
            LeaveLobby();
        }
    }

    // ==================== Yardımcı Metotlar ====================

    /// <summary>
    /// Lobideki oyuncu sayısı.
    /// </summary>
    public int GetLobbyMemberCount()
    {
        if (!IsInLobby) return 0;
        return SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyId);
    }
    
    /// <summary>
    /// Lobideki oyuncunun Steam Id'sini getirir.
    /// </summary>
    public CSteamID GetLobbyMemberId(int index)
    {
        if (!IsInLobby) return CSteamID.Nil;
        return SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbyId, index);
    }

    /// <summary>
    /// Lobideki bir oyuncunun Steam adı.
    /// </summary>
    public string GetLobbyMemberName(int index)
    {
        if (!IsInLobby) return string.Empty;
        CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbyId, index);
        return SteamFriends.GetFriendPersonaName(memberId);
    }

    /// <summary>
    /// Lobi sahibinin adı.
    /// </summary>
    public string GetHostName()
    {
        if (!IsInLobby) return string.Empty;
        return SteamMatchmaking.GetLobbyData(CurrentLobbyId, "host_name");
    }
}
