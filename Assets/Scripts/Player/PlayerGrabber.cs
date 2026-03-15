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

    private GrabbableObject heldObject;
    private Rigidbody heldRb;
    private bool originalUseGravity;

    void Update()
    {
        // Benim objem değil ise çalışma
        if (!base.IsOwner) return;

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
            float scroll = Mouse.current.scroll.y.ReadValue() * 0.01f; // Yeni sistemde değerler çok büyük geldiği için küçültüyoruz
            if (Mathf.Abs(scroll) > 0.01f)
            {
                currentHoldDistance += scroll * scrollSpeed;
                currentHoldDistance = Mathf.Clamp(currentHoldDistance, minHoldDistance, maxHoldDistance);
            }
        }
    }

    void FixedUpdate()
    {
        if (!base.IsOwner) return;

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

    void Release()
    {
        if (heldRb != null)
        {
            // Yerçekimini eski haline döndür
            heldRb.useGravity = originalUseGravity;
        }

        heldObject = null;
        heldRb = null;
    }
}
