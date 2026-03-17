using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Object;
using System;

/// <summary>
/// Client tarafı pompalama mini-game'i.
/// [E] tuşuna hızlıca basarak balonu şişirir. Basmazsanız balon yavaşça söner.
///
/// Co-op: Birden fazla oyuncu aynı balonu pompalayabilir.
/// Her basışta ServerPumpBag() çağrılır (RequireOwnership = false).
/// 2 kişi pompalıyorsa 2× hızlı, 3 kişi 3× hızlı şişer.
///
/// Player prefabına eklenir.
/// </summary>
public class InflationMiniGame : NetworkBehaviour
{
    [Header("Pompalama Ayarları")]
    [Tooltip("Pompalama tuşu")]
    public Key pumpKey = Key.E;

    [Tooltip("Oyuncu başına saniyedeki max pompa sayısı (client-side throttle)")]
    public int maxPumpsPerSecond = 8;

    [Tooltip("Mini-game zaman aşımı (saniye) - bu süre içinde hiç basmazsa mini-game kapanır")]
    public float timeout = 15f;

    [Tooltip("İptal tuşu")]
    public Key cancelKey = Key.Escape;

    // --- State ---
    private LiftingBag _currentBag;
    private float _lastPumpTime;
    private float _idleTimer;
    private float _minPumpInterval;
    private float _graceTimer; // Başlangıçta mesafe kontrolü yapılmaz (network senkronizasyon süresi)

    // --- Public Erişimler (UI için) ---
    public bool IsActive => _currentBag != null;
    public LiftingBag CurrentBag => _currentBag;

    // --- Events (UI bildirimleri için) ---
    public event Action OnMiniGameStarted;
    public event Action OnMiniGameEnded;
    public event Action OnPumped;

    // --- PlayerGrabber bırakınca mini-game iptal ---
    private PlayerGrabber _playerGrabber;

    private void Start()
    {
        _minPumpInterval = 1f / maxPumpsPerSecond;

        _playerGrabber = GetComponent<PlayerGrabber>();
        if (_playerGrabber != null)
        {
            _playerGrabber.OnItemReleased += OnItemReleased;
        }
    }

    private void OnDestroy()
    {
        if (_playerGrabber != null)
        {
            _playerGrabber.OnItemReleased -= OnItemReleased;
        }
    }

    private void OnItemReleased()
    {
        // Eşya bırakıldığında aktif mini-game varsa iptal et
        if (IsActive)
        {
            Debug.Log("[InflationMiniGame] Pompalama bitti. Sebep: Tutulan eşya bırakıldı (OnItemReleased).");
            StopPumping();
        }
    }

    void Update()
    {
        if (!base.IsOwner) return;
        if (_currentBag == null) return;

        // Başlangıç koruma süresi (network senkronizasyon için 1.5 saniye bekle)
        _graceTimer += Time.deltaTime;

        // Balon artık pompalanabilir durumda değilse kapat (koruma süresinden sonra)
        if (_graceTimer > 1.5f)
        {
            LiftingBagState state = _currentBag.State.Value;
            if (state == LiftingBagState.Inflated || state == LiftingBagState.Floating || state == LiftingBagState.Detached)
            {
                Debug.Log($"[InflationMiniGame] Pompalama bitti. Sebep: State = {state}");
                StopPumping();
                return;
            }

            // Mesafe kontrolü: Eşyadan çok uzaklaştıysak kapat (Uzaktan atma eklendiği için limiti 55f yaptık)
            if (_currentBag.TargetItem != null)
            {
                float dist = Vector3.Distance(transform.position, _currentBag.TargetItem.transform.position);
                if (dist > 55f) 
                {
                    Debug.Log($"[InflationMiniGame] Pompalama bitti. Sebep: Mesafe sınırı aşıldı (Dist: {dist} > Limit: 55)");
                    StopPumping();
                    return;
                }
            }
        }

        // İptal tuşu
        if (Keyboard.current != null && Keyboard.current[cancelKey].wasPressedThisFrame)
        {
            Debug.Log("[InflationMiniGame] Pompalama bitti. Sebep: İptal tuşuna (Escape) basıldı.");
            StopPumping();
            return;
        }

        // Sol tık ile de iptal (eşya tutma ile çakışmasın diye sadece eşya tutmuyorken)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (_playerGrabber == null || _playerGrabber.HeldObject == null)
            {
                Debug.Log("[InflationMiniGame] Pompalama bitti. Sebep: Sol tuşa tıklandı.");
                StopPumping();
                return;
            }
        }

        // Zaman aşımı kontrolü
        _idleTimer += Time.deltaTime;
        if (_idleTimer >= timeout)
        {
            Debug.Log($"[InflationMiniGame] Pompalama bitti. Sebep: Zaman aşımı ({timeout} saniye boyunca pompalanmadı).");
            StopPumping();
            return;
        }

        // [E] tuşu: Pompala
        if (Keyboard.current != null && Keyboard.current[pumpKey].wasPressedThisFrame)
        {
            TryPump();
        }
    }

    /// <summary>
    /// Mini-game'i başlatır. Belirtilen balonu pompalamaya başlar.
    /// LiftingBagAttacher tarafından çağrılır.
    /// </summary>
    public void StartPumping(LiftingBag bag)
    {
        if (bag == null) return;

        _currentBag = bag;
        _idleTimer = 0f;
        _lastPumpTime = 0f;
        _graceTimer = 0f;

        OnMiniGameStarted?.Invoke();
        Debug.Log("[InflationMiniGame] Pompalama başladı! [E] tuşuna bas!");
    }

    /// <summary>
    /// Mini-game'i durdurur.
    /// </summary>
    public void StopPumping()
    {
        if (_currentBag == null) return;

        _currentBag = null;
        OnMiniGameEnded?.Invoke();
        Debug.Log("[InflationMiniGame] Pompalama bitti.");
    }

    /// <summary>
    /// Pompa basışı dener. Rate-limit client tarafında da uygulanır.
    /// </summary>
    private void TryPump()
    {
        if (_currentBag == null) return;

        float now = Time.time;
        if (now - _lastPumpTime < _minPumpInterval) return;

        _lastPumpTime = now;
        _idleTimer = 0f; // Aktif basıyorsa timeout sıfırla

        // Sunucuya pompa gönder (co-op: RequireOwnership = false)
        _currentBag.ServerPumpBag();

        OnPumped?.Invoke();
    }
}
