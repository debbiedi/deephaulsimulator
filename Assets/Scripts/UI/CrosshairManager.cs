using UnityEngine;
using UnityEngine.UI;

public class CrosshairManager : MonoBehaviour
{
    public static CrosshairManager Instance { get; private set; }

    [Header("UI Referansları")]
    [Tooltip("FPS Crosshair'in gösterileceği Image bileşeni")]
    public Image crosshairImage;
    [Tooltip("TPS Crosshair'in gösterileceği Image bileşeni (Sağ omuz hizası vb.)")]
    public Image tpsCrosshairImage;

    [Header("Crosshair Görselleri (Sprites)")]
    [Tooltip("Normal durumdaki crosshair görseli (Nokta vs.)")]
    public Sprite defaultCrosshair;
    [Tooltip("Etkileşime girilebilir (kutu vs) bir objeye bakarkenki crosshair görseli (El ikonu, açık kutu vb.)")]
    public Sprite interactableCrosshair;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private CameraModeManager _currentCameraManager;

    private void Update()
    {
        // CameraModeManager instance'ına dinamik olarak bağlan
        if (CameraModeManager.Instance != null && _currentCameraManager != CameraModeManager.Instance)
        {
            if (_currentCameraManager != null)
                _currentCameraManager.OnCameraModeChanged -= HandleCameraModeChanged;

            _currentCameraManager = CameraModeManager.Instance;
            _currentCameraManager.OnCameraModeChanged += HandleCameraModeChanged;

            // Yeni kameraya bağlandığında anında durumunu güncelle
            HandleCameraModeChanged(_currentCameraManager.CurrentMode);
        }
        else if (CameraModeManager.Instance == null && _currentCameraManager != null)
        {
            // Karakter silinirse crosshair'i kapat
            _currentCameraManager = null;
            SetCrosshairState(false, false);
        }
    }

    private void OnDestroy()
    {
        if (_currentCameraManager != null)
        {
            _currentCameraManager.OnCameraModeChanged -= HandleCameraModeChanged;
        }
    }

    private void HandleCameraModeChanged(CameraMode mode)
    {
        if (mode == CameraMode.FPS)
        {
            SetCrosshairState(true, false);
        }
        else if (mode == CameraMode.TPS)
        {
            SetCrosshairState(false, true);
        }
        else
        {
            SetCrosshairState(false, false);
        }
    }

    private void SetCrosshairState(bool fpsVisible, bool tpsVisible)
    {
        if (crosshairImage != null)
        {
            crosshairImage.enabled = fpsVisible;
        }

        if (tpsCrosshairImage != null)
        {
            tpsCrosshairImage.enabled = tpsVisible;
        }
        
        // Eğer herhangi biri açıldıysa standart sprite'ı göster
        if (fpsVisible || tpsVisible)
        {
            SetDefaultCrosshair();
        }
    }

    /// <summary>
    /// Crosshair görüntüsünü normal haline çevirir.
    /// Kutuya bakmayı bıraktığınızda bunu çağırabilirsiniz.
    /// </summary>
    public void SetDefaultCrosshair()
    {
        if (crosshairImage != null && defaultCrosshair != null)
        {
            crosshairImage.sprite = defaultCrosshair;
            crosshairImage.color = Color.white; // Rengini sıfırla
        }

        if (tpsCrosshairImage != null && defaultCrosshair != null)
        {
            tpsCrosshairImage.sprite = defaultCrosshair;
            tpsCrosshairImage.color = Color.white; // Rengini sıfırla
        }
    }

    /// <summary>
    /// Kutu gibi alınabilir bir objeye bakıldığında crosshair ikonunu değiştirir.
    /// Raycast (Etkileşim) kodunuzdan kutuyu gördüğünüz an bu fonksiyonu çağırabilirsiniz.
    /// </summary>
    public void SetInteractableCrosshair()
    {
        if (crosshairImage != null && interactableCrosshair != null)
        {
            crosshairImage.sprite = interactableCrosshair;
            crosshairImage.color = Color.green; // İstediğiniz gibi opsiyonel bir renk ekleyebilirsiniz
        }

        if (tpsCrosshairImage != null && interactableCrosshair != null)
        {
            tpsCrosshairImage.sprite = interactableCrosshair;
            tpsCrosshairImage.color = Color.green; // İstediğiniz gibi opsiyonel bir renk ekleyebilirsiniz
        }
    }

    /// <summary>
    /// Aktif olan crosshair'in ekran koordinatlarını (Screen Space) döndürür.
    /// Raycast işlemleri (ScreenPointToRay) için kullanılır.
    /// </summary>
    public Vector3 GetActiveCrosshairPosition()
    {
        if (tpsCrosshairImage != null && tpsCrosshairImage.enabled)
        {
            return tpsCrosshairImage.transform.position;
        }
        if (crosshairImage != null && crosshairImage.enabled)
        {
            return crosshairImage.transform.position;
        }
        
        // Eğer hiçbir crosshair aktif değilse, ekranın ortasını döndür
        return new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
    }
}