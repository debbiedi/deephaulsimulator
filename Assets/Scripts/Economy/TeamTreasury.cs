using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;

/// <summary>
/// Takımın ortak kasası. Gemi objesine eklenir (ShipCargo gibi Singleton).
/// Tüm oyuncuların kazançları burada da takip edilir.
/// SyncVar ile bakiye tüm istemcilere senkronize edilir.
/// </summary>
public class TeamTreasury : NetworkBehaviour
{
    // Singleton - sahnede tek gemi
    public static TeamTreasury Instance { get; private set; }

    // --- Synced State ---
    public readonly SyncVar<float> SharedBalance = new SyncVar<float>();

    // --- Events (UI bildirimleri için) ---
    public event Action<float> OnTreasuryChanged;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Sunucu tarafı: Ortak kasaya para ekle.
    /// </summary>
    [Server]
    public void AddToTreasury(float amount)
    {
        if (amount <= 0f) return;

        SharedBalance.Value += amount;
        Debug.Log($"[TeamTreasury] +{amount:F0}₺ kasaya eklendi. Toplam: {SharedBalance.Value:F0}₺");

        // Tüm oyunculara bildir
        ObserversNotifyTreasuryChanged(SharedBalance.Value);
    }

    /// <summary>
    /// Sunucu tarafı: Ortak kasadan para çıkar.
    /// </summary>
    [Server]
    public bool RemoveFromTreasury(float amount)
    {
        if (amount <= 0f) return false;
        if (SharedBalance.Value < amount) return false;

        SharedBalance.Value -= amount;
        Debug.Log($"[TeamTreasury] -{amount:F0}₺ kasadan çıkarıldı. Toplam: {SharedBalance.Value:F0}₺");

        ObserversNotifyTreasuryChanged(SharedBalance.Value);
        return true;
    }

    [ObserversRpc]
    private void ObserversNotifyTreasuryChanged(float newBalance)
    {
        OnTreasuryChanged?.Invoke(newBalance);
    }
}
