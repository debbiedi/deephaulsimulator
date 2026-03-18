using UnityEngine;
using System;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using FishNet.Object;

/// <summary>
/// Karakter hareket state machine'inin merkezi yöneticisi.
/// Walking ↔ Swimming ↔ UnderwaterWalking geçişlerini yönetir.
/// ThirdPersonController ile entegre çalışarak hareket parametrelerini state'e göre ayarlar.
/// 
/// Kullanım:
/// 1. Player GameObject'ine bu scripti ekleyin.
/// 2. Inspector'da ThirdPersonController referansını atayın.
/// </summary>
public class PlayerMovementStateManager : NetworkBehaviour
{
    public static PlayerMovementStateManager Instance { get; private set; }

    [Header("Referanslar")]
    [Tooltip("ThirdPersonController referansı (otomatik bulunur)")]
    public ThirdPersonController controller;

    [Tooltip("CharacterController referansı (otomatik bulunur)")]
    public CharacterController characterController;

    [Header("Yüzme Hız Ayarları")]
    [Tooltip("Yüzme hızı (m/s)")]
    public float swimSpeed = 2.5f;

    [Tooltip("Yüzmede sprint hızı (m/s)")]
    public float swimSprintSpeed = 4f;

    [Tooltip("Dikey hareket hızı (yukarı/aşağı yüzme)")]
    public float verticalSwimSpeed = 3.5f;

    [Tooltip("Su altı yürüme hızı (m/s)")]
    public float underwaterWalkSpeed = 1.2f;

    [Tooltip("Su altı yürüme sprint hızı (m/s)")]
    public float underwaterWalkSprintSpeed = 2f;

    [Header("Su Altı Yürüme → Yüzme Geçişi")]
    [Tooltip("Su altında yürürken Space tuşuyla yüzmeye geçer")]
    public bool spaceToSwimFromWalk = true;

    [Header("Yüzme Tuşları")]
    [Tooltip("Yukarı yüzme tuşu")]
    public Key ascendKey = Key.Space;
    [Tooltip("Aşağı dalma tuşu")]
    public Key descendKey = Key.LeftCtrl;

    [Header("State Geçiş Ayarları")]
    [Tooltip("State değişiklikleri arasındaki minimum bekleme süresi (saniye)")]
    public float stateChangeCooldown = 0.5f;

    [Tooltip("UnderwaterWalking'e geçmek için zemine kaç saniye temas etmeli")]
    public float groundedRequiredDuration = 0.3f;

    // Mevcut state
    public PlayerMovementState CurrentState { get; private set; } = PlayerMovementState.Walking;

    // Aktif su bölgesi
    public WaterZone CurrentWaterZone { get; private set; }

    // Su içinde mi?
    public bool IsInWater { get; private set; }

    // State değiştiğinde tetiklenen event
    public event Action<PlayerMovementState> OnMovementStateChanged;

    // Orijinal controller değerleri (geri dönmek için)
    private float _originalMoveSpeed;
    private float _originalSprintSpeed;
    private float _originalGravity;
    private float _originalJumpHeight;

    // Dikey hareket hızı (yüzerken yukarı/aşağı)
    private float _swimVerticalVelocity;

    // State geçiş cooldown
    private float _lastStateChangeTime = -10f;

    // Grounded süre takibi (ping-pong önleme)
    private float _groundedTimer = 0f;

    // Su altı zemin raycast mesafesi
    private const float UNDERWATER_GROUND_CHECK_DISTANCE = 0.5f;

    private void Awake()
    {
        // Otomatik referans bulma
        if (controller == null)
            controller = GetComponent<ThirdPersonController>();
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.IsOwner)
        {
            // OYUNCU BANA AİTSE (Local)
            Instance = this;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            // BAŞKA OYUNCUYSA (Kamera ve kontrolleri bende çalışmasın)
            if (controller != null) controller.enabled = false;

#if ENABLE_INPUT_SYSTEM
            var playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null) playerInput.enabled = false;
#endif

            var starterInputs = GetComponent<StarterAssetsInputs>();
            if (starterInputs != null) starterInputs.enabled = false;

            // Karakterin içindeki kamerayı ve AudioListener'ı kapat
            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in cameras)
            {
                cam.gameObject.SetActive(false);
            }

            AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
            foreach (var listener in listeners)
            {
                listener.enabled = false;
            }
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (base.IsOwner)
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Sahne1 yüklendiğinde ve karakter benimse, onu SpawnPoint objesine taşı
        if (scene.name == "Sahne1")
        {
            GameObject spawnPoint = GameObject.Find("SpawnPoint");
            if (spawnPoint == null) spawnPoint = GameObject.Find("SpawnPoint "); // Boşluklu ihtimali de dene
            
            if (spawnPoint != null)
            {
                // Karakteri taşımak için CharacterController geçici olarak kapatılmalı ve biraz beklenmeli 
                // (FishNet'in ve CharacterController'ın kendisini konumlandırma süresini bekliyoruz)
                StartCoroutine(TeleportToSpawnPoint(spawnPoint.transform));
            }
            else
            {
                Debug.LogWarning("[PlayerMovementStateManager] Sahnede 'SpawnPoint' adında bir obje bulunamadı!");
            }
        }
    }

    private System.Collections.IEnumerator TeleportToSpawnPoint(Transform spawnTransform)
    {
        // Unity ve Fishnet fizik motorunun tam olarak sahneyi yüklemesini bekle
        yield return new WaitForEndOfFrame();
        
        if (characterController != null) characterController.enabled = false;
        
        // Konumu ayarla
        transform.position = spawnTransform.position;
        transform.rotation = spawnTransform.rotation;
        
        // Kamerayı (varsa FirstPersonCamera vb.) doğrudan aynı hizaya çevir
        if (controller != null && Camera.main != null)
        {
            // Cinemachine/StarterAssets için karakterin dönüşünü zorla
            Vector2 targetRotation = new Vector2(spawnTransform.eulerAngles.y, spawnTransform.eulerAngles.x);
            // controller üzerindeki yönü sıfırla/eşitlemek için gerekli değerleri güncelleyebilirsiniz
            // Şimdilik sadece transform güncellendi.
        }

        yield return new WaitForEndOfFrame();
        
        if (characterController != null) characterController.enabled = true;
        
        Debug.Log("[PlayerMovementStateManager] Karakter başarıyla SpawnPoint'e ışınlandı.");
    }

    private void Start()
    {
        if (controller == null)
        {
            Debug.LogError("[PlayerMovementStateManager] ThirdPersonController bulunamadı! Player objesine eklendiğinden emin olun.");
            return;
        }

        // Orijinal değerleri kaydet
        _originalMoveSpeed = controller.MoveSpeed;
        _originalSprintSpeed = controller.SprintSpeed;
        _originalGravity = controller.Gravity;
        _originalJumpHeight = controller.JumpHeight;
    }

    private void Update()
    {
        if (!IsInWater || CurrentWaterZone == null) return;

        // Zemin kontrolü: Hem CharacterController Grounded hem de Raycast kullan
        bool isGrounded = CheckUnderwaterGrounded();
        if (isGrounded)
            _groundedTimer += Time.deltaTime;
        else
            _groundedTimer = 0f;

        // Su içindeyken state geçişlerini kontrol et
        UpdateWaterState(isGrounded);

        // Yüzme modunda dikey hareket (Space/Ctrl)
        if (CurrentState == PlayerMovementState.Swimming)
        {
            HandleSwimVerticalMovement();
        }

        // Su altı yürümede Space ile yüzmeye geçiş
        if (CurrentState == PlayerMovementState.UnderwaterWalking && spaceToSwimFromWalk)
        {
            if (GetAscendInput())
            {
                SetState(PlayerMovementState.Swimming);
            }
        }
    }

    /// <summary>
    /// Su altında zemin tespiti. CharacterController.Grounded + Raycast ile daha güvenilir.
    /// </summary>
    private bool CheckUnderwaterGrounded()
    {
        // Önce CharacterController'ın kendi grounded check'i
        if (controller.Grounded) return true;

        // Ek raycast kontrolü (CharacterController bazen algılayamaz)
        if (characterController != null)
        {
            float skinWidth = characterController.skinWidth;
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            bool rayHit = Physics.Raycast(rayOrigin, Vector3.down, 
                UNDERWATER_GROUND_CHECK_DISTANCE + 0.1f, 
                controller.GroundLayers, QueryTriggerInteraction.Ignore);
            return rayHit;
        }

        return false;
    }

    // ==================== Su Bölgesi Giriş/Çıkış ====================

    /// <summary>
    /// Su bölgesine giriş. WaterZone tarafından çağrılır.
    /// </summary>
    public void EnterWater(WaterZone waterZone)
    {
        IsInWater = true;
        CurrentWaterZone = waterZone;
        _groundedTimer = 0f;
        _swimVerticalVelocity = 0f;

        // Suya girerken dikey hızı ve zıplama animasyonlarını anında sıfırla
        if (controller != null)
        {
            controller.Gravity = 0f;
            controller.ResetVerticalVelocity(); // Zıplama hızı + Jump/FreeFall animasyonları sıfırlanır
        }

        SetState(PlayerMovementState.Swimming, forceChange: true);
        Debug.Log("[PlayerMovementStateManager] Suya girildi → Swimming");
    }

    /// <summary>
    /// Su bölgesinden çıkış. WaterZone tarafından çağrılır.
    /// </summary>
    public void ExitWater()
    {
        IsInWater = false;
        CurrentWaterZone = null;
        _groundedTimer = 0f;
        SetState(PlayerMovementState.Walking, forceChange: true);
        Debug.Log("[PlayerMovementStateManager] Sudan çıkıldı → Walking");
    }

    // ==================== State Yönetimi ====================

    /// <summary>
    /// State değişikliğini uygular ve ilgili parametreleri ayarlar.
    /// forceChange: Cooldown'u atlayarak anında geçiş yapar (su giriş/çıkış için).
    /// </summary>
    public void SetState(PlayerMovementState newState, bool forceChange = false)
    {
        if (CurrentState == newState) return;

        // Cooldown kontrolü (zorlanmış değişiklikler hariç)
        if (!forceChange && Time.time - _lastStateChangeTime < stateChangeCooldown)
            return;

        PlayerMovementState oldState = CurrentState;
        CurrentState = newState;
        _lastStateChangeTime = Time.time;

        // State'e göre controller parametrelerini güncelle
        ApplyStateParameters(newState);

        OnMovementStateChanged?.Invoke(newState);
        Debug.Log($"[PlayerMovementStateManager] State değişti: {oldState} → {newState}");
    }

    /// <summary>
    /// Her state için ThirdPersonController parametrelerini ayarlar.
    /// </summary>
    private void ApplyStateParameters(PlayerMovementState state)
    {
        if (controller == null) return;

        switch (state)
        {
            case PlayerMovementState.Walking:
                controller.MoveSpeed = _originalMoveSpeed;
                controller.SprintSpeed = _originalSprintSpeed;
                controller.Gravity = _originalGravity;
                controller.JumpHeight = _originalJumpHeight;
                _swimVerticalVelocity = 0f;
                break;

            case PlayerMovementState.Swimming:
                controller.MoveSpeed = swimSpeed;
                controller.SprintSpeed = swimSprintSpeed;
                // Nötr yüzerlik: Yerçekimi 0 - karakter ne batar ne çıkar
                // Aşağı/yukarı hareket sadece Space/Ctrl ile olur
                controller.Gravity = 0f;
                controller.JumpHeight = 0f; // Suda zıplama yok
                break;

            case PlayerMovementState.UnderwaterWalking:
                controller.MoveSpeed = underwaterWalkSpeed;
                controller.SprintSpeed = underwaterWalkSprintSpeed;
                // Su altı yürürken yerçekimi Walking ile aynı (zemine yapışsın)
                controller.Gravity = CurrentWaterZone != null ? CurrentWaterZone.underwaterWalkGravity : -5f;
                controller.JumpHeight = 0f; // Su altında zıplama yok
                _swimVerticalVelocity = 0f;
                break;
        }
    }

    // ==================== Su İçi State Geçişleri ====================

    /// <summary>
    /// Su içindeyken grounded kontrolü ve state geçişlerini yönetir.
    /// Ping-pong önleme: Cooldown ve grounded süre kontrolü ile.
    /// </summary>
    private void UpdateWaterState(bool isGroundedInWater)
    {
        switch (CurrentState)
        {
            case PlayerMovementState.Swimming:
                // Yüzerken zemine temas → su altı yürüme
                // Ping-pong önleme: Zemine en az groundedRequiredDuration kadar temas etmeli
                if (isGroundedInWater 
                    && _groundedTimer >= groundedRequiredDuration 
                    && !GetAscendInput()
                    && !GetDescendInput())
                {
                    SetState(PlayerMovementState.UnderwaterWalking);
                    Debug.Log($"[StateManager] UnderwaterWalking! GroundedTimer: {_groundedTimer:F2}");
                }
                break;

            case PlayerMovementState.UnderwaterWalking:
                // Su altı yürürken Space basılırsa veya zeminden ayrılırsa → yüzme
                if (!isGroundedInWater && _groundedTimer == 0f)
                {
                    SetState(PlayerMovementState.Swimming);
                }
                break;
        }
    }

    // ==================== Dikey Yüzme Hareketi ====================

    /// <summary>
    /// Swimming modunda Space (yukarı) ve Ctrl (aşağı) ile dikey hareket.
    /// CharacterController.Move() ile uygulanır.
    /// </summary>
    private void HandleSwimVerticalMovement()
    {
        if (characterController == null) return;

        float verticalInput = 0f;

        if (GetAscendInput())
            verticalInput = 1f;
        else if (GetDescendInput())
            verticalInput = -1f;

        if (Mathf.Abs(verticalInput) > 0.01f)
        {
            // Dikey hızı hedef hıza doğru lerp et (yumuşak geçiş)
            _swimVerticalVelocity = Mathf.Lerp(_swimVerticalVelocity, verticalInput * verticalSwimSpeed, Time.deltaTime * 5f);
        }
        else
        {
            // Input yoksa yavaşça dur (su direnci)
            float drag = CurrentWaterZone != null ? CurrentWaterZone.waterDrag : 0.3f;
            _swimVerticalVelocity = Mathf.Lerp(_swimVerticalVelocity, 0f, Time.deltaTime * (3f + drag * 10f));
        }

        // Dikey hareketi uygula
        if (Mathf.Abs(_swimVerticalVelocity) > 0.01f)
        {
            characterController.Move(new Vector3(0f, _swimVerticalVelocity * Time.deltaTime, 0f));
        }

        // Su yüzeyine çıkınca dikey hızı sınırla (sudan fırlamaması için)
        if (CurrentWaterZone != null)
        {
            float headY = transform.position.y + (characterController.height * 0.5f);
            if (headY >= CurrentWaterZone.waterSurfaceY && _swimVerticalVelocity > 0f)
            {
                _swimVerticalVelocity = 0f;
            }
        }
    }

    // ==================== Input Yardımcıları ====================

    /// <summary>
    /// Yukarı yüzme inputu (Space tuşu).
    /// </summary>
    public bool GetAscendInput()
    {
        return Keyboard.current != null && Keyboard.current[ascendKey].isPressed;
    }

    /// <summary>
    /// Aşağı dalma inputu.
    /// </summary>
    public bool GetDescendInput()
    {
        return Keyboard.current != null && Keyboard.current[descendKey].isPressed;
    }

    // ==================== Public Yardımcılar ====================

    /// <summary>
    /// Şu an suda mı?
    /// </summary>
    public bool IsSwimming => CurrentState == PlayerMovementState.Swimming;

    /// <summary>
    /// Şu an su altında yürüyor mu?
    /// </summary>
    public bool IsUnderwaterWalking => CurrentState == PlayerMovementState.UnderwaterWalking;

    /// <summary>
    /// Şu an su içinde mi? (Swimming veya UnderwaterWalking)
    /// </summary>
    public bool IsInWaterState => CurrentState != PlayerMovementState.Walking;

    /// <summary>
    /// Su yüzeyine olan derinlik (pozitif = su altında).
    /// </summary>
    public float GetCurrentDepth()
    {
        if (CurrentWaterZone == null) return 0f;
        return CurrentWaterZone.GetDepth(transform.position.y);
    }
}
