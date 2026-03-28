using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Object;
using FishNet.Connection;
using System;

public class PlayerGrabber : NetworkBehaviour
{
    [Header("References")]
    public Transform playerCamera; // Karakterin ana kamerası (Raycast için)
    public Transform holdPoint; // Objenin tutulacağı spesifik nokta (Eğer TPS ise karakterin önünde bir boş obje oluşturup buraya atayın. FPS ise kameranın altı olabilir)

    [Header("Holding Settings")]
    public float grabRange = 4f;
    public float minHoldDistance = 1.5f;
    public float maxHoldDistance = 5f;
    public float scrollSpeed = 2f;
    public LayerMask grabMask = ~0; // Sadece nelerin tutulabileceğini/çarpışacağını belirler (Player'ı yoksaymak için gerekli)
    private float currentHoldDistance;

    [Header("Physics Settings")]
    public float springForce = 200f; // Objenin hedefe çekilme gücü
    public float damper = 15f; // Objenin titremesini önleyen sürtünme
    public float maxForce = 1500f; // Duvar arkasında kalırsa çok çekip buga sokmamak için maksimum güç limiti
    public bool reduceRotation = true; // Tutulan objenin dönüşünü yavaşlat / sabitle

    [Header("Belt Bag Integration")]
    public BeltBagInventory beltBagInventory;   // Inspector'dan atanır (aynı player üzerinde)
    [Tooltip("Eşya bu mesafenin altına indiğinde çantaya otomatik emilir")]
    public float bagCollectDistance = 1.0f;
    [Tooltip("Eşyanın çantaya uçma animasyonunun hedef noktası (BeltBagZone Transform)")]
    public Transform bagTargetPoint;
    [Tooltip("Çantaya emilme animasyon süresi (saniye)")]
    public float bagAnimationDuration = 0.4f;

    [Header("Hızlı Satış")]
    [Tooltip("Oyuncunun kişisel cüzdanı (aynı player üzerinde)")]
    public PlayerWallet playerWallet;
    [Tooltip("Hızlı satış tuşu (Hurda eşyalar)")]
    public Key quickSellKey = Key.Q;

    [Header("Drone")]
    [Tooltip("Drone'un spawn edileceği prefab (Networked ayarlarına sahip olmalı). Eğer atanmazsa drone spawn olmaz.")]
    public CarrierDrone dronePrefab;

    [Tooltip("Drone'a eşya yükleme tuşu")]
    public Key droneLoadKey = Key.G;
    [Tooltip("Drone çağırma tuşu")]
    public Key droneSummonKey = Key.T;
    [Tooltip("Drone'u gönderip eşyaları teslim etme tuşu")]
    public Key droneSendKey = Key.F;

    // Sadece bu oyuncunun drone referansı
    private CarrierDrone _myActiveDrone;

    private GrabbableObject heldObject;
    private Rigidbody heldRb;
    private bool originalUseGravity;

    // --- Public Erişimler (LiftingBagAttacher için) ---
    public GrabbableObject HeldObject => heldObject;
    public bool IsCollectingToBag => _isCollectingToBag;

    /// <summary>
    /// Eşya bırakıldığında tetiklenir (mini-game iptal tetikleyici).
    /// </summary>
    public event Action OnItemReleased;

    // Çantaya emilme animasyonu state
    private bool _isCollectingToBag;
    private float _collectTimer;
    private Vector3 _collectStartPos;
    private Vector3 _collectStartScale;

    public Ray GetCrosshairRay()
    {
        if (playerCamera == null) return new Ray(transform.position, transform.forward);
        
        Camera cam = playerCamera.GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) return new Ray(playerCamera.position, playerCamera.forward);
        
        Vector3 screenPos = new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        if (CrosshairManager.Instance != null)
        {
            screenPos = CrosshairManager.Instance.GetActiveCrosshairPosition();
        }
        return cam.ScreenPointToRay(screenPos);
    }

    public float GetActualGrabRange()
    {
        if (playerCamera == null) return grabRange;
        // TPS kamerasında kamera karakterin arkasında kaldığı için, atılan ışın hedefe ulaşmadan bitebilir.
        // Bu yüzden kameranın karaktere olan uzaklığını, objeyi tutma menziline ekliyoruz.
        return grabRange + Vector3.Distance(playerCamera.position, transform.position);
    }

    void Update()
    {
        // Benim objem değil ise çalışma
        if (!base.IsOwner) return;

        // --- ÇANTAYA EMİLME ANİMASYONU ---
        if (_isCollectingToBag)
        {
            UpdateBagAnimation();
            return; // Animasyon sırasında diğer input'lar bloklanır
        }

        // TEST İÇİN: Baktığımız yeri Scene (ve Gizmos açıksa Game) penceresinde çizgi olarak çizer
        if (playerCamera != null)
        {
            Ray ray = GetCrosshairRay();
            float actualRange = GetActualGrabRange();
            RaycastHit checkHit;
            
            if (Physics.Raycast(ray, out checkHit, actualRange, grabMask))
            {
                // Eğer tutabileceğimiz bir objeye (GrabbableObject) bakıyorsak Yeşil, değilse Kırmızı ışın çizer
                Color rayColor = checkHit.collider.GetComponent<GrabbableObject>() != null ? Color.green : Color.red;
                Debug.DrawRay(ray.origin, ray.direction * checkHit.distance, rayColor);
            }
            else
            {
                // Hiçbir engele çarpmıyorsa kırmızı ve max menzilde çizer
                Debug.DrawRay(ray.origin, ray.direction * actualRange, Color.red);
            }
        }

        // Etkileşim Tuşu - Yeni Input Sistemi ile (Sol tık)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (heldObject == null)
            {
                TryGrab();
            }
            else
            {
                Release();
            }
        }

        // --- HIZLI SATIŞ [Q] ---
        if (heldObject != null && Keyboard.current != null && Keyboard.current[quickSellKey].wasPressedThisFrame)
        {
            if (heldObject.rarityTier == RarityTier.Hurda)
            {
                QuickSell();
                return;
            }
        }

        // --- DRONE'A EŞYA YÜKLEME [G] ---
        if (heldObject != null && Keyboard.current != null && Keyboard.current[droneLoadKey].wasPressedThisFrame)
        {
            TryLoadIntoDrone();
        }

        // --- DRONE ÇAĞIRMA [T] ---
        if (Keyboard.current != null && Keyboard.current[droneSummonKey].wasPressedThisFrame)
        {
            TrySummonDrone();
        }

        // --- DRONE GÖNDERME [F] ---
        // (Oyuncu drone'a bakıyorsa ve F'ye basarsa)
        if (Keyboard.current != null && Keyboard.current[droneSendKey].wasPressedThisFrame)
        {
            TrySendDrone();
        }

        // Mouse scroll ile objeyi yakınlaştırıp uzaklaştırma (Yeni Input Sistemi)
        if (heldObject != null && Mouse.current != null)
        {
            float rawScroll = Mouse.current.scroll.y.ReadValue();
            if (Mathf.Abs(rawScroll) > 0.1f)
            {
                // Scroll yönünü normalize et (-1 veya +1) ve scrollSpeed ile çarp
                float scrollDir = Mathf.Sign(rawScroll);
                currentHoldDistance += scrollDir * scrollSpeed * Time.deltaTime * 5f;

                // Küçük eşyalar çantaya emilmek için daha yakına gelebilir
                float effectiveMinDistance = (heldObject.itemSize == ItemSize.Small) ?
                    bagCollectDistance * 0.5f : minHoldDistance;
                currentHoldDistance = Mathf.Clamp(currentHoldDistance, effectiveMinDistance, maxHoldDistance);
            }

            // R.E.P.O TARZI OTOMATİK TOPLAMA:
            // Küçük eşya yeterince yakınlaştırıldığında çantaya emilme animasyonu başlar
            if (heldObject.itemSize == ItemSize.Small
                && currentHoldDistance <= bagCollectDistance
                && beltBagInventory != null
                && beltBagInventory.CanAddItem(heldObject))
            {
                StartBagAnimation();
            }
        }
    }

    void FixedUpdate()
    {
        if (!base.IsOwner) return;

        // Animasyon sırasında spring fiziği uygulanmaz (obje direkt Transform ile hareket eder)
        if (_isCollectingToBag) return;

        if (heldRb != null)
        {
            // TPS Karakter kontrolcüsünde WASD'ye basınca karakter döner, bu da objenin karakterle birlikte sağa sola uçmasına sebep oluyordu.
            // Objeyi crosshair'in (kameranın merkezinin) gösterdiği hizada tutmak için, hedefini doğrudan kameranın ışını üzerinde belirliyoruz.
            // Böylece obje her zaman ekranın ortasını (crosshair'i) takip eder.
            Ray ray = GetCrosshairRay();
            float camToHoldPointDist = holdPoint != null ? Vector3.Distance(playerCamera.position, holdPoint.position) : 0f;
            Vector3 targetPosition = ray.origin + ray.direction * (camToHoldPointDist + currentHoldDistance);

            // Aradaki mesafe (Hata payı)
            Vector3 error = targetPosition - heldRb.position;

            // Eğer obje bir yere takılırsa ve çok fazla uzakta kalırsa elden düşsün
            if (error.magnitude > 4f)
            {
                Release();
                return;
            }

            // Hooke yasası (Spring - Damper sistemi)
            // Kuvvet = (Mesafe * Yay sabiti) - (Mevcut Hız * Sönümleyici)
            Vector3 force = (error * springForce) - (heldRb.linearVelocity * damper);

            // Gücü limitliyoruz
            if (force.magnitude > maxForce)
            {
                force = force.normalized * maxForce;
            }

            // Kütleye göre kuvveti hesapla ki ağır cisimler saçmalamasın
            force *= heldRb.mass;

            // Eşyayı hedef noktaya doğru it/çek
            heldRb.AddForce(force, ForceMode.Force);

            // Cisim tutulurken kendi etrafında deli gibi dönmesini engellemek için dönüş hızını yavaşlatıyoruz
            if (reduceRotation)
            {
                heldRb.angularVelocity = Vector3.Lerp(heldRb.angularVelocity, Vector3.zero, Time.fixedDeltaTime * 10f);
            }
        }
    }

    void TryGrab()
    {
        Ray ray = GetCrosshairRay();
        float actualRange = GetActualGrabRange();
        
        // RaycastAll ile ışın yolundaki tüm objeleri al (sepetin arkasındaki/içindeki eşyaları da yakala)
        RaycastHit[] hits = Physics.RaycastAll(ray, actualRange, grabMask);
        
        // Mesafeye göre sırala (en yakından en uzağa)
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        
        GrabbableObject grabbable = null;
        foreach (var hit in hits)
        {
            GrabbableObject candidate = hit.collider.GetComponentInParent<GrabbableObject>();
            if (candidate != null && !candidate.isHeavyVehicle)
            {
                grabbable = candidate;
                break;
            }
        }
        
        if (grabbable != null)
        {
            if (grabbable.AttachedBagCount > 0)
            {
                Debug.Log("Bu eşyaya balon takılı, taşınamaz.");
                return;
            }

            heldObject = grabbable;
            heldRb = grabbable.GetComponent<Rigidbody>();
            
            // Objenin sahipliğini sunucudan üzerimize alıyoruz
            NetworkObject netObj = heldObject.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                ServerTakeOwnership(netObj);
            }

            // Objenin tutulma mesafesi başlangıcını, referans noktasına (holdPoint veya kamera) göre ayarla
            Vector3 referencePos = holdPoint != null ? holdPoint.position : playerCamera.position;
            currentHoldDistance = Vector3.Distance(referencePos, heldRb.position);
            currentHoldDistance = Mathf.Clamp(currentHoldDistance, minHoldDistance, maxHoldDistance);

            // Fizik ayarlarını geçici olarak tutuş için uygun hale getir
            originalUseGravity = heldRb.useGravity;
            heldRb.useGravity = false; // Tutarken yerçekimini kapat ki aşağı çekmesin, yay gibi dengede kalsın
            heldRb.interpolation = RigidbodyInterpolation.Interpolate; // Kamerada titremeden gözükmesi için
        }
    }

    /// <summary>
    /// Çantaya emilme animasyonunu başlatır.
    /// Eşya fizikten koparılır ve Transform ile çantaya doğru hareket ettirilir.
    /// </summary>
    void StartBagAnimation()
    {
        if (heldObject == null || heldRb == null) return;

        _isCollectingToBag = true;
        _collectTimer = 0f;
        _collectStartPos = heldObject.transform.position;
        _collectStartScale = heldObject.transform.localScale;

        // Animasyon sırasında fiziği devre dışı bırak (Transform ile hareket ettireceğiz)
        heldRb.isKinematic = true;
        heldRb.linearVelocity = Vector3.zero;
        heldRb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Her frame çağrılır: eşyayı çantaya doğru uçurur ve küçültür.
    /// Animasyon bitince CollectIntoBag() ile despawn eder.
    /// </summary>
    void UpdateBagAnimation()
    {
        if (heldObject == null)
        {
            // Obje bir şekilde kaybolmuşsa animasyonu iptal et
            _isCollectingToBag = false;
            heldRb = null;
            return;
        }

        _collectTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_collectTimer / bagAnimationDuration);

        // Ease-in: başta yavaş, sona doğru hızlanarak çantaya gider
        float easedT = t * t;

        // Hedef pozisyon: BeltBagZone (çanta noktası) veya fallback olarak oyuncunun pozisyonu
        Vector3 targetPos = bagTargetPoint != null ? bagTargetPoint.position : transform.position;

        // Eşyayı çantaya doğru uçur
        heldObject.transform.position = Vector3.Lerp(_collectStartPos, targetPos, easedT);

        // Eşyayı küçült (çantanın içine giriyormuş etkisi)
        heldObject.transform.localScale = Vector3.Lerp(_collectStartScale, Vector3.zero, easedT);

        // Animasyon tamamlandı
        if (t >= 1f)
        {
            _isCollectingToBag = false;
            // Scale'i geri yükle (despawn öncesi ağ senkronizasyonu için)
            heldObject.transform.localScale = _collectStartScale;
            CollectIntoBag();
        }
    }

    /// <summary>
    /// Küçük eşyayı çantaya otomatik topla (R.E.P.O tarzı).
    /// Animasyon bittikten sonra veya direkt olarak çağrılır.
    /// </summary>
    void CollectIntoBag()
    {
        if (heldObject == null || beltBagInventory == null) return;

        NetworkObject netObj = heldObject.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            beltBagInventory.ServerCollectItem(netObj);
        }

        heldObject = null;
        heldRb = null;
    }

    /// <summary>
    /// Hurda eşyayı anında sat. [Q] tuşuyla tetiklenir.
    /// Eşya despawn olur, para kişisel cüzdana eklenir.
    /// </summary>
    void QuickSell()
    {
        if (heldObject == null) return;

        NetworkObject netObj = heldObject.GetComponent<NetworkObject>();
        if (netObj == null) return;

        // Yerçekimini geri yükle (despawn öncesi temizlik)
        if (heldRb != null)
            heldRb.useGravity = originalUseGravity;

        // Sunucuya hızlı satış isteği gönder
        ServerQuickSell(netObj);

        // Client tarafı referansları temizle
        heldObject = null;
        heldRb = null;
    }

    /// <summary>
    /// Oyuncunun baktığı veya yatay olarak en yakın olduğu (belirli mesafe içinde) drone'u bulur.
    /// </summary>
    private CarrierDrone FindTargetDroneForInteraction(float maxDistance)
    {
        // 1. Önce kameranın merkezinden raycast atarak bir drone'a bakıp bakmadığımızı kontrol edelim
        Ray ray = GetCrosshairRay();
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, maxDistance))
        {
            CarrierDrone lookedDrone = hit.collider.GetComponentInParent<CarrierDrone>();
            if (lookedDrone != null)
            {
                return lookedDrone;
            }
        }

        // 2. Eğer bir drone'a bakmıyorsak, etrafımızdaki tüm drone'lar arasından en yakın olanı bulalım
        CarrierDrone targetDrone = null;
        float closestDist = maxDistance;
        foreach (var drone in CarrierDrone.ActiveDrones)
        {
            if (drone == null || !drone.IsSpawned) continue;
            
            float xzDist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(drone.transform.position.x, drone.transform.position.z));
                
            if (xzDist < closestDist)
            {
                closestDist = xzDist;
                targetDrone = drone;
            }
        }

        return targetDrone;
    }

    /// <summary>
    /// Tutulan eşyayı yakındaki drone'a yüklemeyi dener. [G] tuşuyla tetiklenir.
    /// </summary>
    void TryLoadIntoDrone()
    {
        if (heldObject == null) return;

        CarrierDrone targetDrone = FindTargetDroneForInteraction(20f);

        if (targetDrone == null)
        {
            Debug.Log("[PlayerGrabber] Yakında veya baktığınız yönde etkileşime girilecek bir drone bulunamadı!");
            return;
        }

        // Drone yuklenebilir durumda mi?
        DroneState state = targetDrone.State.Value;
        if (state != DroneState.Hovering && state != DroneState.Loading)
        {
            Debug.Log("[PlayerGrabber] Seçilen drone su an esya kabul edemiyor.");
            targetDrone.ServerPlayErrorSound();
            return;
        }

        // Client-side ön kontrol (ağırlık)
        if (targetDrone.CurrentWeight.Value + heldObject.weight > targetDrone.maxCargoWeight)
        {
            Debug.Log("[PlayerGrabber] Drone kargo kapasitesi dolu!");
            targetDrone.ServerPlayErrorSound();
            return;
        }

        // Client-side ön kontrol (slot)
        if (targetDrone.CargoItems.Count >= targetDrone.maxCargoSlots)
        {
            Debug.Log("[PlayerGrabber] Drone'da bos slot yok!");
            targetDrone.ServerPlayErrorSound();
            return;
        }

        NetworkObject itemNetObj = heldObject.GetComponent<NetworkObject>();
        if (itemNetObj == null) return;

        // Yerçekimini geri yükle (despawn öncesi temizlik)
        if (heldRb != null)
            heldRb.useGravity = originalUseGravity;

        // Sunucuya yükleme isteği gönder (drone üzerinden)
        targetDrone.ServerLoadItem(itemNetObj);

        // Client tarafı referansları temizle
        heldObject = null;
        heldRb = null;
    }

    /// Client tarafı: Drone çağırma veya çağırma isteği.
    /// </summary>
    void TrySummonDrone()
    {
        if (_myActiveDrone != null && _myActiveDrone.IsSpawned)
        {
            Debug.Log("[PlayerGrabber] Zaten aktif bir dronunuz var!");
            return;
        }

        if (dronePrefab == null)
        {
            Debug.LogError("[PlayerGrabber] Drone Prefab atanmamış! Lütfen inspector üzerinden PlayerGrabber'a dronePrefab atayın.");
            return;
        }

        // Sunucuya yeni bir drone oluşturmasını veya mevcut onesi getirmesini söyle
        ServerSpawnAndSummonDrone(transform.position);
    }

    /// <summary>
    /// F'ye basıldığında bakılan veya en yakındaki Drone'un bize ait olup olmadığını kontrol edip gidiş emri verir.
    /// </summary>
    void TrySendDrone()
    {
        CarrierDrone targetDrone = FindTargetDroneForInteraction(15f);

        if (targetDrone == null)
        {
            Debug.Log("[PlayerGrabber] Etrafta gönderilecek bir drone bulunamadı!");
            return;
        }

        if (targetDrone.OwnerClientId.Value != base.Owner.ClientId)
        {
            Debug.Log("[PlayerGrabber] HATA: Göndermeye çalıştığınız drone başkasına ait!");
            return;
        }

        // Gönder
        targetDrone.ServerSendAway();
    }

    [ServerRpc]
    private void ServerSpawnAndSummonDrone(Vector3 callerPosition)
    {
        // 1. Zaten bir drone varsa tekrar spawn etme
        if (_myActiveDrone != null && _myActiveDrone.IsSpawned)
        {
            return;
        }

        // 2. Suyun neresinde olduğunu kontrol et
        WaterZone zone = WaterZone.GetZoneForPosition(callerPosition);
        if (zone == null)
        {
            TargetNotifyLoadFailed(base.Owner, "Drone'u sadece su içindeyken çağırabilirsiniz!");
            return;
        }

        // 3. Drone'u yüksekte instatiate/pool et
        float dropHeight = zone.waterSurfaceY + 50f;
        Vector3 spawnPos = new Vector3(callerPosition.x, dropHeight, callerPosition.z);
        
        NetworkObject droneNetObj = base.NetworkManager.GetPooledInstantiated(dronePrefab.gameObject, spawnPos, Quaternion.identity, asServer: true);
        
        // 4. FishNet ServerManager ile Spawn et (Sahiplik Server'da kalmali, boylece interpolasyon calisir)
        base.ServerManager.Spawn(droneNetObj);

        // 5. Kurulum yap
        CarrierDrone spawnedDrone = droneNetObj.GetComponent<CarrierDrone>();
        spawnedDrone.ServerInitAndSummon(this, zone.waterSurfaceY);
        
        // Bu sunucu objesini kendimizin olarak işaretleyelim (Gerekirse ClientRPC de atılabilir)
        _myActiveDrone = spawnedDrone;
        SetMyActiveDroneClientRpc(droneNetObj);
    }

    [ObserversRpc(BufferLast = true)]
    private void SetMyActiveDroneClientRpc(NetworkObject droneNetObj)
    {
        if (droneNetObj != null)
            _myActiveDrone = droneNetObj.GetComponent<CarrierDrone>();
    }

    [TargetRpc]
    private void TargetNotifyLoadFailed(NetworkConnection conn, string message)
    {
        Debug.Log("[PlayerGrabber] Drone Hatasi: " + message);
        // Eger projede uygun bir UI yoksa sadece loglayalim veya baska bir UI bulursaniz ekleyebilirsiniz
        // CargoNotificationUI sildik burdan
    }

    /// <summary>
    /// Sunucu tarafı: Hızlı satış doğrulama, para ekleme ve despawn.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void ServerQuickSell(NetworkObject itemNetObj)
    {
        if (itemNetObj == null) return;

        GrabbableObject grabbable = itemNetObj.GetComponent<GrabbableObject>();
        if (grabbable == null) return;

        // Sadece Hurda eşyalar hızlı satılabilir
        if (grabbable.rarityTier != RarityTier.Hurda) return;

        float sellPrice = grabbable.currentPrice;
        string itemName = grabbable.itemName;

        // Kişisel cüzdana para ekle
        if (playerWallet != null)
            playerWallet.AddMoney(sellPrice);

        // Ortak kasaya da ekle
        if (TeamTreasury.Instance != null)
            TeamTreasury.Instance.AddToTreasury(sellPrice);

        Debug.Log($"[QuickSell] '{itemName}' hızlı satıldı! +{sellPrice:F0}₺");

        // Satış bildirimini oyuncuya gönder
        if (playerWallet != null)
            playerWallet.TargetNotifyQuickSell(base.Owner, sellPrice, itemName);

        // Eşyayı ağdan despawn et
        base.ServerManager.Despawn(itemNetObj);
    }

    void Release()
    {
        if (heldObject == null)
        {
            heldRb = null;
            return;
        }

        // Eşya bırakıldığını bildir (LiftingBagAttacher mini-game iptali için)
        OnItemReleased?.Invoke();

        // --- KEMER ÇANTASI TOPLAMA KONTROLÜ ---
        // Eşya küçük mü VE yeterince yakınsa çantaya emilme animasyonu başlat
        bool collected = false;
        if (heldObject.itemSize == ItemSize.Small
            && beltBagInventory != null
            && currentHoldDistance <= bagCollectDistance
            && beltBagInventory.CanAddItem(heldObject))
        {
            StartBagAnimation();
            collected = true;
        }

        // --- NORMAL BIRAKMA (mevcut davranış) ---
        // Eğer çantaya toplanmadıysa, eski bırakma mantığını çalıştır
        if (!collected)
        {
            if (heldRb != null)
            {
                // Yerçekimini eski haline döndür
                heldRb.useGravity = originalUseGravity;
            }

            // Objenin sahipliğini bırak (Sunucuya geri ver)
            if (heldObject != null)
            {
                NetworkObject netObj = heldObject.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    ServerRemoveOwnership(netObj);
                }
            }

            heldObject = null;
            heldRb = null;
        }
    }

    // --- Ağ Üzerinden Sahiplik (Ownership) Değiştirme ---

    [ServerRpc(RequireOwnership = true)]
    private void ServerTakeOwnership(NetworkObject targetNetObj)
    {
        // Hedef obje varsa ve hali hazırda bize ait değilse
        if (targetNetObj != null && targetNetObj.Owner != base.Owner)
        {
            // O objenin kontrolünü bu oyuncuya (Rpc'yi çağıran Owner'a) ver
            targetNetObj.GiveOwnership(base.Owner);
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void ServerRemoveOwnership(NetworkObject targetNetObj)
    {
        // Hedef objenin sahibi hala bizim oyuncumuzsa, sahipliği kaldır (Sunucuya geri döner)
        if (targetNetObj != null && targetNetObj.Owner == base.Owner)
        {
            targetNetObj.RemoveOwnership();
        }
    }}
