using UnityEngine;

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
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
    }

    private void OnDestroy()
    {
        if (cameraModeManager != null)
        {
            cameraModeManager.OnCameraModeChanged -= HandleCameraModeChanged;
        }
    }

    private void HandleCameraModeChanged(CameraMode newMode)
    {
        // İleride mod değişikliğine bağlı UI güncellemeleri, 
        // silah sistemi değişiklikleri vb. buraya eklenebilir
        Debug.Log($"[GameManager] Kamera modu: {newMode}");
    }

    void Update()
    {
        
    }
}
