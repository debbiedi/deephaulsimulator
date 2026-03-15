using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using System;

/// <summary>
/// Oyuncunun kemer çantası envanter sistemi.
/// Player prefabına eklenir. NetworkBehaviour olarak çalışır.
/// SyncList ile envanter tüm istemcilere senkronize edilir.
/// </summary>
public class BeltBagInventory : NetworkBehaviour
{
    [Header("Çanta Ayarları")]
    [Tooltip("Maksimum taşınabilir ağırlık (kg)")]
    public float maxWeight = 20f;

    [Tooltip("Maksimum eşya slotu")]
    public int maxSlots = 30;

    // --- Synced State ---
    // FishNet SyncList: Envanter tüm istemcilere otomatik senkronize olur
    public readonly SyncList<ItemData> Items = new SyncList<ItemData>();

    // Toplam ağırlık (SyncVar ile senkronize)
    public readonly SyncVar<float> CurrentWeight = new SyncVar<float>();

    // --- Events (client-side UI ve ses bildirimleri için) ---
    public event Action<ItemData> OnItemAdded;
    public event Action<float> OnWeightChanged;

    public override void OnStartClient()
    {
        base.OnStartClient();
        Items.OnChange += OnItemsChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Items.OnChange -= OnItemsChanged;
    }

    private void OnItemsChanged(SyncListOperation op, int index,
        ItemData oldItem, ItemData newItem, bool asServer)
    {
        // Tüm istemcilerde çalışır - gerektiğinde UI güncellemesi için kullanılabilir
    }

    /// <summary>
    /// Eşya eklenebilir mi kontrol et (ağırlık + slot).
    /// Client tarafında hızlı ön kontrol. Gerçek doğrulama sunucuda yapılır.
    /// </summary>
    public bool CanAddItem(GrabbableObject grabbable)
    {
        if (Items.Count >= maxSlots) return false;
        if (CurrentWeight.Value + grabbable.weight > maxWeight) return false;
        return true;
    }

    /// <summary>
    /// Client -> Server: Eşyayı çantaya ekle.
    /// PlayerGrabber.Release() tarafından çağrılır.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    public void ServerCollectItem(NetworkObject itemNetObj)
    {
        // Sunucu tarafı doğrulama
        if (itemNetObj == null) return;

        GrabbableObject grabbable = itemNetObj.GetComponent<GrabbableObject>();
        if (grabbable == null) return;
        if (grabbable.itemSize != ItemSize.Small) return;

        // Ağırlık ve slot kontrolü (server authoritative)
        ItemData data = new ItemData(grabbable);
        if (Items.Count >= maxSlots) return;
        if (CurrentWeight.Value + data.Weight > maxWeight) return;

        // Envantere ekle (SyncList otomatik senkronize eder)
        Items.Add(data);
        CurrentWeight.Value += data.Weight;

        // Objeyi ağdan despawn et (tüm istemcilerde kaybolur)
        base.ServerManager.Despawn(itemNetObj);

        Debug.Log($"[BeltBag] '{data.ItemName}' toplandı! Ağırlık: {CurrentWeight.Value:F1}/{maxWeight} kg | Eşya: {Items.Count}/{maxSlots} | Toplam Değer: {GetTotalValue():F0}₺");

        // Toplayan oyuncuya bildirim gönder
        TargetNotifyItemCollected(base.Owner, data);
    }

    /// <summary>
    /// Server -> Owner Client: Eşya toplandı bildirimi.
    /// Sadece eşyayı toplayan oyuncunun istemcisinde çalışır.
    /// </summary>
    [TargetRpc]
    private void TargetNotifyItemCollected(NetworkConnection conn, ItemData data)
    {
        OnItemAdded?.Invoke(data);
        OnWeightChanged?.Invoke(CurrentWeight.Value);
    }

    /// <summary>
    /// Envanterdeki tüm eşyaların toplam değerini hesapla.
    /// </summary>
    public float GetTotalValue()
    {
        float total = 0f;
        foreach (var item in Items)
        {
            total += item.CurrentPrice;
        }
        return total;
    }
}
