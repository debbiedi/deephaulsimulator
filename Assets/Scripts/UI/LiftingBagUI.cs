using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Object;

/// <summary>
/// Kaldırma balonu HUD'u. Player Canvas'ına eklenir.
/// - Pompalama sırasında progress bar gösterir
/// - Kalan balon sayısı
/// - Aktif pompalayıcı sayısı (co-op)
/// - İpucu metinleri ("[F] Balon Tak", "[E] Pompala" vb.)
///
/// Kurulum:
/// 1. Player Canvas'a bir UI paneli ekleyin (LiftingBagPanel).
/// 2. İçine: ProgressBar (Slider), BagCountText, PumperCountText, HintText
/// 3. Bu scripti panele ekleyin ve referansları atayın.
/// </summary>
public class LiftingBagUI : MonoBehaviour
{
    [Header("Referanslar - Player")]
    [Tooltip("Aynı player üzerindeki LiftingBagAttacher")]
    public LiftingBagAttacher attacher;

    [Tooltip("Aynı player üzerindeki InflationMiniGame")]
    public InflationMiniGame miniGame;

    [Header("UI Elementleri")]
    [Tooltip("Şişirme progress bar (Slider)")]
    public Slider progressBar;

    [Tooltip("Kalan balon sayısı metni")]
    public TextMeshProUGUI bagCountText;

    [Tooltip("Aktif pompalayıcı sayısı metni (co-op)")]
    public TextMeshProUGUI pumperCountText;

    [Tooltip("İpucu metni ([F] Balon Tak, [E] Pompala vb.)")]
    public TextMeshProUGUI hintText;

    [Header("Panel")]
    [Tooltip("Mini-game paneli (pompalama sırasında gösterilir)")]
    public GameObject miniGamePanel;

    [Tooltip("Balon sayısı paneli (her zaman gösterilir)")]
    public GameObject bagCountPanel;

    private LiftingBag _currentBag;

    private void Start()
    {
        if (miniGame != null)
        {
            miniGame.OnMiniGameStarted += OnMiniGameStarted;
            miniGame.OnMiniGameEnded += OnMiniGameEnded;
            miniGame.OnPumped += OnPumped;
        }

        // Başlangıçta mini-game paneli gizli
        if (miniGamePanel != null)
            miniGamePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (miniGame != null)
        {
            miniGame.OnMiniGameStarted -= OnMiniGameStarted;
            miniGame.OnMiniGameEnded -= OnMiniGameEnded;
            miniGame.OnPumped -= OnPumped;
        }
    }

    void Update()
    {
        UpdateBagCount();
        UpdateMiniGameUI();
    }

    private void UpdateBagCount()
    {
        if (attacher == null || bagCountText == null) return;
        bagCountText.text = $"{attacher.BagCount.Value}";
    }

    private void UpdateMiniGameUI()
    {
        if (_currentBag == null) return;

        // Progress bar güncelle
        if (progressBar != null)
        {
            progressBar.value = _currentBag.InflationLevel.Value;
        }

        // Aktif pompalayıcı sayısı
        if (pumperCountText != null)
        {
            int count = _currentBag.ActivePumperCount.Value;
            pumperCountText.text = count > 1 ? $"{count} Kisi Pompaliyor" : "";
            pumperCountText.gameObject.SetActive(count > 1);
        }
    }

    private void OnMiniGameStarted()
    {
        _currentBag = miniGame.CurrentBag;

        if (miniGamePanel != null)
            miniGamePanel.SetActive(true);

        if (hintText != null)
            hintText.text = "[E] Pompala | [ESC] Iptal";

        if (progressBar != null)
            progressBar.value = 0f;
    }

    private void OnMiniGameEnded()
    {
        _currentBag = null;

        if (miniGamePanel != null)
            miniGamePanel.SetActive(false);
    }

    private void OnPumped()
    {
        // Pompa basıldığında kısa görsel geri bildirim (isteğe bağlı)
        // Örn: progress bar'ı kısa süreliğine parlat
    }
}
