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

    [Tooltip("Su yuzeyinin uzerinde veya karakterin yaninda hovlama yuksekligi (metre)")]
    public float hoverHeight = 2f;

    [Tooltip("Karakterin ne kadar yaninda duracak (metre)")]
    public float sideOffset = 1.5f;

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

    [Tooltip("Esya yukleme ve hata seslerinin calinmasi icin ikinci bir AudioSource (sfx icin)")]
    public AudioSource sfxAudioSource;

    [Tooltip("Drona esya basariyla yuklendiginde calacak ses")]
    public AudioClip itemLoadSound;

    [Tooltip("Kapasite dolu vb. hata durumunda calacak ses")]
    public AudioClip cargoErrorSound;

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

    [Header("Yapay Zeka (AI) Pathfinding")]
    [Tooltip("Dronun carpmamak icin algilayacagi duvar/engel katmanlari. Inspector'dan Default vs secin!")]
    public LayerMask obstacleMask;
    [Tooltip("Sensorun engelleri gorecegi mesafe. (Orn: 4 metre)")]
    public float sensorRange = 4f;
    [Tooltip("Duvara yaklasinca onu ne kadar gucle itip kacacagi")]
    public float avoidanceForce = 8f;
    [Tooltip("Acil Durum (Failsafe): Drone kac metre uzakta kalirsa oyuncunun sirtina isinlanip kurtarilsin?")]
    public float teleportDistance = 35f;
    [Tooltip("Breadcrumb'larin ne kadar aralikla birakilacagi (metre)")]
    public float breadcrumbDropDistance = 2f;
    [Tooltip("Dronun magaraya girdigi varsayilip ekmek kirintilariyla takip edecegi mesafe (metre)")]
    public float breadcrumbChaseDistance = 8f;

    // --- PRIVATE SERVER STATE ---
    private Rigidbody _rb;
    private float _departureCountdown;
    private float _deliveryTimer;
    private float _hoverTimer;            // Hover bekleme zamanlayicisi
    private Transform _trackingTarget;    // Drone'un su uzerinde oyuncuyu takip etmesi icin hedeflenen oyuncu
    private Vector3 _spawnPosition;       // Yukaridaki baslangic noktasi
    private Quaternion _spawnRotation;
    private Vector3 _hoverTargetPos;      // Su yuzeyinde hovlanacagi pozisyon
    private Vector3 _currentVelocity;     // SmoothDamp icerisinde kullanilan hiz referansi
    private Vector3 _wanderOffset;        // Oyuncu dururken yapilan rastgele gezinme offset'i
    private float _wanderTimer;           // Rastgele gezinme periyodu
    private float _waterSurfaceY;         // Su yuzey yuksekligi
    private Renderer[] _renderers;        // Gorsel gizleme/gosterme icin

    private Queue<Vector3> _breadcrumbs = new Queue<Vector3>();
    private bool _isFollowingBreadcrumbs = false;
    private Vector3 _lastBreadcrumbPos;

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

        // Tum rendererlari cache'le (true diyerek kapali olanlari da bulur)
        _renderers = GetComponentsInChildren<Renderer>(true);
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

        if (base.IsServerInitialized)
        {
            State.Value = DroneState.Idle;
            CurrentWeight.Value = 0f;
            // Spawn pozisyonunu yukarıda ayarla (Despawn olurken buraya uçar)
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        // Idle'da gorunmez baslat
        SetVisible(false);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        // --- LAG/KASMA ÇÖZÜMÜ ---
        // Eger Server ("Odayi Kuran") degilsek, bizim tarafimizda drone'un fizigi izleyici (kinematik) olmali!
        // Aksi takdirde, FishNet'in NetworkTransform verisiyle, bu bilgisayardaki fizik moturu her karede kafa kafaya carpisir (kasarak ilerleme efekti)
        if (!base.IsServerInitialized && _rb != null)
        {
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.None; // Unity ve FishNet cift interpolasyon cakismasini onle
        }

        CargoItems.OnChange += OnCargoListChanged;
        State.OnChange += OnStateValueChanged;

        // Mevcut state'e gore bastan gorunurlugu ayarla (sonradan baglanan client'lar icin onemli)
        if (State.Value == DroneState.Idle)
            SetVisible(false);
        else
            SetVisible(true);
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
        else
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
    //  SUMMONING & INIT (Drone Çağırma ve Spawn)
    // ========================================================

    public readonly SyncVar<int> OwnerClientId = new SyncVar<int>(-1);

    /// <summary>
    /// PlayerGrabber tarafından drone objesi spawn edildikten hemen sonra çağrılır.
    /// Zaten havada (dropHeight) yaratıldı. Sadece aşağı inecek.
    /// </summary>
    [Server]
    public void ServerInitAndSummon(PlayerGrabber callerScript, float waterY)
    {
        if (callerScript == null || callerScript.Owner == null) return;

        OwnerClientId.Value = callerScript.Owner.ClientId;
        _waterSurfaceY = waterY;

        Vector3 callerPosition = callerScript.transform.position;
        // Karakterin sag tarafinda (X ekseninde offset) ve yukarisinda (+Y) bir nokta belirle
        _hoverTargetPos = callerPosition + (callerScript.transform.right * sideOffset) + new Vector3(0, hoverHeight, 0);

        // Başlangıç fiziğini ayarla (kinematik inis yapacagiz)
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // Oyuncuyu bul ve takip hedefine ayarla
        _trackingTarget = callerScript.transform;
        Debug.Log($"[CarrierDrone] Takip edilecek obje ayarlandi: {_trackingTarget.name} (ClientId: {OwnerClientId.Value})");

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
        // Surekli hareket eden oyuncuyu takip etmek icin hedef yuzey guncellenir
        if (_trackingTarget != null)
        {
            // Oyuncunun sağına ve yukarıya offset belirle
            _hoverTargetPos = _trackingTarget.position + (_trackingTarget.right * sideOffset) + new Vector3(0, hoverHeight, 0);
        }

        // Asagi inis ve takip (kinematik hareket)
        float currentSpeed = verticalSpeed * 1.5f; // Hizli inis
        Vector3 nextPos = Vector3.MoveTowards(transform.position, _hoverTargetPos, currentSpeed * Time.fixedDeltaTime);
        _rb.MovePosition(nextPos);
        
        // Asagi dogru yonelme (sadece gorsel amacli)
        Vector3 direction = transform.forward;
        if (_trackingTarget != null)
        {
            // İnerken oyuncuya baksın
            direction = (_trackingTarget.position - transform.position);
        }
        else
        {
            direction = (_hoverTargetPos - nextPos);
        }

        direction.y = 0;
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion lookRot = Quaternion.LookRotation(direction.normalized, Vector3.up);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, lookRot, rotationSpeed * Time.fixedDeltaTime * 0.05f));
        }

        float dist = Vector3.Distance(nextPos, _hoverTargetPos);
        if (dist <= arriveDistance)
        {
            // Yerine ulasti - hoverlama basla
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.MovePosition(_hoverTargetPos);
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
    /// Oyuncu manuel olarak gonderene kadar burada kalir.
    /// </summary>
    private void UpdateHovering()
    {
        HoldHoverPosition();
    }

    /// <summary>
    /// Drone'u hover pozisyonunda tut (yukari-asagi yalpalama ile).
    /// Kinematik modda direkt transform ile pozisyonlama.
    /// </summary>
    private void HoldHoverPosition()
    {
        if (_trackingTarget != null)
        {
            float distToPlayerFull = Vector3.Distance(transform.position, _trackingTarget.position);

            // 1. TIER 3 - FAILSAFE (ACIL DURUM ISINLANMA)
            // Drone oyuncudan cok uzak kalirsa her seyi sifirla ve direk yanina isinla
            if (distToPlayerFull > teleportDistance)
            {
                _rb.position = _trackingTarget.position + (_trackingTarget.right * sideOffset) - (_trackingTarget.forward * 1.5f) + new Vector3(0, hoverHeight, 0);
                _breadcrumbs.Clear();
                _isFollowingBreadcrumbs = false;
                _currentVelocity = Vector3.zero;
            }

            // 2. OYUNCUNUN ARDINDAN BREADCRUMB (KIRINTI) BIRAKMA MANTIGI
            if (Vector3.Distance(_trackingTarget.position, _lastBreadcrumbPos) > breadcrumbDropDistance)
            {
                Vector3 newCrumb = _trackingTarget.position - (_trackingTarget.forward * 1f) + new Vector3(0, hoverHeight, 0);
                _breadcrumbs.Enqueue(newCrumb);
                _lastBreadcrumbPos = _trackingTarget.position;
                if (_breadcrumbs.Count > 50) _breadcrumbs.Dequeue();
            }

            // Orijinal ideal pos
            Vector3 idealPosition = _trackingTarget.position + (_trackingTarget.right * sideOffset) - (_trackingTarget.forward * 1.5f) + new Vector3(0, hoverHeight, 0);

            if (distToPlayerFull > breadcrumbChaseDistance)
            {
                _isFollowingBreadcrumbs = true;
                _wanderTimer = 0f;
            }

            if (distToPlayerFull < 5.0f && _breadcrumbs.Count == 0)
            {
                _isFollowingBreadcrumbs = false;
                _breadcrumbs.Clear();
            }

            Vector3 finalTargetPos = idealPosition;

            if (_isFollowingBreadcrumbs && _breadcrumbs.Count > 0)
            {
                Vector3 peekCrumb = _breadcrumbs.Peek();
                finalTargetPos = peekCrumb;
                if (Vector3.Distance(transform.position, peekCrumb) < 2.0f)
                {
                    _breadcrumbs.Dequeue();
                }
            }
            else
            {
                // ÖLÜ BÖLGE (DEADZONE) VE GEZINME KONTROLU
                Vector2 droneXZ = new Vector2(transform.position.x, transform.position.z);
                Vector2 idealXZ = new Vector2(idealPosition.x, idealPosition.z);
                float distToIdeal = Vector2.Distance(droneXZ, idealXZ);

                if (distToIdeal > 4.0f)
                {
                    _hoverTargetPos = idealPosition;
                    _wanderTimer = 0f;
                    _wanderOffset = Vector3.zero;
                }
                else
                {
                    _wanderTimer -= Time.fixedDeltaTime;
                    if (_wanderTimer <= 0f)
                    {
                        _wanderTimer = UnityEngine.Random.Range(7f, 10f);
                        float randX = UnityEngine.Random.Range(-1.5f, 1.5f);
                        float randZ = UnityEngine.Random.Range(-1.5f, 1.5f);
                        _wanderOffset = new Vector3(randX, 0, randZ);
                    }
                }
                finalTargetPos = _hoverTargetPos;
            }

            // 3. TIER 1 - ENGELDEN KACINMA (RAYCAST OBSTACLE AVOIDANCE)
            // Duvarlara surtmeyi ve sikismayi engellemek icin
            Vector3 avoidanceOffset = Vector3.zero;
            RaycastHit hit;

            if (Physics.Raycast(transform.position, transform.forward, out hit, sensorRange, obstacleMask))
            {
                avoidanceOffset += hit.normal * avoidanceForce * (1.0f - hit.distance / sensorRange);
            }
            if (Physics.Raycast(transform.position, transform.right, out hit, sensorRange, obstacleMask))
            {
                avoidanceOffset += hit.normal * avoidanceForce * (1.0f - hit.distance / sensorRange);
            }
            if (Physics.Raycast(transform.position, -transform.right, out hit, sensorRange, obstacleMask))
            {
                avoidanceOffset += hit.normal * avoidanceForce * (1.0f - hit.distance / sensorRange);
            }
            
            // Yukari (tavan veya magara tavanlari) carpismalarini engellemek icin
            if (Physics.Raycast(transform.position, transform.up, out hit, sensorRange, obstacleMask))
            {
                avoidanceOffset += hit.normal * avoidanceForce * (1.0f - hit.distance / sensorRange);
            }
            
            // Ekstra guvenlik: Asagiya (zemin/kayalar) sert inmemesi icin
            if (Physics.Raycast(transform.position, -transform.up, out hit, sensorRange, obstacleMask))
            {
                avoidanceOffset += hit.normal * avoidanceForce * (1.0f - hit.distance / sensorRange);
            }

            Vector3 targetPosWithWanderAndAvoidance = finalTargetPos + _wanderOffset + avoidanceOffset;
            float bobOffset = Mathf.Sin(Time.time * 2.5f) * 0.15f; 
            targetPosWithWanderAndAvoidance.y += bobOffset;

            // 4. YUMUŞAK TAKİP VE FİZİK (SMOOTHDAMP)
            float smoothTime = 0.6f; 
            Vector3 nextPos = Vector3.SmoothDamp(transform.position, targetPosWithWanderAndAvoidance, ref _currentVelocity, smoothTime, flySpeed);
            _rb.MovePosition(nextPos);

            // 5. DONUS (ROTATION) - SLERP
            Vector3 directionToFace = transform.forward; // Varsayılan olarak eski yönü koru

            if (_trackingTarget != null && _currentVelocity.sqrMagnitude > 0.1f)
            {
                // Sadece hareket halindeyken (hız 0.1'den büyükse) karaktere (oyuncuya) bak
                directionToFace = (_trackingTarget.position - transform.position);
            }
            else if (_trackingTarget == null)
            {
                directionToFace = (targetPosWithWanderAndAvoidance - nextPos);
            }

            directionToFace.y = 0; // Sadece Y ekseninde (sağa-sola) dönsün, aşağı/yukarı eğilmesin
            
            if (directionToFace.sqrMagnitude > 0.01f)
            {
                Quaternion lookRot = Quaternion.LookRotation(directionToFace.normalized, Vector3.up);

                float forwardTilt = Mathf.Clamp(Vector3.Dot(transform.forward, _currentVelocity) * 2.5f, -20f, 20f);
                float sideTilt = Mathf.Clamp(Vector3.Dot(transform.right, _currentVelocity) * -2.5f, -20f, 20f);
                
                Quaternion tiltOffset = Quaternion.Euler(forwardTilt, 0, sideTilt);
                Quaternion finalRotation = lookRot * tiltOffset;

                _rb.MoveRotation(Quaternion.Slerp(transform.rotation, finalRotation, Time.fixedDeltaTime * rotationSpeed * 0.03f));
            }
        }
    }

    // ========================================================
    //  STATE: LOADING (Esya yukleniyor)
    // ========================================================

    private void UpdateLoading()
    {
        // Surekli hover kalir. Kalkis manuel "F" ile yapilacak.
        HoldHoverPosition();
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

    // ========================================================
    //  MANUAL SEND AWAY
    // ========================================================

    [ServerRpc(RequireOwnership = false)]
    public void ServerSendAway()
    {
        // Geri donus veya kalkis disinda cagrildiysa kabul et
        if (State.Value == DroneState.Hovering || State.Value == DroneState.Loading)
        {
            ExitHoverMode();
            State.Value = DroneState.Returning;
            Debug.Log("[CarrierDrone] Oyuncu tarafindan gonderildi. Yukariya ucuyor.");
        }
    }

    // ========================================================
    //  STATE: DEPARTING / DELIVERING (Kullanılmıyor, direkt Returning'e geçiyor)
    // ========================================================

    private void UpdateDeparting() { /* Kullanılmıyor */ }
    private void UpdateDelivering() { /* Kullanılmıyor */ }

    // ========================================================
    //  STATE: RETURNING (Gokyuzundeki spawn noktasina cikis ve despawn)
    // ========================================================

    private void UpdateReturning()
    {
        // Direkt olarak en basinda baslatildigi XZ ekseninden dusmeden dikine yukari ucus
        Vector3 targetPos = new Vector3(transform.position.x, _spawnPosition.y, transform.position.z);
        FlyToward(targetPos, verticalSpeed * 2f);

        float dist = Mathf.Abs(transform.position.y - targetPos.y);
        if (dist <= arriveDistance)
        {
            ArriveHomeAndDeliver();
        }
    }

    [Server]
    private void ArriveHomeAndDeliver()
    {
        // Esyalari gemiye (kasaya) ver
        DeliverAllCargo();

        // Sistemi sifirla
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        State.Value = DroneState.Idle;

        Debug.Log("[CarrierDrone] Drone oyuncunun isini bitirdi, esyalar teslim edildi, Despawn oluyor.");
        base.ServerManager.Despawn(base.NetworkObject);
    }

    [Server]
    private void DeliverAllCargo()
    {
        float totalSaleValue = 0f;
        int itemCount = CargoItems.Count;

        for (int i = CargoItems.Count - 1; i >= 0; i--)
        {
            ItemData item = CargoItems[i];
            float salePrice = item.CurrentPrice;
            totalSaleValue += salePrice;
            ObserversNotifyItemDelivered(salePrice, item.ItemName);
        }

        // Oyun ici Hazineye veya Cüzdana eklensin
        if (TeamTreasury.Instance != null && totalSaleValue > 0)
        {
            TeamTreasury.Instance.AddToTreasury(totalSaleValue);
        }
        else if (totalSaleValue > 0)
        {
            // Ortak hazine yoksa varsayılan veya test mekanizması baska bir yere koyulabilir
            Debug.LogWarning("[CarrierDrone] Hazine bulunamadi, para uctu.");
        }

        CargoItems.Clear();
        CurrentWeight.Value = 0f;

        if(itemCount > 0)
            Debug.Log($"[CarrierDrone] Teslimat (Gemiye) tamamlandi! ({itemCount} eşya), Toplam: {totalSaleValue:F0}TL");
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
            ObserversNotifyErrorSound();
            return;
        }

        // Slot kontrolu
        if (CargoItems.Count >= maxCargoSlots)
        {
            if (sender != null)
                TargetNotifyLoadFailed(sender, "Drone'da bos slot yok!");
            ObserversNotifyErrorSound();
            return;
        }

        // --- YUKLEME BASARILI ---
        CargoItems.Add(data);
        CurrentWeight.Value += data.Weight;

        base.ServerManager.Despawn(itemNetObj);

        // State'i Loading veya Hovering'de tut (Farki yok, sadece UI vs icin bilsin yeter)
        State.Value = DroneState.Loading;

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
        PlaySfx(itemLoadSound);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ServerPlayErrorSound()
    {
        ObserversNotifyErrorSound();
    }

    [ObserversRpc]
    private void ObserversNotifyErrorSound()
    {
        PlaySfx(cargoErrorSound);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(clip, 1f);
        }
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
