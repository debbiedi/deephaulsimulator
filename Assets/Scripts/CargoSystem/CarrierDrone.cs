using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using System;
using System.Collections.Generic;

/// <summary>
/// Hava-su yuzey tasiyici drone. Oyuncu cagirir, drone yukaridan iner,
/// su yuzeyinde hovlanir, oyuncular esya yukler, sonra havalanip
/// teslimat noktasina ucarak esyalari satar.
///
/// Kurulum:
/// 1. Drone prefabina bu scripti ekleyin (NetworkObject + NetworkTransform gerekli).
/// 2. Drone'u sahnenin yukarisina (spawn noktasi) yerlestirin.
/// 3. Inspector'dan targetStation, flySpeed vb. ayarlayin.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarrierDrone : NetworkBehaviour
{
    // --- STATIC REGISTRY ---
    public static readonly List<CarrierDrone> ActiveDrones = new List<CarrierDrone>();

    [Header("Ucus Ayarlari")]
    [Tooltip("Drone'un ucus hizi (m/s)")]
    public float flySpeed = 8f;

    [Tooltip("Inis/cikis dikey hizi (m/s)")]
    public float verticalSpeed = 4f;

    [Tooltip("Donus hizi (derece/saniye)")]
    public float rotationSpeed = 60f;

    [Tooltip("Su yuzeyinin uzerinde hovlama yuksekligi (metre)")]
    public float hoverHeight = 2f;

    [Tooltip("Hedefe ulasma mesafesi (metre)")]
    public float arriveDistance = 2f;

    [Header("Kargo Ayarlari")]
    [Tooltip("Maksimum tasima kapasitesi (kg)")]
    public float maxCargoWeight = 20f;

    [Tooltip("Maksimum esya slotu")]
    public int maxCargoSlots = 15;

    [Header("Zamanlama")]
    [Tooltip("Son esya yuklendikten sonra kalkis bekleme suresi (saniye)")]
    public float departureDelay = 5f;

    [Tooltip("Teslimat noktasinda bosaltma suresi (saniye)")]
    public float deliveryDuration = 3f;

    [Header("Referanslar")]
    [Tooltip("Drone'un teslimat noktasi (DroneStation)")]
    public DroneStation targetStation;

    [Header("Gorsel / Ses")]
    [Tooltip("Pervane/motor sesi icin AudioSource (opsiyonel)")]
    public AudioSource motorAudioSource;

    [Tooltip("Kargo sepeti Transform (esya animasyonu hedef noktasi)")]
    public Transform cargoBasketPoint;

    [Tooltip("Drone Animator (kanat animasyonlari)")]
    public Animator droneAnimator;

    [Tooltip("Kanat/pervane animasyon hizi carpani (1=normal, 5-10=drone hizi)")]
    [Range(1f, 20f)]
    public float wingSpeed = 8f;

    [Header("Etkilesim")]
    [Tooltip("Oyuncunun esya yuklemek icin olmasi gereken max mesafe")]
    public float loadRange = 10f;

    // --- SYNCED STATE ---
    public readonly SyncList<ItemData> CargoItems = new SyncList<ItemData>();
    public readonly SyncVar<float> CurrentWeight = new SyncVar<float>();
    public readonly SyncVar<DroneState> State = new SyncVar<DroneState>();
    public readonly SyncVar<float> DepartureTimer = new SyncVar<float>();

    // --- EVENTS (UI bildirimleri icin) ---
    public event Action<ItemData> OnCargoAdded;
    public event Action OnCargoChanged;
    public event Action<DroneState> OnStateChanged;
    public event Action<float, string> OnItemDelivered;

    [Header("Hover")]
    [Tooltip("Kimse esya yuklemezse drone'un bekleme suresi (saniye)")]
    public float hoverTimeout = 90f;

    // --- PRIVATE SERVER STATE ---
    private Rigidbody _rb;
    private float _departureCountdown;
    private float _deliveryTimer;
    private float _hoverTimer;            // Hover bekleme zamanlayicisi
    private Vector3 _spawnPosition;       // Yukaridaki baslangic noktasi
    private Quaternion _spawnRotation;
    private Vector3 _hoverTargetPos;      // Su yuzeyinde hovlanacagi pozisyon
    private float _waterSurfaceY;         // Su yuzey yuksekligi
    private Renderer[] _renderers;        // Gorsel gizleme/gosterme icin

    // ========================================================
    //  LIFECYCLE
    // ========================================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.isKinematic = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.linearDamping = 2f;
        _rb.angularDamping = 5f;

        // Tum rendererlari cache'le
        _renderers = GetComponentsInChildren<Renderer>();
    }

    private void OnEnable()
    {
        if (!ActiveDrones.Contains(this))
            ActiveDrones.Add(this);
    }

    private void OnDisable()
    {
        ActiveDrones.Remove(this);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;

        if (base.IsServerInitialized)
        {
            State.Value = DroneState.Idle;
            CurrentWeight.Value = 0f;
        }

        // Idle'da gorunmez baslat
        SetVisible(false);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        CargoItems.OnChange += OnCargoListChanged;
        State.OnChange += OnStateValueChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        CargoItems.OnChange -= OnCargoListChanged;
        State.OnChange -= OnStateValueChanged;
    }

    // ========================================================
    //  SYNCLIST / SYNCVAR CALLBACKS
    // ========================================================

    private void OnCargoListChanged(SyncListOperation op, int index,
        ItemData oldItem, ItemData newItem, bool asServer)
    {
        if (op == SyncListOperation.Add)
            OnCargoAdded?.Invoke(newItem);
        OnCargoChanged?.Invoke();
    }

    private void OnStateValueChanged(DroneState prev, DroneState next, bool asServer)
    {
        OnStateChanged?.Invoke(next);

        // Idle'da gizle, diger durumlarda goster
        if (next == DroneState.Idle)
            SetVisible(false);
        else if (prev == DroneState.Idle)
            SetVisible(true);
    }

    /// <summary>
    /// Drone'un gorsellerini ac/kapat. Idle'da gorunmez.
    /// </summary>
    private void SetVisible(bool visible)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = visible;
        }
    }

    // ========================================================
    //  STATE MACHINE (Server-only, FixedUpdate)
    // ========================================================

    void FixedUpdate()
    {
        if (!base.IsServerInitialized) return;

        switch (State.Value)
        {
            case DroneState.Idle:
                // Spawn noktasinda bekliyor, bir sey yapma
                break;
            case DroneState.Arriving:
                UpdateArriving();
                break;
            case DroneState.Hovering:
                UpdateHovering();
                break;
            case DroneState.Loading:
                UpdateLoading();
                break;
            case DroneState.Departing:
                UpdateDeparting();
                break;
            case DroneState.Delivering:
                UpdateDelivering();
                break;
            case DroneState.Returning:
                UpdateReturning();
                break;
        }
    }

    // ========================================================
    //  SUMMONING (Drone Cagirma)
    // ========================================================

    /// <summary>
    /// Herhangi bir oyuncu drone'u cagirir.
    /// Drone yukaridan oyuncunun bulundugu su yuzeyine iner.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ServerSummonDrone(Vector3 callerPosition, NetworkConnection sender = null)
    {
        // Sadece Idle durumundayken cagrilabilir
        if (State.Value != DroneState.Idle)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Drone zaten gorevde!");
            return;
        }

        // Oyuncunun bulundugu su bolgesini bul
        WaterZone zone = WaterZone.GetZoneForPosition(callerPosition);
        if (zone == null)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Drone'u sadece su icindeyken cagirabilirsiniz!");
            return;
        }

        // Su yuzeyini bul
        _waterSurfaceY = zone.waterSurfaceY;

        // Hedef: Oyuncunun XZ pozisyonu, su yuzeyinin uzerinde hover yuksekligi
        _hoverTargetPos = new Vector3(callerPosition.x, _waterSurfaceY + hoverHeight, callerPosition.z);

        // Drone'u spawn pozisyonuna tasi (yukarida)
        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

        State.Value = DroneState.Arriving;
        ObserversNotifyDroneSummoned();

        Debug.Log($"[CarrierDrone] Drone cagirildi! Su yuzeyi Y={_waterSurfaceY}, Hedef: {_hoverTargetPos}");
    }

    // ========================================================
    //  STATE: ARRIVING (Yukaridan inis)
    // ========================================================

    /// <summary>
    /// Spawn noktasindan su yuzeyine dogru iniyor.
    /// </summary>
    private void UpdateArriving()
    {
        float dist = Vector3.Distance(transform.position, _hoverTargetPos);

        // Yaklastikca yavasla (son 5 metrede hiz duser)
        float slowdownDist = 5f;
        float speedFactor = Mathf.Clamp01(dist / slowdownDist);
        float currentSpeed = Mathf.Lerp(verticalSpeed * 0.3f, flySpeed, speedFactor);

        FlyToward(_hoverTargetPos, currentSpeed);

        if (dist <= arriveDistance)
        {
            // Yerine ulasti - kinematik yap, hoverlama basla
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            transform.position = _hoverTargetPos;
            _hoverTimer = 0f;
            State.Value = DroneState.Hovering;

            Debug.Log($"[CarrierDrone] Su yuzeyine ulasti, hoverlaniyor. Pozisyon: {_hoverTargetPos}");
        }
    }

    // ========================================================
    //  STATE: HOVERING (Su yuzeyinde bekleme)
    // ========================================================

    /// <summary>
    /// Su yuzeyinde hovlaniyor, esya yuklenmeyi bekliyor.
    /// Hafif yukari-asagi yalpalama efekti.
    /// Timeout suresinde esya yuklenmezse drone eve doner.
    /// </summary>
    private void UpdateHovering()
    {
        HoldHoverPosition();

        // Hover timeout - kimse esya yuklemezse eve don
        _hoverTimer += Time.fixedDeltaTime;
        if (_hoverTimer >= hoverTimeout)
        {
            Debug.Log("[CarrierDrone] Hover zamani doldu, drone eve donuyor.");
            StartReturning();
        }
    }

    /// <summary>
    /// Drone'u hover pozisyonunda tut (yukari-asagi yalpalama ile).
    /// Kinematik modda direkt transform ile pozisyonlama.
    /// </summary>
    private void HoldHoverPosition()
    {
        // Hedef yukseklikte tut + hafif yalpalama
        float targetY = _waterSurfaceY + hoverHeight;
        float bobOffset = Mathf.Sin(Time.time * 1.5f) * 0.15f;
        targetY += bobOffset;

        // Direkt pozisyonlama (kinematik modda fizik karismaz)
        transform.position = new Vector3(_hoverTargetPos.x, targetY, _hoverTargetPos.z);
    }

    // ========================================================
    //  STATE: LOADING (Esya yukleniyor, geri sayim)
    // ========================================================

    private void UpdateLoading()
    {
        _departureCountdown -= Time.fixedDeltaTime;
        DepartureTimer.Value = Mathf.Max(0f, _departureCountdown);

        // Hover pozisyonunda tut
        HoldHoverPosition();

        if (_departureCountdown <= 0f || CurrentWeight.Value >= maxCargoWeight)
        {
            StartDeparting();
        }
    }

    // ========================================================
    //  STATE: DEPARTING (Havalanip teslimat noktasina ucus)
    // ========================================================

    /// <summary>
    /// Kinematik'ten cikip ucar moda gec.
    /// </summary>
    private void ExitHoverMode()
    {
        _rb.isKinematic = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    [Server]
    private void StartDeparting()
    {
        // Bos kargoyla kalkma — eve don
        if (CargoItems.Count == 0)
        {
            Debug.Log("[CarrierDrone] Kargo bos, drone eve donuyor.");
            StartReturning();
            return;
        }

        ExitHoverMode();
        State.Value = DroneState.Departing;
        ObserversNotifyDeparture();
        Debug.Log("[CarrierDrone] Drone havalanıyor, teslimat noktasina gidiyor!");
    }

    /// <summary>
    /// Drone'u dogrudan eve dondur (esya yoksa veya timeout).
    /// </summary>
    [Server]
    private void StartReturning()
    {
        ExitHoverMode();
        State.Value = DroneState.Returning;
    }

    /// <summary>
    /// Teslimat noktasina dogru ucuyor.
    /// </summary>
    private void UpdateDeparting()
    {
        if (targetStation == null || targetStation.deliveryPoint == null)
        {
            Debug.LogWarning("[CarrierDrone] Teslimat noktasi yok! Geri donuyor.");
            State.Value = DroneState.Returning;
            return;
        }

        Vector3 stationPos = targetStation.deliveryPoint.position;
        FlyToward(stationPos, flySpeed);

        float dist = Vector3.Distance(transform.position, stationPos);
        if (dist <= arriveDistance)
        {
            ArriveAtStation();
        }
    }

    // ========================================================
    //  STATE: DELIVERING (Teslimat noktasinda bosaltma)
    // ========================================================

    [Server]
    private void ArriveAtStation()
    {
        _deliveryTimer = 0f;
        State.Value = DroneState.Delivering;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    private void UpdateDelivering()
    {
        _deliveryTimer += Time.fixedDeltaTime;

        if (_deliveryTimer >= deliveryDuration)
        {
            DeliverAllCargo();
            State.Value = DroneState.Returning;
        }
    }

    [Server]
    private void DeliverAllCargo()
    {
        if (targetStation == null) return;

        float totalSaleValue = 0f;
        int itemCount = CargoItems.Count;

        for (int i = CargoItems.Count - 1; i >= 0; i--)
        {
            ItemData item = CargoItems[i];
            float salePrice = item.CurrentPrice;
            totalSaleValue += salePrice;
            ObserversNotifyItemDelivered(salePrice, item.ItemName);
        }

        if (TeamTreasury.Instance != null)
            TeamTreasury.Instance.AddToTreasury(totalSaleValue);

        targetStation.ServerReceiveDelivery(itemCount, totalSaleValue);

        CargoItems.Clear();
        CurrentWeight.Value = 0f;

        Debug.Log($"[CarrierDrone] Teslimat tamamlandi! {itemCount} esya, toplam: {totalSaleValue:F0}TL");
    }

    // ========================================================
    //  STATE: RETURNING (Spawn noktasina geri donus)
    // ========================================================

    private void UpdateReturning()
    {
        FlyToward(_spawnPosition, flySpeed);

        float dist = Vector3.Distance(transform.position, _spawnPosition);
        if (dist <= arriveDistance)
        {
            ArriveHome();
        }
    }

    [Server]
    private void ArriveHome()
    {
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;
        State.Value = DroneState.Idle;

        Debug.Log("[CarrierDrone] Drone yuvasina dondu, tekrar cagrilmaya hazir.");
    }

    // ========================================================
    //  FLIGHT MOVEMENT
    // ========================================================

    /// <summary>
    /// Verilen pozisyona dogru uc (havada, engelsiz direkt ucus).
    /// </summary>
    private void FlyToward(Vector3 target, float speed)
    {
        Vector3 direction = (target - transform.position).normalized;

        // Yumusak donus (sadece XZ duzleminde, drone yukari bakmaz)
        Vector3 flatDir = new Vector3(direction.x, 0f, direction.z);
        if (flatDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            _rb.MoveRotation(Quaternion.RotateTowards(
                _rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }

        // Hareket kuvveti
        Vector3 desiredVelocity = direction * speed;
        Vector3 forceNeeded = (desiredVelocity - _rb.linearVelocity) * _rb.mass;

        float maxForce = speed * _rb.mass * 3f;
        if (forceNeeded.magnitude > maxForce)
            forceNeeded = forceNeeded.normalized * maxForce;

        _rb.AddForce(forceNeeded, ForceMode.Force);
    }

    // ========================================================
    //  ITEM LOADING (ServerRpc - herhangi bir oyuncu)
    // ========================================================

    /// <summary>
    /// Herhangi bir oyuncu tarafindan cagrilabilir (RequireOwnership = false).
    /// Sunucu mesafe, agirlik ve state kontrolu yapar.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ServerLoadItem(NetworkObject itemNetObj, NetworkConnection sender = null)
    {
        if (itemNetObj == null) return;

        // Sadece Hovering veya Loading state'inde esya yuklenebilir
        if (State.Value != DroneState.Hovering && State.Value != DroneState.Loading)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Drone su anda mesgul!");
            return;
        }

        GrabbableObject grabbable = itemNetObj.GetComponent<GrabbableObject>();
        if (grabbable == null) return;

        // Balon takili esya yuklenemez
        if (grabbable.AttachedBagCount > 0)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Balon takili esya yuklenemez!");
            return;
        }

        // Mesafe kontrolu (sadece yatay XZ — dikey mesafe onemli degil
        // cunku oyuncu su altinda, drone su yuzeyinde)
        if (sender != null && sender.FirstObject != null)
        {
            Vector3 playerPos = sender.FirstObject.transform.position;
            Vector3 dronePos = transform.position;
            float xzDist = Vector2.Distance(
                new Vector2(playerPos.x, playerPos.z),
                new Vector2(dronePos.x, dronePos.z));
            if (xzDist > loadRange)
            {
                TargetNotifyLoadFailed(sender, "Drone'a daha yakin olmalisiniz!");
                return;
            }
        }

        // Agirlik kontrolu
        ItemData data = new ItemData(grabbable);
        if (CurrentWeight.Value + data.Weight > maxCargoWeight)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Drone'un kargo kapasitesi dolu!");
            return;
        }

        // Slot kontrolu
        if (CargoItems.Count >= maxCargoSlots)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Drone'da bos slot yok!");
            return;
        }

        // --- YUKLEME BASARILI ---
        CargoItems.Add(data);
        CurrentWeight.Value += data.Weight;

        base.ServerManager.Despawn(itemNetObj);

        // State'i Loading'e gec ve zamanlayiciyi baslat/sifirla
        State.Value = DroneState.Loading;
        _departureCountdown = departureDelay;
        DepartureTimer.Value = departureDelay;

        Debug.Log($"[CarrierDrone] '{data.ItemName}' yuklendi! " +
                  $"Agirlik: {CurrentWeight.Value:F1}/{maxCargoWeight} kg | " +
                  $"Esya: {CargoItems.Count}/{maxCargoSlots}");

        if (sender != null)
            TargetNotifyLoadSuccess(sender, data);

        ObserversNotifyItemLoaded(data);
    }

    // ========================================================
    //  STATIC HELPERS
    // ========================================================

    /// <summary>
    /// En yakin cagrilabilir (Idle) drone'u bul.
    /// </summary>
    public static CarrierDrone FindNearestSummonable(Vector3 position)
    {
        CarrierDrone nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < ActiveDrones.Count; i++)
        {
            if (ActiveDrones[i] == null) continue;
            if (ActiveDrones[i].State.Value != DroneState.Idle) continue;

            float dist = Vector3.Distance(ActiveDrones[i]._spawnPosition, position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = ActiveDrones[i];
            }
        }

        return nearest;
    }

    /// <summary>
    /// En yakin esya yuklenebilir (Hovering/Loading) drone'u bul.
    /// Yatay (XZ) mesafe kullanir — dikey fark onemli degil.
    /// </summary>
    public static CarrierDrone FindNearestLoadable(Vector3 position, float maxRange)
    {
        CarrierDrone nearest = null;
        float nearestDist = maxRange;

        for (int i = 0; i < ActiveDrones.Count; i++)
        {
            if (ActiveDrones[i] == null) continue;

            DroneState state = ActiveDrones[i].State.Value;
            if (state != DroneState.Hovering && state != DroneState.Loading) continue;

            // Yatay mesafe (XZ) — oyuncu su altinda, drone su yuzeyinde
            Vector3 dronePos = ActiveDrones[i].transform.position;
            float xzDist = Vector2.Distance(
                new Vector2(position.x, position.z),
                new Vector2(dronePos.x, dronePos.z));
            if (xzDist < nearestDist)
            {
                nearestDist = xzDist;
                nearest = ActiveDrones[i];
            }
        }

        return nearest;
    }

    // ========================================================
    //  RPCs (Bildirimler)
    // ========================================================

    [TargetRpc]
    private void TargetNotifyLoadFailed(NetworkConnection conn, string reason)
    {
        Debug.Log($"[CarrierDrone] Yukleme basarisiz: {reason}");
    }

    [TargetRpc]
    private void TargetNotifyLoadSuccess(NetworkConnection conn, ItemData data)
    {
        OnCargoAdded?.Invoke(data);
    }

    [ObserversRpc]
    private void ObserversNotifyItemLoaded(ItemData data)
    {
        OnCargoAdded?.Invoke(data);
    }

    [ObserversRpc]
    private void ObserversNotifyDroneSummoned()
    {
        // Drone cagirildi sesi / efekti
        if (motorAudioSource != null)
            motorAudioSource.Play();
    }

    [ObserversRpc]
    private void ObserversNotifyDeparture()
    {
        // Kalkis sesi
    }

    [ObserversRpc]
    private void ObserversNotifyItemDelivered(float price, string itemName)
    {
        OnItemDelivered?.Invoke(price, itemName);
    }

    // ========================================================
    //  VISUALS (All Clients)
    // ========================================================

    void Update()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        bool isActive = State.Value != DroneState.Idle;

        // Animator kontrolu (kanat animasyonlari)
        // Aktifken Animator acik = animasyon oynar, Idle'da kapali = durur
        if (droneAnimator != null)
        {
            droneAnimator.enabled = isActive;
            if (isActive)
                droneAnimator.speed = wingSpeed;
        }

        if (motorAudioSource != null)
        {
            if (isActive && !motorAudioSource.isPlaying)
                motorAudioSource.Play();
            else if (!isActive && motorAudioSource.isPlaying)
                motorAudioSource.Stop();
        }
    }

    // ========================================================
    //  EDITOR GIZMOS
    // ========================================================

    private void OnDrawGizmosSelected()
    {
        // Spawn noktasi
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, 1f);

        // Teslimat noktasina cizgi
        if (targetStation != null && targetStation.deliveryPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, targetStation.deliveryPoint.position);
            Gizmos.DrawWireSphere(targetStation.deliveryPoint.position, 1f);
        }

        // Yukleme mesafesi
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, loadRange);
    }
}
