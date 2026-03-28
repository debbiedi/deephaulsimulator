using UnityEngine;
using UnityEngine.UI;

public class CrosshairManager : MonoBehaviour
{
    public static CrosshairManager Instance { get; private set; }

    [Header("UI Referansları")]
    [Tooltip("Crosshair'in gösterileceği Image bileşeni")]
    public Image crosshairImage;

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

    private void Start()
    {
        // Eğer CameraModeManager varsa, kamera değişimi eventini dinlemeye başla
        if (CameraModeManager.Instance != null)
        {
            CameraModeManager.Instance.OnCameraModeChanged += HandleCameraModeChanged;

            // Oyun başladığında mevcut kameraya göre görünürlüğü ayarla
            HandleCameraModeChanged(CameraModeManager.Instance.CurrentMode);
        }
        else
        {
            SetCrosshairVisible(false);
        }
    }

    private void OnDestroy()
    {
        // Script kapanınca eventten çıkış yap (Hata vermemesi için)
        if (CameraModeManager.Instance != null)
        {
            CameraModeManager.Instance.OnCameraModeChanged -= HandleCameraModeChanged;
        }
    }

    private void HandleCameraModeChanged(CameraMode mode)
    {
        // Sadece FPS modundaysa crosshair'i göster
        if (mode == CameraMode.FPS)
        {
            SetCrosshairVisible(true);
        }
        else
        {
            SetCrosshairVisible(false);
        }
    }

    private void SetCrosshairVisible(bool isVisible)
    {
        if (crosshairImage != null)
        {
            crosshairImage.enabled = isVisible;
            
            // Eğer açıldıysa standart sprite'ı göster
            if (isVisible) SetDefaultCrosshair();
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
    }
}