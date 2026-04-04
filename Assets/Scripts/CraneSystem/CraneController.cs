using UnityEngine;
using FishNet.Object;
using UnityEngine.InputSystem;

/// <summary>
/// Vinç kontrol paneli. Oyuncu yaklaşıp [E] tuşuyla sepeti indirir/çeker.
/// Kablo görselini yönetir (CraneCable component'ı ile).
///
/// Kurulum:
/// 1. Gemideki vinç objesine (veya CraneInteractionPoint'e) bu scripti ekleyin.
/// 2. Inspector'dan basket referansını CraneBasket'a atayın.
/// 3. cableTip'i vinç kolunun ucundaki Transform'a atayın.
/// 4. interactionRange'i ayarlayın (varsayılan 3m).
/// </summary>
public class CraneController : NetworkBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Vinç sepeti")]
    public CraneBasket basket;

    [Tooltip("Vinç kolunun ucundaki kablo bağlantı noktası")]
    public Transform cableTip;

    [Header("Etkileşim")]
    [Tooltip("Etkileşim mesafesi (metre)")]
    public float interactionRange = 4f;

    [Tooltip("Etkileşim tuşu")]
    public Key interactKey = Key.E;

    [Header("UI Prompt")]
    [Tooltip("Etkileşim prompt'u gösterilecek world-space pozisyon (boş bırakılırsa transform kullanılır)")]
    public Transform promptPoint;

    // --- PRIVATE ---
    private Transform _localPlayer;
    private bool _playerInRange;

    private void Update()
    {
        if (_localPlayer == null)
        {
            FindLocalPlayer();
            if (_localPlayer == null) return;
        }

        // Mesafe kontrolü
        float dist = Vector3.Distance(_localPlayer.position, transform.position);
        bool wasInRange = _playerInRange;
        _playerInRange = dist <= interactionRange;

        // Prompt göster/gizle
        if (_playerInRange != wasInRange)
        {
            OnRangeChanged(_playerInRange);
        }

        // Etkileşim girişi
        if (_playerInRange && Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
        {
            TryInteract();
        }
    }

    private void FindLocalPlayer()
    {
        // FishNet'te yerel oyuncunun transform'unu bul
        if (base.ClientManager != null && base.ClientManager.Connection != null 
            && base.ClientManager.Connection.Objects != null)
        {
            foreach (var nob in base.ClientManager.Connection.Objects)
            {
                if (nob != null && nob.GetComponent<PlayerMovementStateManager>() != null)
                {
                    _localPlayer = nob.transform;
                    Debug.Log("[CraneController] Yerel oyuncu bulundu.");
                    break;
                }
            }
        }

        // Yedek yöntem: sahnedeki tüm PlayerMovementStateManager'ları tara
        if (_localPlayer == null)
        {
            var allPlayers = FindObjectsByType<PlayerMovementStateManager>(FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p.IsOwner)
                {
                    _localPlayer = p.transform;
                    Debug.Log("[CraneController] Yerel oyuncu bulundu (yedek yöntem).");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Sepet durumuna göre uygun komutu gönderir.
    /// </summary>
    private void TryInteract()
    {
        if (basket == null) return;

        CraneBasketState state = basket.State.Value;

        if (state == CraneBasketState.Idle)
        {
            ServerRequestLower();
        }
        else if (state == CraneBasketState.Lowered)
        {
            ServerRequestRaise();
        }
        // Hareket halinde veya boşaltma sırasında işlem yapma
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerRequestLower()
    {
        if (basket != null)
            basket.ServerLower();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerRequestRaise()
    {
        if (basket != null)
            basket.ServerRaise();
    }

    // ==========================================
    // PROMPT UI
    // ==========================================

    private void OnRangeChanged(bool inRange)
    {
        // TODO: World-space UI prompt göster/gizle
        // Şimdilik Debug log ile kontrol
        if (inRange)
        {
            string action = GetPromptText();
            Debug.Log($"[CraneController] {action}");
        }
    }

    /// <summary>
    /// Mevcut duruma göre etkileşim metnini döndürür.
    /// </summary>
    public string GetPromptText()
    {
        if (basket == null) return "";

        switch (basket.State.Value)
        {
            case CraneBasketState.Idle:
                return "[E] Sepeti İndir";
            case CraneBasketState.Lowered:
                return "[E] Sepeti Çek";
            case CraneBasketState.Lowering:
                return "Sepet iniyor...";
            case CraneBasketState.Raising:
                return "Sepet çekiliyor...";
            case CraneBasketState.Unloading:
                return "Boşaltılıyor...";
            default:
                return "";
        }
    }

    /// <summary>
    /// Oyuncu etkileşim menzilinde mi?
    /// </summary>
    public bool IsPlayerInRange()
    {
        return _playerInRange;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
#endif
}
