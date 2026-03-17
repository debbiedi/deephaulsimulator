using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;

/// <summary>
/// Gemi kargo envanteri. Gemi objesine eklenir (NetworkBehaviour).
/// Tüm oyuncuların topladığı eşyalar ortak kargoya girer.
/// SyncList ile envanter tüm istemcilere senkronize edilir.
///
/// Kurulum:
/// - Gemi root objesine bu scripti ekleyin.
/// - ShipCollector bu scripti referans alır.
/// </summary>
public class ShipCargo : NetworkBehaviour
{
    // Singleton - sahnede tek gemi (çoklu gemi gerekirse liste yapısına dönüştürülebilir)
    public static ShipCargo Instance { get; private set; }

    // --- Synced State ---
    public readonly SyncList<ItemData> CargoItems = new SyncList<ItemData>();
    public readonly SyncVar<float> TotalValue = new SyncVar<float>();
    public readonly SyncVar<float> TotalWeight = new SyncVar<float>();

    // --- Events (UI bildirimleri için) ---
    public event Action<ItemData> OnCargoAdded;
    public event Action OnCargoChanged;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Instance = this;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        CargoItems.OnChange += OnCargoListChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        CargoItems.OnChange -= OnCargoListChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnCargoListChanged(SyncListOperation op, int index,
        ItemData oldItem, ItemData newItem, bool asServer)
    {
        if (op == SyncListOperation.Add)
        {
            OnCargoAdded?.Invoke(newItem);
        }
        OnCargoChanged?.Invoke();
    }

    /// <summary>
    /// Sunucu tarafı: Eşyayı ortak kargoya ekle.
    /// ShipCollector tarafından toplama animasyonu bittikten sonra çağrılır.
    /// </summary>
    [Server]
    public void AddItemToCargo(GrabbableObject grabbable)
    {
        if (grabbable == null) return;

        ItemData data = new ItemData(grabbable);
        CargoItems.Add(data);
        TotalValue.Value += data.CurrentPrice;
        TotalWeight.Value += data.Weight;

        Debug.Log($"[ShipCargo] '{data.ItemName}' kargoya eklendi! Toplam Değer: {TotalValue.Value:F0} | Toplam Ağırlık: {TotalWeight.Value:F1} kg | Eşya Sayısı: {CargoItems.Count}");

        // Tüm clientlara bildirim
        ObserversNotifyCargoAdded(data);
    }

    [ObserversRpc]
    private void ObserversNotifyCargoAdded(ItemData data)
    {
        // UI güncellemesi veya ses efekti tetiklemek için
        // OnCargoAdded event'i SyncList callback'inden zaten tetikleniyor
    }

    /// <summary>
    /// Kargodaki tüm eşyaların toplam değerini hesapla.
    /// </summary>
    public float GetTotalValue()
    {
        float total = 0f;
        foreach (var item in CargoItems)
        {
            total += item.CurrentPrice;
        }
        return total;
    }
}
