using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using System.Collections.Generic;

/// <summary>
/// Drone'un esyalari teslim ettigi nokta (dukkan/depo).
/// Sahnede bir veya birden fazla istasyon olabilir.
///
/// Kurulum:
/// 1. Sahneye bir GameObject ekleyin (DroneDeliveryStation).
/// 2. Bu scripti ekleyin (NetworkObject gerekli).
/// 3. deliveryPoint Transform'unu drone'un varacagi noktaya ayarlayin.
/// 4. CarrierDrone inspector'undan bu istasyonu referans alin.
/// </summary>
public class DroneStation : NetworkBehaviour
{
    // --- STATIC REGISTRY ---
    public static readonly List<DroneStation> ActiveStations = new List<DroneStation>();

    [Header("Referanslar")]
    [Tooltip("Drone'un varacagi pozisyon (bos Transform)")]
    public Transform deliveryPoint;

    [Header("Ayarlar")]
    [Tooltip("Istasyon adi (UI icin)")]
    public string stationName = "Dukkan";

    [Tooltip("Satis fiyat carpani (1.0 = normal fiyat, 0.8 = %20 indirim)")]
    [Range(0.5f, 2.0f)]
    public float priceMultiplier = 1.0f;

    // --- SYNCED STATE ---
    public readonly SyncVar<float> TotalDeliveredValue = new SyncVar<float>();
    public readonly SyncVar<int> TotalDeliveredCount = new SyncVar<int>();

    // --- EVENTS ---
    public event Action<float> OnDeliveryReceived;

    private void OnEnable()
    {
        if (!ActiveStations.Contains(this))
            ActiveStations.Add(this);
    }

    private void OnDisable()
    {
        ActiveStations.Remove(this);
    }

    /// <summary>
    /// Sunucu: Drone teslimat yaptiginda cagrilir.
    /// Esyalari alir, istatistikleri gunceller.
    /// </summary>
    [Server]
    public void ServerReceiveDelivery(int itemCount, float totalValue)
    {
        float adjustedValue = totalValue * priceMultiplier;

        TotalDeliveredValue.Value += adjustedValue;
        TotalDeliveredCount.Value += itemCount;

        Debug.Log($"[DroneStation] '{stationName}' teslimat aldi! " +
                  $"{itemCount} esya, {adjustedValue:F0}TL deger.");

        ObserversNotifyDelivery(adjustedValue);
    }

    [ObserversRpc]
    private void ObserversNotifyDelivery(float value)
    {
        OnDeliveryReceived?.Invoke(value);
    }

    /// <summary>
    /// En yakin istasyonu bul.
    /// </summary>
    public static DroneStation FindNearest(Vector3 position)
    {
        DroneStation nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < ActiveStations.Count; i++)
        {
            if (ActiveStations[i] == null) continue;
            float dist = Vector3.Distance(ActiveStations[i].transform.position, position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = ActiveStations[i];
            }
        }

        return nearest;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        if (deliveryPoint != null)
        {
            Gizmos.DrawWireSphere(deliveryPoint.position, 2f);
            Gizmos.DrawLine(transform.position, deliveryPoint.position);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 2f);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f,
            $"[DroneStation] {stationName}");
#endif
    }
}
