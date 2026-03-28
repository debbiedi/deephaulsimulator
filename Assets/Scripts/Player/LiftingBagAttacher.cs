using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine.InputSystem;

/// <summary>
/// Oyuncu bileşeni: Kaldırma balonlarını eşyalara takar.
/// Player prefabına eklenir. [F] tuşu ile balon takma işlemi yapılır.
///
/// Gerekli referanslar (Inspector):
/// - PlayerGrabber: Tutulan eşyayı öğrenmek için
/// - PlayerMovementStateManager: Su altı kontrolü için
/// - bagPrefab: Kaldırma balonu NetworkObject prefabı
/// </summary>
public class LiftingBagAttacher : NetworkBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Aynı player üzerindeki PlayerGrabber")]
    public PlayerGrabber playerGrabber;

    [Tooltip("Aynı player üzerindeki PlayerMovementStateManager")]
    public PlayerMovementStateManager movementStateManager;

    [Tooltip("Kaldırma balonu prefabı (NetworkObject)")]
    public NetworkObject bagPrefab;

    [Header("Envanter")]
    [Tooltip("Oyuncunun başlangıç balon sayısı")]
    public int startingBagCount = 3;

    [Header("Ayarlar")]
    [Tooltip("Balon takma tuşu")]
    public Key attachKey = Key.L;

    // --- SyncVar: Oyuncunun kalan balon sayısı ---
    public readonly SyncVar<int> BagCount = new SyncVar<int>();

    // --- Co-op: Yakındaki pompalanabilir balon ---
    private LiftingBag _nearbyPumpableBag;
    private InflationMiniGame _miniGame;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (base.IsServerInitialized)
        {
            BagCount.Value = startingBagCount;
        }
    }

    private void Start()
    {
        _miniGame = GetComponent<InflationMiniGame>();
    }

    void Update()
    {
        if (!base.IsOwner) return;

        // [L] tuşu: Balon tak veya yakındaki balonu pompalamaya başla
        if (Keyboard.current != null && Keyboard.current[attachKey].wasPressedThisFrame)
        {
            TryAttachOrPump();
        }
    }

    /// <summary>
    /// [L] basıldığında:
    /// 1) Eşya tutuyorsak veya bakıyorsak ve koşullar uygunsa → balon tak
    /// 2) Hedefte eşya yoksa ama yakınımızda pompalanabilir balon varsa → pompalamaya başla
    /// </summary>
    private void TryAttachOrPump()
    {
        GrabbableObject targetItem = null;

        if (playerGrabber != null)
        {
            // Önce tutulan eşya var mı kontrol edelim
            if (playerGrabber.HeldObject != null)
            {
                targetItem = playerGrabber.HeldObject;
            }
            // Tutulan eşya yoksa bakılan eşya var mı ona bakalım
            else if (playerGrabber.playerCamera != null)
            {
                Ray ray = playerGrabber.GetCrosshairRay();
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, playerGrabber.GetActualGrabRange(), playerGrabber.grabMask))
                {
                    targetItem = hit.collider.GetComponentInParent<GrabbableObject>();
                }
            }
        }

        // Durum 1: Bir eşyaya bakıyorsak veya tutuyorsak → balon takma dene
        if (targetItem != null)
        {
            // Koşullar:
            // - Eşya Medium veya MediumLarge mi? vs.
            if (!targetItem.CanAttachBag())
            {
                Debug.Log("[LiftingBagAttacher] Bu eşyaya balon takılamaz.");
                return;
            }

            if (movementStateManager == null || !movementStateManager.IsInWater)
            {
                Debug.Log("[LiftingBagAttacher] Balon takmak için su altında olmalısınız.");
                return;
            }

            if (BagCount.Value <= 0)
            {
                Debug.Log("[LiftingBagAttacher] Balon kalmadı!");
                return;
            }

            // Sunucuya balon takma isteği gönder
            NetworkObject targetNetObj = targetItem.GetComponent<NetworkObject>();
            if (targetNetObj != null)
            {
                ServerAttachBag(targetNetObj);
            }
            return;
        }

        // Durum 2: Eşya hedeflenmiyorsa → yakındaki pompalanabilir balonu bul ve mini-game başlat
        if (_miniGame != null && !_miniGame.IsActive)
        {
            LiftingBag nearby = FindNearbyPumpableBag();
            if (nearby != null)
            {
                _miniGame.StartPumping(nearby);
            }
        }
    }

    /// <summary>
    /// Yakında pompalanabilir (Inflating veya Paused state) balon var mı?
    /// Co-op pompalama için kullanılır.
    /// </summary>
    private LiftingBag FindNearbyPumpableBag()
    {
        // Basit yakınlık taraması - küçük yarıçapta OverlapSphere
        Collider[] hits = Physics.OverlapSphere(transform.position, 3f);
        LiftingBag closest = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            LiftingBag bag = hit.GetComponentInParent<LiftingBag>();
            if (bag == null) continue;

            LiftingBagState state = bag.State.Value;
            if (state != LiftingBagState.Inflating && state != LiftingBagState.Paused)
                continue;

            float dist = Vector3.Distance(transform.position, bag.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = bag;
            }
        }

        return closest;
    }

    // ==================== SERVER RPCs ====================

    /// <summary>
    /// Client → Server: Eşyaya kaldırma balonu tak.
    /// Sunucu doğrulama yapar, balonu spawn eder ve eşyaya bağlar.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void ServerAttachBag(NetworkObject itemNetObj)
    {
        if (itemNetObj == null) return;

        GrabbableObject grabbable = itemNetObj.GetComponent<GrabbableObject>();
        if (grabbable == null) return;

        // Sunucu tarafı doğrulama
        if (!grabbable.CanAttachBag()) return;
        if (BagCount.Value <= 0) return;

        // Su yüzeyi Y değerini bul
        WaterZone zone = WaterZone.GetZoneForPosition(grabbable.transform.position);
        float waterSurfaceY = zone != null ? zone.waterSurfaceY : 0f;

        // Balonu spawn et
        if (bagPrefab == null)
        {
            Debug.LogError("[LiftingBagAttacher] Balon prefabı atanmamış!");
            return;
        }

        // Spawn pozisyonunu eşyanın lokal koordinatlarında sadece yukarıya (Y) ayarlıyoruz.
        // LiftingBag.PositionOnItem kendi offset'ini kendisi hesaplayıp düzeltecek.
        Vector3 spawnPos = grabbable.transform.position + Vector3.up * 1f;

        NetworkObject bagNob = PoolManager.Instance.SpawnNetwork(
            bagPrefab,
            spawnPos,
            Quaternion.identity
        );

        if (bagNob == null) return;

        LiftingBag bag = bagNob.GetComponent<LiftingBag>();
        if (bag == null)
        {
            Debug.LogError("[LiftingBagAttacher] Balon prefabında LiftingBag bileşeni yok!");
            PoolManager.Instance.DespawnNetwork(bagNob);
            return;
        }

        // Balonu eşyaya bağla
        bag.ServerInitialize(grabbable, waterSurfaceY);

        // Envanterdeki balon sayısını düşür
        BagCount.Value--;

        Debug.Log($"[LiftingBagAttacher] Balon takıldı! Kalan: {BagCount.Value} | Eşya: {grabbable.itemName} ({grabbable.AttachedBagCount}/{grabbable.requiredBagCount})");

        // Takan oyuncuya mini-game başlatma bildirimi
        TargetStartMiniGame(base.Owner, bagNob);
    }

    /// <summary>
    /// Server → Owner Client: Mini-game başlat bildirimi.
    /// Balonu takan oyuncunun istemcisinde InflationMiniGame başlatır.
    /// </summary>
    [TargetRpc]
    private void TargetStartMiniGame(NetworkConnection conn, NetworkObject bagNetObj)
    {
        if (_miniGame == null) return;

        LiftingBag bag = bagNetObj.GetComponent<LiftingBag>();
        if (bag != null)
        {
            _miniGame.StartPumping(bag);
        }
    }
}
