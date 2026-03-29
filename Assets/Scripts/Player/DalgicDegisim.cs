using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Object;
using FishNet.Object.Synchronizing;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Karakter değişim sistemi (FishNet NetworkBehaviour).
/// Belirli bir zone'a girince N tuşuna 1 saniye basılı tutarak
/// karakter modelini değiştirir. Toggle mantığıyla geri dönüş de mümkün.
/// </summary>
public class DalgicDegisim : NetworkBehaviour
{
    [Header("UI Referansları (Otomatik bulunur)")]
    public GameObject promptPanel;
    public Image progressFill;
    public TextMeshProUGUI promptText;

    [Header("Swap Ayarları")]
    public float holdDuration = 1f;

    [Header("Karakter Modelleri")]
    public GameObject normalModel;
    public GameObject diverModel;

    [Header("Animator Controllers & Avatars")]
    public RuntimeAnimatorController normalController;
    public Avatar normalAvatar;
    
    [Space(10)]
    public RuntimeAnimatorController diverController;
    public Avatar diverAvatar;

    // --- Network Sync ---
    private readonly SyncVar<bool> _syncIsDiver = new SyncVar<bool>();

    // --- Local State ---
    private bool _isInZone = false;
    private float _holdTimer = 0f;
    private CharacterSwapZone _currentZone;
    private Animator _animator;
    private bool _isDiver = false;

    public override void OnStartClient()
    {
        base.OnStartClient();

        _syncIsDiver.OnChange += OnDiverStateChanged;

        if (base.IsOwner)
        {
            FindUIElements();
        }

        if (_syncIsDiver.Value)
        {
            ApplyVisualSwap(true);
        }

        if (!base.IsOwner && promptPanel != null)
        {
            promptPanel.SetActive(false);
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _syncIsDiver.OnChange -= OnDiverStateChanged;
    }

    private void FindUIElements()
    {
        if (promptPanel == null)
        {
            Canvas[] allCanvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Debug.Log($"[DalgicDegisim] Sahnede {allCanvases.Length} Canvas bulundu.");

            foreach (Canvas canvas in allCanvases)
            {
                Transform[] allChildren = canvas.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child.name == "PromptPanel")
                    {
                        promptPanel = child.gameObject;
                        Debug.Log($"[DalgicDegisim] PromptPanel bulundu! Canvas: {canvas.gameObject.name}");
                        break;
                    }
                }
                if (promptPanel != null) break;
            }
        }

        if (promptPanel != null)
        {
            if (progressFill == null)
            {
                Transform fillTransform = promptPanel.transform.Find("ProgressFill");
                if (fillTransform != null)
                    progressFill = fillTransform.GetComponent<Image>();
            }

            if (promptText == null)
            {
                Transform textTransform = promptPanel.transform.Find("PromptText");
                if (textTransform != null)
                    promptText = textTransform.GetComponent<TextMeshProUGUI>();
            }

            promptPanel.SetActive(false);
            Debug.Log($"[DalgicDegisim] UI bulundu! ProgressFill={progressFill != null}, PromptText={promptText != null}");
        }
        else
        {
            Debug.LogWarning("[DalgicDegisim] PromptPanel hiçbir Canvas altında bulunamadı!");
        }
    }

    private void Update()
    {
        if (!base.IsOwner) return;

        if (!_isInZone)
        {
            HidePrompt();
            return;
        }

        ShowPrompt();

        // N tuşu kontrolü
#if ENABLE_INPUT_SYSTEM
        bool nPressed = Keyboard.current != null && Keyboard.current.nKey.isPressed;
#else
        bool nPressed = Input.GetKey(KeyCode.N);
#endif

        if (nPressed)
        {
            _holdTimer += Time.deltaTime;
            UpdateProgressBar(_holdTimer / holdDuration);
            Debug.Log($"[DalgicDegisim] N basılı! Timer: {_holdTimer:F2}/{holdDuration}");

            if (_holdTimer >= holdDuration)
            {
                Debug.Log($"[DalgicDegisim] SWAP! isDiver: {_isDiver} -> {!_isDiver}");
                ServerSwapCharacter(!_isDiver);
                _holdTimer = 0f;
                UpdateProgressBar(0f);
            }
        }
        else
        {
            if (_holdTimer > 0f)
            {
                _holdTimer = 0f;
                UpdateProgressBar(0f);
            }
        }
    }

    // ==================== Zone ====================

    public void EnterZone(CharacterSwapZone zone)
    {
        Debug.Log($"[DalgicDegisim] EnterZone! IsOwner={base.IsOwner}");
        if (!base.IsOwner) return;

        _isInZone = true;
        _currentZone = zone;
        _holdTimer = 0f;
    }

    public void ExitZone()
    {
        if (!base.IsOwner) return;

        _isInZone = false;
        _currentZone = null;
        _holdTimer = 0f;
        HidePrompt();
    }

    // ==================== Network ====================

    [ServerRpc]
    private void ServerSwapCharacter(bool toDiver)
    {
        _syncIsDiver.Value = toDiver;
    }

    private void OnDiverStateChanged(bool prev, bool next, bool asServer)
    {
        ApplyVisualSwap(next);
    }

    // ==================== Visual ====================

    private void ApplyVisualSwap(bool isDiver)
    {
        _isDiver = isDiver;

        if (normalModel != null)
            normalModel.SetActive(!isDiver);

        if (diverModel != null)
            diverModel.SetActive(isDiver);

        _animator = GetComponent<Animator>();
        if (_animator != null)
        {
            // Avatar değiştir (Kemiklerin doğru çalışması için mecburi)
            Avatar targetAvatar = isDiver ? diverAvatar : normalAvatar;
            if (targetAvatar != null)
                _animator.avatar = targetAvatar;

            // Controller değiştir
            RuntimeAnimatorController target = isDiver ? diverController : normalController;
            if (target != null)
                _animator.runtimeAnimatorController = target;
        }
    }

    // ==================== UI ====================

    private void ShowPrompt()
    {
        if (promptPanel == null)
            FindUIElements();

        if (promptPanel != null && !promptPanel.activeSelf)
        {
            promptPanel.SetActive(true);
            Debug.Log("[DalgicDegisim] PromptPanel AÇILDI!");
        }

        if (promptText != null && _currentZone != null)
        {
            promptText.text = _isDiver
                ? $"[N] {_currentZone.revertText}"
                : $"[N] {_currentZone.interactionText}";
        }
    }

    private void HidePrompt()
    {
        if (promptPanel != null && promptPanel.activeSelf)
            promptPanel.SetActive(false);

        if (_holdTimer > 0f)
        {
            _holdTimer = 0f;
            UpdateProgressBar(0f);
        }
    }

    private void UpdateProgressBar(float progress)
    {
        if (progressFill != null)
            progressFill.fillAmount = Mathf.Clamp01(progress);
    }
}
