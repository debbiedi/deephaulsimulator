using UnityEngine;
using TMPro;
using FishNet.Object;

/// <summary>
/// Para HUD göstergesi.
/// Kişisel cüzdan ve ortak kasa bakiyelerini gösterir.
/// Hızlı satış bildirimlerini ekrana yazar.
/// Player runtime'da spawn olduğunda cüzdanı otomatik bulur.
/// </summary>
public class WalletUI : MonoBehaviour
{
    [Header("HUD - Para Göstergesi")]
    [Tooltip("Kişisel bakiye metni (örn: 'Cüzdan: 1,250₺')")]
    public TextMeshProUGUI personalBalanceText;

    [Tooltip("Ortak kasa metni (örn: 'Kasa: 5,400₺')")]
    public TextMeshProUGUI treasuryText;

    [Header("Hızlı Satış Bildirimi")]
    [Tooltip("Satış bildirimi metni ('+15₺ Satıldı!' gibi kısa süre görünür)")]
    public TextMeshProUGUI quickSellNotifText;

    [Tooltip("Bildirimin ekranda kalma süresi (saniye)")]
    public float notifDuration = 2f;

    private PlayerWallet _wallet;
    private float _notifTimer;
    private bool _walletFound;

    void Start()
    {
        HideNotification();
    }

    void OnDestroy()
    {
        UnbindWallet();

        if (TeamTreasury.Instance != null)
            TeamTreasury.Instance.OnTreasuryChanged -= HandleTreasuryChanged;
    }

    void Update()
    {
        // Bildirim zamanlayıcısı
        if (_notifTimer > 0f)
        {
            _notifTimer -= Time.deltaTime;
            if (_notifTimer <= 0f)
                HideNotification();
        }

        // Cüzdan henüz bulunmadıysa her frame ara (player spawn olana kadar)
        if (!_walletFound)
        {
            TryFindLocalWallet();
        }

        // TeamTreasury geç başlatılabilir, kontrol et
        if (TeamTreasury.Instance != null && treasuryText != null)
            treasuryText.text = $"Kasa: {Mathf.Round(TeamTreasury.Instance.SharedBalance.Value)}₺";
    }

    /// <summary>
    /// Sahnedeki tüm PlayerWallet'ları tarar, bizim oyuncumuzu (IsOwner) bulur.
    /// Player runtime'da spawn olduğu için Inspector'dan atanamaz.
    /// </summary>
    private void TryFindLocalWallet()
    {
        PlayerWallet[] wallets = FindObjectsByType<PlayerWallet>(FindObjectsSortMode.None);
        foreach (var w in wallets)
        {
            if (w.IsOwner)
            {
                BindWallet(w);
                return;
            }
        }
    }

    private void BindWallet(PlayerWallet wallet)
    {
        _wallet = wallet;
        _walletFound = true;

        _wallet.OnMoneyChanged += HandleMoneyChanged;
        _wallet.OnQuickSellCompleted += HandleQuickSell;

        // Ortak kasa event'ine de bağlan
        if (TeamTreasury.Instance != null)
            TeamTreasury.Instance.OnTreasuryChanged += HandleTreasuryChanged;

        UpdateHUD();
        Debug.Log("[WalletUI] Oyuncu cüzdanı bulundu ve bağlandı.");
    }

    private void UnbindWallet()
    {
        if (_wallet != null)
        {
            _wallet.OnMoneyChanged -= HandleMoneyChanged;
            _wallet.OnQuickSellCompleted -= HandleQuickSell;
        }
    }

    private void HandleMoneyChanged(float amount, float newBalance)
    {
        UpdateHUD();
    }

    private void HandleQuickSell(float sellPrice, string itemName)
    {
        ShowSellNotification(sellPrice, itemName);
        UpdateHUD();
    }

    private void HandleTreasuryChanged(float newBalance)
    {
        if (treasuryText != null)
            treasuryText.text = $"Kasa: {Mathf.Round(newBalance)}₺";
    }

    private void UpdateHUD()
    {
        if (_wallet != null && personalBalanceText != null)
            personalBalanceText.text = $"Cüzdan: {Mathf.Round(_wallet.PersonalBalance.Value)}₺";

        if (TeamTreasury.Instance != null && treasuryText != null)
            treasuryText.text = $"Kasa: {Mathf.Round(TeamTreasury.Instance.SharedBalance.Value)}₺";
    }

    private void ShowSellNotification(float price, string itemName)
    {
        if (quickSellNotifText == null) return;

        quickSellNotifText.text = $"+{Mathf.Round(price)}₺ {itemName} satıldı!";
        quickSellNotifText.gameObject.SetActive(true);
        _notifTimer = notifDuration;
    }

    private void HideNotification()
    {
        if (quickSellNotifText != null)
            quickSellNotifText.gameObject.SetActive(false);
    }
}
