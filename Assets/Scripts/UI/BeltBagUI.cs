using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Kemer çantası UI yöneticisi.
/// Eşya toplama bildirimlerini gösterir ve envanter bilgisini günceller.
/// </summary>
public class BeltBagUI : MonoBehaviour
{
    [Header("Referanslar")]
    public BeltBagInventory inventory;

    [Header("HUD - Daimi Bilgi")]
    public TextMeshProUGUI weightText;     // "Çanta: 5.2 / 20 kg"
    public TextMeshProUGUI itemCountText;  // "Eşya: 12 / 30"
    public TextMeshProUGUI totalValueText; // "Toplam: 1,250₺"

    [Header("Bildirim Sistemi")]
    public Transform notificationParent;           // Bildirimlerin oluşturulacağı parent
    public GameObject notificationPrefab;           // PickupNotification prefab'ı
    public float notificationDuration = 2.5f;
    public int maxVisibleNotifications = 4;

    private Queue<GameObject> _activeNotifications = new Queue<GameObject>();

    void Start()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded += HandleItemAdded;
            inventory.OnWeightChanged += HandleWeightChanged;
        }
        UpdateHUD();
    }

    void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= HandleItemAdded;
            inventory.OnWeightChanged -= HandleWeightChanged;
        }
    }

    private void HandleItemAdded(ItemData item)
    {
        ShowNotification($"+1 {item.ItemName}", $"Değer: {Mathf.Round(item.CurrentPrice)}₺");
        UpdateHUD();
    }

    private void HandleWeightChanged(float newWeight)
    {
        UpdateHUD();
    }

    private void ShowNotification(string title, string subtitle)
    {
        if (notificationPrefab == null || notificationParent == null) return;

        // Eski bildirimleri sil (max aşıldıysa)
        while (_activeNotifications.Count >= maxVisibleNotifications)
        {
            GameObject oldest = _activeNotifications.Dequeue();
            if (oldest != null) Destroy(oldest);
        }

        GameObject notif = Instantiate(notificationPrefab, notificationParent);
        PickupNotification comp = notif.GetComponent<PickupNotification>();
        if (comp != null)
        {
            comp.Setup(title, subtitle, notificationDuration);
        }
        _activeNotifications.Enqueue(notif);
    }

    private void UpdateHUD()
    {
        if (inventory == null) return;

        if (weightText != null)
            weightText.text = $"Çanta: {inventory.CurrentWeight.Value:F1} / {inventory.maxWeight} kg";
        if (itemCountText != null)
            itemCountText.text = $"Eşya: {inventory.Items.Count} / {inventory.maxSlots}";
        if (totalValueText != null)
            totalValueText.text = $"Toplam: {Mathf.Round(inventory.GetTotalValue())}₺";
    }
}
