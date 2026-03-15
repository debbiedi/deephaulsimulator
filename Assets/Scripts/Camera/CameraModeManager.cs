using UnityEngine;
using System;
using FishNet.Object;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// TPS ve FPS kamera modları arasında geçişi yönetir.
/// Cinemachine Virtual Camera'ların priority değerlerini değiştirerek geçiş yapar.
/// FPSZoneTrigger ile entegre çalışarak belirli bölgelerde FPS modunu zorunlu kılar.
/// </summary>
public class CameraModeManager : NetworkBehaviour
{
    public static CameraModeManager Instance { get; private set; }

    [Header("Kamera Referansları")]
    [Tooltip("TPS (Üçüncü Şahıs) Cinemachine Virtual Camera")]
    public GameObject tpsVirtualCamera;

    [Tooltip("FPS (Birinci Şahıs) Cinemachine Virtual Camera")]
    public GameObject fpsVirtualCamera;

    [Header("Ayarlar")]
    [Tooltip("Başlangıç kamera modu")]
    public CameraMode startMode = CameraMode.TPS;

    [Tooltip("Geçiş tuşu (varsayılan: O)")]
    public KeyCode toggleKey = KeyCode.O;

    [Header("FPS Ayarları")]
    [Tooltip("FPS modunda karakterin mesh renderlarını gizle")]
    public bool hideCharacterInFPS = true;

    [Tooltip("Kameranın FPS'e giderken karakteri gizlemeden önce bekleyeceği SÜRE (Cinemachine Default Blend süreniz 2 saniye ise buraya 2 yazın)")]
    public float hideDelayInFPS = 0f;

    [Tooltip("FPS modunda gizlenecek karakter mesh'leri (SkinnedMeshRenderer veya MeshRenderer)")]
    public Renderer[] characterRenderers;

    [Header("Cinemachine Priority")]
    [Tooltip("Aktif kamera için priority değeri")]
    public int activePriority = 20;

    [Tooltip("Pasif kamera için priority değeri")]
    public int inactivePriority = 10;

    // Mevcut kamera modu
    public CameraMode CurrentMode { get; private set; }

    // Bölge tarafından zorlanmış mod
    private bool _isForcedMode = false;
    private CameraMode _forcedMode;

    // Mod değiştiğinde tetiklenen event
    public event Action<CameraMode> OnCameraModeChanged;

    // Cinemachine component referansları (runtime'da çözümlenir)
    private MonoBehaviour _tpsCinemachineComponent;
    private MonoBehaviour _fpsCinemachineComponent;

    private void Awake()
    {
        ResolveCinemachineComponents();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        if (base.IsOwner)
        {
            Instance = this;
        }
        else
        {
            // BAŞKA OYUNCU İSE ONUN KAMERALARINI KAPAT (Ekran çakışmasını engeller)
            if (tpsVirtualCamera != null) tpsVirtualCamera.SetActive(false);
            if (fpsVirtualCamera != null) fpsVirtualCamera.SetActive(false);
        }
    }

    private void Start()
    {
        // Başlangıç modunu ayarla
        SetCameraMode(startMode, forceEvent: true);
    }

    private void Update()
    {
        bool togglePressed = false;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // Yeni Input System için dinamik tuş kontrolü (Inspector'dan seçilen tuşu kullanır)
        if (Keyboard.current != null)
        {
            // Unity'nin KeyCode enum'ını Input System'in Key enum'ına çeviriyoruz
            Key inputSystemKey = MapKeyCodeToInputSystemKey(toggleKey);
            if (inputSystemKey != Key.None && Keyboard.current[inputSystemKey].wasPressedThisFrame)
            {
                togglePressed = true;
            }
        }
#else
        // Eski Input Manager (veya fallback) için kontrol
        if (Input.GetKeyDown(toggleKey))
        {
            togglePressed = true;
        }
#endif

        // Tuşa basıldığında mod değiştir (zorlanmış mod yoksa)
        if (togglePressed && !_isForcedMode)
        {
            ToggleCameraMode();
        }
    }

    /// <summary>
    /// Legacy KeyCode değerlerini yeni Input System Key enum'ına dönüştürür.
    /// Yaygın kullanılan tuşları ekledim, gerekirse çoğaltılabilir.
    /// </summary>
    private Key MapKeyCodeToInputSystemKey(KeyCode code)
    {
        return code switch
        {
            KeyCode.O => Key.O,
            KeyCode.P => Key.P,
            KeyCode.V => Key.V,
            KeyCode.B => Key.B,
            KeyCode.C => Key.C,
            KeyCode.T => Key.T,
            KeyCode.Tab => Key.Tab,
            KeyCode.CapsLock => Key.CapsLock,
            KeyCode.F1 => Key.F1,
            KeyCode.F2 => Key.F2,
            KeyCode.F3 => Key.F3,
            KeyCode.F4 => Key.F4,
            KeyCode.F5 => Key.F5,
            _ => Key.None
        };
    }

    /// <summary>
    /// Cinemachine component'lerini runtime'da bulur.
    /// Cinemachine 3.x (CinemachineCamera) veya 2.x (CinemachineVirtualCamera) destekler.
    /// </summary>
    private void ResolveCinemachineComponents()
    {
        if (tpsVirtualCamera != null)
        {
            _tpsCinemachineComponent = FindCinemachineComponent(tpsVirtualCamera);
            if (_tpsCinemachineComponent == null)
                Debug.LogError("[CameraModeManager] TPS Virtual Camera üzerinde Cinemachine component bulunamadı!", tpsVirtualCamera);
        }
        else
        {
            Debug.LogError("[CameraModeManager] TPS Virtual Camera atanmamış!");
        }

        if (fpsVirtualCamera != null)
        {
            _fpsCinemachineComponent = FindCinemachineComponent(fpsVirtualCamera);
            if (_fpsCinemachineComponent == null)
                Debug.LogError("[CameraModeManager] FPS Virtual Camera üzerinde Cinemachine component bulunamadı!", fpsVirtualCamera);
        }
        else
        {
            Debug.LogError("[CameraModeManager] FPS Virtual Camera atanmamış!");
        }
    }

    /// <summary>
    /// GameObject üzerindeki Cinemachine component'ini bulur.
    /// Cinemachine 3.x ve 2.x uyumlu.
    /// </summary>
    private MonoBehaviour FindCinemachineComponent(GameObject go)
    {
        // Cinemachine 3.x: Unity.Cinemachine.CinemachineCamera
        // Cinemachine 2.x: Cinemachine.CinemachineVirtualCamera
        foreach (var comp in go.GetComponents<MonoBehaviour>())
        {
            if (comp == null) continue;

            string typeName = comp.GetType().Name;
            if (typeName.Contains("CinemachineCamera") || typeName.Contains("CinemachineVirtualCamera"))
            {
                return comp;
            }
        }
        return null;
    }

    /// <summary>
    /// Cinemachine component'inin Priority değerini ayarlar.
    /// Reflection kullanarak hem CM2 hem CM3 ile uyumlu çalışır.
    /// </summary>
    private void SetCinemachinePriority(MonoBehaviour cinemachineComp, int priority)
    {
        if (cinemachineComp == null) return;

        var type = cinemachineComp.GetType();

        // Cinemachine 3.x: "Priority" property (struct wrapper)
        // Cinemachine 2.x: "Priority" property (int)
        var priorityProp = type.GetProperty("Priority");
        if (priorityProp != null)
        {
            // CM3 uses a struct, CM2 uses int
            if (priorityProp.PropertyType == typeof(int))
            {
                priorityProp.SetValue(cinemachineComp, priority);
            }
            else
            {
                // CM3: Priority is a struct with Value property
                var priorityObj = priorityProp.GetValue(cinemachineComp);
                var valueProp = priorityObj.GetType().GetProperty("Value") 
                             ?? priorityObj.GetType().GetField("Value")?.DeclaringType?.GetProperty("Value");
                
                if (valueProp != null)
                {
                    valueProp.SetValue(priorityObj, priority);
                    priorityProp.SetValue(cinemachineComp, priorityObj);
                }
                else
                {
                    // Fallback: m_Priority field
                    var field = type.GetField("m_Priority", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (field != null) field.SetValue(cinemachineComp, priority);
                }
            }
            return;
        }

        // Fallback: direct field
        var priorityField = type.GetField("m_Priority", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (priorityField != null)
        {
            priorityField.SetValue(cinemachineComp, priority);
        }
    }

    /// <summary>
    /// TPS ve FPS modları arasında geçiş yapar.
    /// </summary>
    public void ToggleCameraMode()
    {
        CameraMode newMode = CurrentMode == CameraMode.TPS ? CameraMode.FPS : CameraMode.TPS;
        SetCameraMode(newMode);
    }

    /// <summary>
    /// Belirtilen kamera moduna geçiş yapar.
    /// </summary>
    public void SetCameraMode(CameraMode mode, bool forceEvent = false)
    {
        if (CurrentMode == mode && !forceEvent) return;

        CurrentMode = mode;

        switch (mode)
        {
            case CameraMode.TPS:
                ActivateTPS();
                break;
            case CameraMode.FPS:
                ActivateFPS();
                break;
        }

        OnCameraModeChanged?.Invoke(mode);
        Debug.Log($"[CameraModeManager] Kamera modu değişti: {mode}");
    }

    private void ActivateTPS()
    {
        // Önceki geçiş işlemlerini (Coroutine) durdur ki çakışma olmasın
        StopAllCoroutines();

        // En güvenli yöntem: İki kamerayı da açık tutup SADECE Priority (Öncelik) değerlerini değiştirmek
        // Priority'si yüksek olan (örn: 20) kamerayı Cinemachine otomatik devralır.
        if (_tpsCinemachineComponent != null) SetCinemachinePriority(_tpsCinemachineComponent, activePriority);
        if (_fpsCinemachineComponent != null) SetCinemachinePriority(_fpsCinemachineComponent, inactivePriority);

        // Karakter mesh'lerini anında GÖSTER
        SetCharacterRenderersVisible(true);
    }

    private void ActivateFPS()
    {
        // Önceki geçiş işlemlerini durdur
        StopAllCoroutines();

        // FPS kamerasının önceliğini artır, TPS'i düşür
        if (_fpsCinemachineComponent != null) SetCinemachinePriority(_fpsCinemachineComponent, activePriority);
        if (_tpsCinemachineComponent != null) SetCinemachinePriority(_tpsCinemachineComponent, inactivePriority);

        // FPS modunda karakter mesh'lerini gizle (opsiyonel)
        if (hideCharacterInFPS)
        {
            if (hideDelayInFPS > 0f)
            {
                // Geçiş süresi (Blend) varsa gecikmeli gizle
                StartCoroutine(HideCharacterWithDelay(hideDelayInFPS));
            }
            else
            {
                // Anında gizle
                SetCharacterRenderersVisible(false);
            }
        }
    }

    private System.Collections.IEnumerator HideCharacterWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Sadece hala FPS modundaysak gizle (gecikme sırasında oyuncu tekrar TPS'e geçmiş olabilir)
        if (CurrentMode == CameraMode.FPS)
        {
            SetCharacterRenderersVisible(false);
        }
    }

    private void SetCharacterRenderersVisible(bool visible)
    {
        if (characterRenderers == null) return;

        foreach (var renderer in characterRenderers)
        {
            if (renderer != null)
            {
                // Multiplayer (Online) oyunlar için daha güvenli bir yöntem:
                // Modeli tamamen kapatmak yerine, sadece o oyuncunun kamerasından gizlenip
                // yere gölge düşürmesini (ShadowsOnly) sağlıyoruz.
                renderer.shadowCastingMode = visible 
                    ? UnityEngine.Rendering.ShadowCastingMode.On 
                    : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }
    }

    // ==================== Zorunlu Mod (FPS Zone) ====================

    /// <summary>
    /// Belirli bir bölgede FPS modunu zorlar. FPSZoneTrigger tarafından çağrılır.
    /// </summary>
    public void ForceMode(CameraMode mode)
    {
        _isForcedMode = true;
        _forcedMode = mode;
        SetCameraMode(mode);
        Debug.Log($"[CameraModeManager] Mod zorlandı: {mode}");
    }

    /// <summary>
    /// Zorunlu modu kaldırır ve önceki moda (TPS) döner.
    /// </summary>
    public void ReleaseForceMode()
    {
        _isForcedMode = false;
        // Zorlanmış moddan çıkınca varsayılan TPS'e dön
        SetCameraMode(CameraMode.TPS);
        Debug.Log("[CameraModeManager] Zorunlu mod kaldırıldı, TPS'e dönüldü.");
    }

    /// <summary>
    /// Şu an zorlanmış modda mı?
    /// </summary>
    public bool IsForcedMode => _isForcedMode;
}
