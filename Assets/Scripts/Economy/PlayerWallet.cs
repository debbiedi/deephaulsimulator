using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using System;

/// <summary>
/// Oyuncunun kişisel cüzdanı. Player prefabına eklenir.
/// SyncVar ile bakiye tüm istemcilere senkronize edilir.
/// Hızlı satış ve diğer para kazanma mekanikleri buraya para ekler.
/// </summary>
public class PlayerWallet : NetworkBehaviour
{
    // --- Synced State ---
    public readonly SyncVar<float> PersonalBalance = new SyncVar<float>();

    // --- Events (UI bildirimleri için) ---
    /// <summary>
    /// Para değiştiğinde tetiklenir. (eklenen/çıkarılan miktar, yeni bakiye)
    /// </summary>
    public event Action<float, float> OnMoneyChanged;

    /// <summary>
    /// Hızlı satış yapıldığında tetiklenir. (satış fiyatı, eşya adı)
    /// </summary>
    public event Action<float, string> OnQuickSellCompleted;

    /// <summary>
    /// Sunucu tarafı: Cüzdana para ekle.
    /// </summary>
    [Server]
    public void AddMoney(float amount)
    {
        if (amount <= 0f) return;

        PersonalBalance.Value += amount;
        Debug.Log($"[PlayerWallet] +{amount:F0}₺ eklendi. Bakiye: {PersonalBalance.Value:F0}₺");

        // Sadece cüzdan sahibine bildirim gönder
        TargetNotifyMoneyChanged(base.Owner, amount, PersonalBalance.Value);
    }

    /// <summary>
    /// Sunucu tarafı: Cüzdandan para çıkar.
    /// </summary>
    [Server]
    public bool RemoveMoney(float amount)
    {
        if (amount <= 0f) return false;
        if (PersonalBalance.Value < amount) return false;

        PersonalBalance.Value -= amount;
        Debug.Log($"[PlayerWallet] -{amount:F0}₺ çıkarıldı. Bakiye: {PersonalBalance.Value:F0}₺");

        TargetNotifyMoneyChanged(base.Owner, -amount, PersonalBalance.Value);
        return true;
    }

    /// <summary>
    /// Yeterli para var mı kontrol et.
    /// </summary>
    public bool CanAfford(float amount)
    {
        return PersonalBalance.Value >= amount;
    }

    /// <summary>
    /// Server -> Owner Client: Para değişikliği bildirimi.
    /// </summary>
    [TargetRpc]
    private void TargetNotifyMoneyChanged(NetworkConnection conn, float amount, float newBalance)
    {
        OnMoneyChanged?.Invoke(amount, newBalance);
    }

    /// <summary>
    /// Server -> Owner Client: Hızlı satış bildirimi.
    /// </summary>
    [TargetRpc]
    public void TargetNotifyQuickSell(NetworkConnection conn, float sellPrice, string itemName)
    {
        OnQuickSellCompleted?.Invoke(sellPrice, itemName);
    }
}
