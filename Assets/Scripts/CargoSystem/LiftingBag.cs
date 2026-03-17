using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using System.Collections.Generic;

/// <summary>
/// Kaldırma balonu prefabına eklenir (NetworkBehaviour).
/// Eşyaya takılır, pompalanır, şişince eşyayı su yüzeyine kaldırır.
/// Co-op: Birden fazla oyuncu aynı balonu pompalayabilir (RequireOwnership = false).
///
/// Prefab kurulumu:
/// - NetworkObject + LiftingBag (NetworkTransform KULLANILMAZ, FollowTarget ile takip edilir)
/// - Child "BagMesh": 3D balon modeli (SkinnedMeshRenderer + BlendShape)
/// </summary>
public class LiftingBag : NetworkBehaviour
{
    [Header("Şişirme Ayarları")]
    [Tooltip("Her pompa basışında eklenen şişirme miktarı (0-1 arası)")]
    public float pumpAmountPerPress = 0.02f;

    [Tooltip("Pompalama durunca sönme hızı (birim/saniye)")]
    public float deflationRate = 0.02f;

    [Tooltip("Takılma animasyonu süresi (saniye)")]
    public float attachDuration = 0.5f;

    [Header("Fizik Ayarları")]
    [Tooltip("Tam şişmiş balonun maksimum kaldırma kuvveti (Newton)")]
    public float maxLiftForce = 80f;

    [Tooltip("Su direnci katsayısı (yükseldikçe yavaşlatma)")]
    public float waterDragCoefficient = 0.5f;

    [Header("Boyut Ayarları")]
    [Tooltip("Sönmüş balon ölçeği")]
    public Vector3 deflatedScale = new Vector3(0.1f, 0.1f, 0.1f);

    [Tooltip("Tam şişmiş balon ölçeği")]
    public Vector3 inflatedScale = new Vector3(1f, 1.5f, 1f);

    [Header("Pompalama Mesafesi")]
    [Tooltip("Oyuncunun balonu pompalamak için olması gereken max mesafe")]
    public float pumpRange = 3f;

    [Tooltip("Oyuncu başına saniyedeki maksimum pompa sayısı (anti-cheat)")]
    public int maxPumpsPerSecond = 8;

    // --- SyncVar'lar (Server → Tüm Clientlar) ---
    public readonly SyncVar<float> InflationLevel = new SyncVar<float>();
    public readonly SyncVar<LiftingBagState> State = new SyncVar<LiftingBagState>();
    public readonly SyncVar<int> ActivePumperCount = new SyncVar<int>();
    public readonly SyncVar<NetworkObject> SyncedTargetNetObj = new SyncVar<NetworkObject>(); // Hedef eşya (tüm clientlara sync)
    public readonly SyncVar<Vector3> SyncedFollowOffset = new SyncVar<Vector3>(); // Takip offseti (tüm clientlara sync)

    // --- Takılı olduğu eşya ---
    private GrabbableObject _targetItem;
    private Rigidbody _targetRb;
    private float _waterSurfaceY;
    private float _attachTimer;
    private float _ascendTimer;
    private Vector3 _followOffset;

    // --- Balon mesh (BlendShape animasyonu) ---
    [Header("Referanslar")]
    [Tooltip("Balon SkinnedMeshRenderer (BlendShape şişirme için)")]
    public SkinnedMeshRenderer bagRenderer;

    [Tooltip("BlendShape index numarası (genelde 0 = ilk shape key)")]
    public int blendShapeIndex = 0;

    // --- Co-op pompalama: Sunucu tarafı rate-limit ---
    private Dictionary<int, float> _lastPumpTimes = new Dictionary<int, float>(); // ClientId → son pompa zamanı
    private Dictionary<int, int> _pumpCounts = new Dictionary<int, int>();         // ClientId → bu saniyedeki basış
    private Dictionary<int, float> _pumpWindowStart = new Dictionary<int, float>(); // ClientId → sayaç başlangıcı
    private HashSet<int> _recentPumpers = new HashSet<int>(); // Son 0.5s içinde pompalayan oyuncular
    private float _pumperCheckTimer;

    // --- Public Erişimler ---
    public GrabbableObject TargetItem => _targetItem;
    public float WaterSurfaceY => _waterSurfaceY;

    private void Awake()
    {
        // Balon collider'ını Trigger yap → fizik çakışması olmaz (eşyayı itmez/titretmez)
        // ama Physics.OverlapSphere ile hala algılanır (co-op pompalama için gerekli)
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    /// <summary>
    /// Sunucu tarafı: Balonu eşyaya takar ve başlangıç state'ini ayarlar.
    /// LiftingBagAttacher.ServerAttachBag() tarafından çağrılır.
    /// </summary>
    [Server]
    public void ServerInitialize(GrabbableObject item, float waterSurfaceY)
    {
        _targetItem = item;
        _targetRb = item.Rb;
        _waterSurfaceY = waterSurfaceY;

        InflationLevel.Value = 0f;
        State.Value = LiftingBagState.Attaching;
        ActivePumperCount.Value = 0;
        _attachTimer = 0f;
        _ascendTimer = 0f;

        // Hedef eşyayı tüm clientlara senkronize et
        NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
        SyncedTargetNetObj.Value = itemNetObj;

        // Eşyaya bildir
        item.OnBagAttached(this);

        // Balonu eşyanın üstüne konumla
        PositionOnItem();

        // Offset'i tüm clientlara senkronize et
        SyncedFollowOffset.Value = _followOffset;

        // Tüm clientlara hedef bilgisini HEMEN gönder (SyncVar gecikmesini beklemeden)
        ObserversSetTarget(itemNetObj, _followOffset);
    }

    /// <summary>
    /// Tüm clientlarda hedef eşyayı ve takip offset'ini ayarlar.
    /// SyncVar senkronizasyonundan daha hızlı çalışır (RPC anında gider).
    /// </summary>
    [ObserversRpc]
    private void ObserversSetTarget(NetworkObject targetNetObj, Vector3 offset)
    {
        // Sunucu zaten ServerInitialize'da ayarladı, tekrar ayarlamaya gerek yok
        if (base.IsServerInitialized) return;

        if (targetNetObj != null)
        {
            _targetItem = targetNetObj.GetComponent<GrabbableObject>();
            _targetRb = _targetItem != null ? _targetItem.Rb : null;
            _followOffset = offset;

            // Hemen doğru pozisyona konumla (spawn pozisyonunda beklemesin)
            if (_targetItem != null)
            {
                transform.position = _targetItem.transform.TransformPoint(_followOffset);
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// Balonu eşyanın üstüne konumlandırır.
    /// Çoklu balon durumunda offsetleme yapılır.
    /// </summary>
    private void PositionOnItem()
    {
        if (_targetItem == null) return;

        // Orijinal objenin bounding box'unu bularak, objenin "Dünya(World)" merkezini ve en üst noktasını tespit edelim
        Collider col = _targetItem.GetComponentInChildren<Collider>();
        Vector3 worldTopPoint;

        if (col != null)
        {
            // Kutunun fiziksel dünyadaki tam orta noktası + kutunun yüksekliğinin yarısı = KUTUNUN TAM TEPESİ (Dünya koordinatlarında)
            worldTopPoint = col.bounds.center + (Vector3.up * col.bounds.extents.y);
        }
        else
        {
            worldTopPoint = _targetItem.transform.position + Vector3.up * 1f;
        }

        // Balonun balon model pivotuna da bağlı olarak havada durması gereken minik ekstra boşluk
        worldTopPoint += Vector3.up * 0.4f;

        // Çoklu balon durumunda yatay offset ekle (Dünya koordinatında sağa/sola)
        int bagIndex = _targetItem.AttachedBagCount - 1;
        float xOffset = bagIndex * 0.6f - (_targetItem.requiredBagCount - 1) * 0.3f;
        worldTopPoint += new Vector3(xOffset, 0f, 0f);

        // ŞİMDİ asıl kritik yer: Bulduğumuz bu DÜNYA noktasını, eşyanın LOKAL eksenine geri çevirmeliyiz.
        // Böylece eşya fizik motoruyla yuvarlandığında balon da o "göreceli" lokal noktada yapışık kalmaya devam eder.
        _followOffset = _targetItem.transform.InverseTransformPoint(worldTopPoint);

        // Başlangıç pozisyonunu ata
        transform.position = _targetItem.transform.TransformPoint(_followOffset);
        // NOT: Model dosyası import edilirken eksenleri dönük geldiyse, onu düzeltmek için rotasyon ekleyelim.
        // Orijinal model X ekseni etrafında 90 veya -90 dönük olabilir. Bu yüzden dik durmasını sağlayacak sabit bir rotasyon veriyoruz:
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void Update()
    {
        // Tüm client'larda balon mesh ölçeğini güncelle (SyncVar'a göre)
        UpdateVisualScale();

        // Eşyayı takip et (parenting yerine her frame pozisyon güncelle)
        FollowTarget();
    }

    /// <summary>
    /// Balon eşyanın üstünde kalır (parenting yok, SyncVar ile tüm clientlarda çalışır).
    /// Server: _targetItem doğrudan atanır.
    /// Client: SyncedTargetNetObj SyncVar'ından çözümlenir.
    /// </summary>
    private void FollowTarget()
    {
        // Client tarafı: SyncVar'dan hedef eşyayı çözümle (henüz atanmamışsa)
        if (_targetItem == null && SyncedTargetNetObj.Value != null)
        {
            _targetItem = SyncedTargetNetObj.Value.GetComponent<GrabbableObject>();
            _targetRb = _targetItem != null ? _targetItem.Rb : null;
            _followOffset = SyncedFollowOffset.Value;
        }

        if (_targetItem == null) return;
        if (State.Value == LiftingBagState.Detached) return;

        // Balonu objenin dönüşüne göre konumlandır ama kendisini her zaman YUKARI doğru döndür!
        // Eğer kanca yukarı bakıyorsa bu değeri tersine (-90 yerine 90 veya tam tersi) çevirmemiz gerekir.
        
        // --- Sabit Su Altı Balon Yalpalama (Visual Sway) ---
        // Balon eşyaya takılıyken de su akıntısıyla hafif sağa sola yalpalasın
        // Ortası bulunarak tatlı bir seviyeye getirildi
        float swayOffsetX = Mathf.Sin(Time.time * 1.1f) * 0.030f;
        float swayOffsetZ = Mathf.Cos(Time.time * 0.9f) * 0.030f;
        
        Vector3 basePosition = _targetItem.transform.TransformPoint(_followOffset);
        transform.position = basePosition + new Vector3(swayOffsetX, 0f, swayOffsetZ);

        // Kancanın aşağı bakması için X eksenini 90 olarak güncelliyoruz.
        // Rotasyonda da çok hafif bir salınım yapalım
        float swayRotX = Mathf.Sin(Time.time * 1.4f) * 1.9f;
        float swayRotZ = Mathf.Cos(Time.time * 1.1f) * 1.9f;
        transform.rotation = Quaternion.Euler(90f + swayRotX, 0f, swayRotZ);
    }

    void FixedUpdate()
    {
        if (!base.IsServerInitialized) return;

        switch (State.Value)
        {
            case LiftingBagState.Attaching:
                UpdateAttaching();
                break;
            case LiftingBagState.Inflating:
                UpdateInflating();
                break;
            case LiftingBagState.Paused:
                UpdatePaused();
                break;
            case LiftingBagState.Inflated:
                UpdateInflated();
                break;
            case LiftingBagState.Floating:
                UpdateFloating();
                break;
        }

        // Aktif pompalayıcı sayısını güncelle (her 0.5 saniyede)
        _pumperCheckTimer += Time.fixedDeltaTime;
        if (_pumperCheckTimer >= 0.5f)
        {
            ActivePumperCount.Value = _recentPumpers.Count;
            _recentPumpers.Clear();
            _pumperCheckTimer = 0f;
        }
    }

    // ==================== STATE HANDLERS (Server Only) ====================

    private void UpdateAttaching()
    {
        _attachTimer += Time.fixedDeltaTime;
        if (_attachTimer >= attachDuration)
        {
            State.Value = LiftingBagState.Inflating;
        }
    }

    private void UpdateInflating()
    {
        // Balon biraz şişmişse kaldırma kuvveti uygula
        if (InflationLevel.Value > 0.05f)
        {
            ApplyBuoyancy();
        }

        // Eğer eşya zaten yerden kesilmiş ve yukarı doğru çıkıyorsa (yeterli kuvvete ulaşmışsa),
        // şişirmeyi durdur ve direkt Inflated state'ine geçirerek minigame'i bitir.
        if (_targetRb != null && _targetRb.linearVelocity.y > 0.5f)
        {
            if (_targetItem != null && _targetItem.HasEnoughBags)
            {
                State.Value = LiftingBagState.Inflated;
                return;
            }
        }

        // Aktif pompalayan yoksa Paused'a geç
        if (ActivePumperCount.Value == 0 && _recentPumpers.Count == 0)
        {
            State.Value = LiftingBagState.Paused;
            return;
        }

        CheckSurface();
    }

    private void UpdatePaused()
    {
        // Yavaşça sön
        InflationLevel.Value = Mathf.Max(0f, InflationLevel.Value - deflationRate * Time.fixedDeltaTime);

        // Hala kısmen şişse kaldırma kuvveti uygula
        if (InflationLevel.Value > 0f)
        {
            ApplyBuoyancy();
        }

        CheckSurface();
    }

    private void UpdateInflated()
    {
        ApplyBuoyancy();

        // 6 saniye boyunca yukarı çıktıktan sonra direkt yüzeye ulaşmış gibi davran (yok olup gemiye gitmesi için)
        _ascendTimer += Time.fixedDeltaTime;
        if (_ascendTimer >= 6f)
        {
            State.Value = LiftingBagState.Floating;
            if (_targetRb != null) _targetRb.linearVelocity *= 0.3f;
            NotifyShipCollector();
        }
        else
        {
            CheckSurface();
        }
    }

    private void UpdateFloating()
    {
        if (_targetRb == null) return;

        // Yüzeyde tut (hafif yukarı kuvvet + yüzeye sabitle)
        float currentY = _targetRb.position.y;
        if (currentY < _waterSurfaceY)
        {
            _targetRb.AddForce(Vector3.up * maxLiftForce * 0.3f, ForceMode.Force);
        }
        else
        {
            // Yüzeyin üstüne çıktıysa hızı yavaşlat
            _targetRb.linearVelocity *= 0.95f;
        }
    }

    /// <summary>
    /// Kaldırma kuvvetini uygula (inflationLevel oranında).
    /// Kuvvet eşyanın ağırlığıyla orantılı hesaplanır (hafif eşya = roket gibi fırlamaz).
    /// </summary>
    private void ApplyBuoyancy()
    {
        if (_targetRb == null || _targetItem == null) return;

        // Eşyanın ağırlık kuvveti (yerçekimi): F = m * g
        float gravityForce = _targetRb.mass * Physics.gravity.magnitude;

        // Kaldırma kuvveti = yerçekimini yenecek + küçük fazlalık (yavaş yükselme)
        // inflationLevel %100 olduğunda yerçekiminin 1.3 katı kuvvet uygular
        float buoyancyForce = InflationLevel.Value * gravityForce * 1.3f;
        _targetRb.AddForce(Vector3.up * buoyancyForce, ForceMode.Force);

        // Su direnci (hız arttıkça direnç kuvvetlenir → sabit hızda yükselir)
        float dragMultiplier = waterDragCoefficient * _targetRb.mass;
        Vector3 dragForce = -_targetRb.linearVelocity * dragMultiplier;
        _targetRb.AddForce(dragForce, ForceMode.Force);

        // Maksimum yükselme hızını sınırla (2 m/s)
        if (_targetRb.linearVelocity.y > 2f)
        {
            Vector3 vel = _targetRb.linearVelocity;
            vel.y = 2f;
            _targetRb.linearVelocity = vel;
        }

        // --- Su Altı Yalpalama (Wobble) Efekti ---
        // Sadece kayda değer bir şekilde yukarı çıkıyorsa sallanma başlasın
        if (_targetRb.linearVelocity.y > 0.1f)
        {
            float time = Time.time;
            float mass = _targetRb.mass;
            
            // X ve Z eksenlerinde farklı hızlarda (kaotik ve doğal hissettirmesi için) sallanma kuvveti oluştur
            float swayForceX = Mathf.Sin(time * 2.5f) * mass * 1.2f;
            float swayForceZ = Mathf.Cos(time * 1.8f) * mass * 1.2f;

            // Objeye sağa sola yumuşak itiş gücü ver
            _targetRb.AddForce(new Vector3(swayForceX, 0f, swayForceZ), ForceMode.Force);
        }
    }

    /// <summary>
    /// Eşya su yüzeyine ulaştı mı kontrol et.
    /// Balon en az %50 şişmiş olmalı ve eşya gerçekten yüzeyde olmalı.
    /// </summary>
    private void CheckSurface()
    {
        if (_targetRb == null) return;

        // Balon yeterince şişmemişse yüzey kontrolü yapma
        if (InflationLevel.Value < 0.5f) return;

        // WaterZone bulunamadıysa (_waterSurfaceY == 0) runtime'da tekrar ara
        if (_waterSurfaceY == 0f)
        {
            WaterZone zone = WaterZone.GetZoneForPosition(_targetRb.position);
            if (zone != null)
                _waterSurfaceY = zone.waterSurfaceY;
            else
                return; // WaterZone hala bulunamıyorsa kontrol etme
        }

        if (_targetRb.position.y >= _waterSurfaceY)
        {
            // Yüzeye ulaştı
            State.Value = LiftingBagState.Floating;

            // Hızı yavaşlat
            _targetRb.linearVelocity *= 0.3f;

            // ShipCollector'a bildir (varsa)
            NotifyShipCollector();
        }
    }

    /// <summary>
    /// Yüzeye ulaşan eşya için en yakın ShipCollector'ı bul ve bildir.
    /// </summary>
    private void NotifyShipCollector()
    {
        if (_targetItem == null) return;

        // ShipCollector sahnede bulunur ve eşyayı toplar
        ShipCollector collector = ShipCollector.FindNearest(_targetItem.transform.position);
        if (collector != null)
        {
            collector.ServerCollectFloatingItem(_targetItem);
        }
    }

    // ==================== CO-OP POMPALAMA ====================

    /// <summary>
    /// Herhangi bir oyuncu tarafından çağrılabilir (RequireOwnership = false).
    /// Sunucu mesafe ve rate-limit kontrolü yapar.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ServerPumpBag(NetworkConnection sender = null)
    {
        if (State.Value != LiftingBagState.Inflating && State.Value != LiftingBagState.Paused)
            return;

        // "yükselirken şişirmeye basarsam şişmeye devam ediyor orda şişmemesi lazım"
        // Eğer obje havaya kalkmaya başladıysa pompa işlemini yoksay ve state'i kilitle.
        if (_targetRb != null && _targetRb.linearVelocity.y > 0.2f)
        {
            if (_targetItem != null && _targetItem.HasEnoughBags)
            {
                State.Value = LiftingBagState.Inflated;
            }
            return;
        }

        if (sender == null) return;
        int clientId = sender.ClientId;

        // Rate-limit kontrolü
        float now = Time.time;
        if (!_pumpWindowStart.ContainsKey(clientId))
        {
            _pumpWindowStart[clientId] = now;
            _pumpCounts[clientId] = 0;
        }

        // 1 saniyelik pencere sıfırlama
        if (now - _pumpWindowStart[clientId] >= 1f)
        {
            _pumpWindowStart[clientId] = now;
            _pumpCounts[clientId] = 0;
        }

        // Max basış kontrolü
        if (_pumpCounts[clientId] >= maxPumpsPerSecond) return;

        // Mesafe kontrolü: Pompacı oyuncu eşyaya yeterince yakın mı?
        // Uzaktan takma işlemi eklendiği için range limitini 55f'e çıkardık.
        NetworkConnection conn = sender;
        if (conn.FirstObject != null)
        {
            Vector3 checkPos = _targetItem != null ? _targetItem.transform.position : transform.position;
            float dist = Vector3.Distance(conn.FirstObject.transform.position, checkPos);
            if (dist > 55f) return;
        }

        // Pompayı uygula
        _pumpCounts[clientId]++;
        _recentPumpers.Add(clientId);
        InflationLevel.Value = Mathf.Min(1f, InflationLevel.Value + pumpAmountPerPress);

        // State güncelle
        if (InflationLevel.Value >= 1f)
        {
            // Tam şiş
            if (_targetItem != null && _targetItem.HasEnoughBags)
            {
                State.Value = LiftingBagState.Inflated;
            }
        }
        else if (State.Value == LiftingBagState.Paused)
        {
            // Tekrar pompalamaya başlandı
            State.Value = LiftingBagState.Inflating;
        }

        // Tüm clientlara pompa efekti bildir
        ObserversPumpEffect();
    }

    /// <summary>
    /// Tüm clientlarda pompa görsel/ses efekti tetikler.
    /// </summary>
    [ObserversRpc]
    private void ObserversPumpEffect()
    {
        // LiftingBagAudio ve görsel efektler bu RPC ile tetiklenir
        // (LiftingBagAudio bu objeyi dinler)
    }

    // ==================== GÖRSEL GÜNCELLEME ====================

    /// <summary>
    /// Balon mesh'ini inflation seviyesine göre ölçekle.
    /// Tüm clientlarda çalışır (SyncVar'dan okur).
    /// </summary>
    private void UpdateVisualScale()
    {
        if (bagRenderer == null || bagRenderer.sharedMesh == null) return;
        if (bagRenderer.sharedMesh.blendShapeCount == 0) return;

        // Modelin "SonukHal" (Deflated State) blendshape'i: 100 = Sönük, 0 = Şişkin.
        // Başlangıçta (InflationLevel = 0) değer 100 olmalı. Pompalamayla (InflationLevel = 1) değer 0'a inmeli.
        float targetWeight = (1f - InflationLevel.Value) * 100f;
        
        // HATA ÖNLEME: Eğer editörden index yanlışlıkla çok yüksek girildiyse sınırla.
        int safeIndex = Mathf.Clamp(blendShapeIndex, 0, bagRenderer.sharedMesh.blendShapeCount - 1);
        bagRenderer.SetBlendShapeWeight(safeIndex, targetWeight);

        // DİKKAT: Ölçeklemeyi (localScale) koddan siliyoruz! 
        // Çünkü BlendShape zaten şişme/sönme yapıyor ve senin inspector'da belirlediğin Scale (8,8,8) değerini bozuyordu.
    }

    // ==================== SÖKME / TEMİZLEME ====================

    /// <summary>
    /// Balonu eşyadan söker ve despawn eder.
    /// GrabbableObject.BreakObject() veya ShipCollector tarafından çağrılır.
    /// </summary>
    [Server]
    public void Detach()
    {
        if (_targetItem != null)
        {
            _targetItem.OnBagDetached(this);
            _targetItem = null;
        }

        _targetRb = null;
        State.Value = LiftingBagState.Detached;

        // SyncVar'ları temizle (clientlarda takip dursun)
        SyncedTargetNetObj.Value = null;

        // Despawn (pool'a geri dönecek)
        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            PoolManager.Instance.DespawnNetwork(netObj);
        }
    }
}
