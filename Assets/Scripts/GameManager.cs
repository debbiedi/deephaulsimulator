using UnityEngine;
using System.Collections.Generic;
using System;
/// <summary>
/// Oyun genelini yöneten ana manager.
/// CameraModeManager ile entegre çalışarak kamera modu değişikliklerini dinler.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Referanslar")]
    [Tooltip("CameraModeManager referansı (sahnede otomatik bulunur)")]
    public CameraModeManager cameraModeManager;

    [Tooltip("PlayerMovementStateManager referansı (sahnede otomatik bulunur)")]
    public PlayerMovementStateManager movementStateManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    [Header("Rarity (Nadirlik) Veritabanı")]
    [Tooltip("Projedeki RarityDatabase objesini buraya sürükleyin. Outline (Glow) rengi bu veritabanındaki Glow Color HDR değerinden alınacak.")]
    public RarityDatabase rarityDatabase;

    public Color GetColorForRarity(RarityTier tier)
    {
        if (rarityDatabase != null)
        {
            RarityData data = rarityDatabase.GetData(tier);
            if (data != null)
                return data.glowColorHDR; // veya rarityColor dönebilirsiniz
        }
        return Color.yellow; // Ayarlanmamışsa veya veritabanı yoksa varsayılan sarı döner
    }

    void Start()
    {
        // CameraModeManager referansını bul
        if (cameraModeManager == null)
            cameraModeManager = FindFirstObjectByType<CameraModeManager>();

        // Kamera modu değişikliklerini dinle
        if (cameraModeManager != null)
        {
            cameraModeManager.OnCameraModeChanged += HandleCameraModeChanged;
        }

        // PlayerMovementStateManager referansını bul
        if (movementStateManager == null)
            movementStateManager = FindFirstObjectByType<PlayerMovementStateManager>();

        // Hareket state değişikliklerini dinle
        if (movementStateManager != null)
        {
            movementStateManager.OnMovementStateChanged += HandleMovementStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (cameraModeManager != null)
        {
            cameraModeManager.OnCameraModeChanged -= HandleCameraModeChanged;
        }

        if (movementStateManager != null)
        {
            movementStateManager.OnMovementStateChanged -= HandleMovementStateChanged;
        }
    }

    private void HandleCameraModeChanged(CameraMode newMode)
    {
        // İleride mod değişikliğine bağlı UI güncellemeleri, 
        // silah sistemi değişiklikleri vb. buraya eklenebilir
        Debug.Log($"[GameManager] Kamera modu: {newMode}");
    }

    private void HandleMovementStateChanged(PlayerMovementState newState)
    {
        // İleride state değişikliğine bağlı UI güncellemeleri,
        // oksijen sistemi, ses efektleri vb. buraya eklenebilir
        Debug.Log($"[GameManager] Hareket state: {newState}");
    }

    void Update()
    {
        
    }
}
