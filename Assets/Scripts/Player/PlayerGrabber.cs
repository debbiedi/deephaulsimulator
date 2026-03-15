using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Object;

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

    private GrabbableObject heldObject;
    private Rigidbody heldRb;
    private bool originalUseGravity;

    // Çantaya emilme animasyonu state
    private bool _isCollectingToBag;
    private float _collectTimer;
    private Vector3 _collectStartPos;
    private Vector3 _collectStartScale;

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
            RaycastHit checkHit;
            if (Physics.Raycast(playerCamera.position, playerCamera.forward, out checkHit, grabRange, grabMask))
            {
                // Eğer tutabileceğimiz bir objeye (GrabbableObject) bakıyorsak Yeşil, değilse Kırmızı ışın çizer
                Color rayColor = checkHit.collider.GetComponent<GrabbableObject>() != null ? Color.green : Color.red;
                Debug.DrawRay(playerCamera.position, playerCamera.forward * checkHit.distance, rayColor);
            }
            else
            {
                // Hiçbir engele çarpmıyorsa kırmızı ve max menzilde çizer
                Debug.DrawRay(playerCamera.position, playerCamera.forward * grabRange, Color.red);
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
            // Bu yüzden objenin yönünü her zaman MAUSE'nin (Kameranın) baktığı yön (playerCamera.forward) olarak ayarlıyoruz.
            // Başlangıç noktası olarak holdPoint (karakterin önü) kullanılsa bile yön kameraya göre belirlenir.
            Vector3 targetPosition = holdPoint != null ? 
                holdPoint.position + playerCamera.forward * currentHoldDistance : 
                playerCamera.position + playerCamera.forward * currentHoldDistance;

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
        RaycastHit hit;
        // Kameranın ortasından (veya bakış yönünden) yolla ama oyuncuyu yoksay (grabMask kullanarak)
        if (Physics.Raycast(playerCamera.position, playerCamera.forward, out hit, grabRange, grabMask))
        {
            // Çarptığımız obje GrabbableObject scriptine sahip mi?
            GrabbableObject grabbable = hit.collider.GetComponent<GrabbableObject>();
            if (grabbable != null)
            {
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

    void Release()
    {
        if (heldObject == null)
        {
            heldRb = null;
            return;
        }

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
