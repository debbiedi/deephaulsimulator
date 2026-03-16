using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : NetworkBehaviour
{
    [Header("Item Identity")]
    public string itemName = "Unnamed Item";
    public string itemId = "";

    [Header("Size Classification")]
    public ItemSize itemSize = ItemSize.Large; // Varsayılan: Large (mevcut davranış korunur)

    [Header("Weight")]
    public float weight = 1f; // Kilogram cinsinden ağırlık

    [Header("Value Settings")]
    public float basePrice = 100f;
    public float currentPrice;

    [Header("Damage Settings")]
    public float fragility = 5f; // Çarpma şiddetinin ne kadarı hasara dönüşecek
    public float damageThreshold = 3f; // Hasar almak için gereken minimum çarpma hızı (velocity)

    private Rigidbody rb;
    private bool _originalIsKinematic;

    void Start()
    {
        currentPrice = basePrice;
        rb = GetComponent<Rigidbody>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdatePhysicsAuthority();
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        UpdatePhysicsAuthority();
    }

    /// <summary>
    /// Sahip olan istemcide fizik aktif, diğerlerinde kinematik.
    /// Bu, NetworkTransform ile Rigidbody çakışmasını önler (titreşimi engeller).
    /// </summary>
    private void UpdatePhysicsAuthority()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();

        if (base.IsOwner || base.IsServerInitialized)
        {
            // Sahip veya sunucu: fizik normal çalışır
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        else
        {
            // Diğer istemciler: fizik kapalı, pozisyon NetworkTransform'dan gelir
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Çarpışmanın şiddetini alıyoruz
        float impactSpeed = collision.relativeVelocity.magnitude;

        // Eğer eşik değerinden hızlı çarpıldıysa hasar uygula
        if (impactSpeed > damageThreshold)
        {
            float damage = (impactSpeed - damageThreshold) * fragility;
            TakeDamage(damage);
        }
    }

    private void TakeDamage(float amount)
    {
        currentPrice -= amount;
        Debug.Log($"{gameObject.name} hasar aldı! Mevcut fiyat: {Mathf.Round(currentPrice)} / {basePrice}");

        if (currentPrice <= 0)
        {
            currentPrice = 0;
            BreakObject();
        }
    }

    private void BreakObject()
    {
        Debug.Log($"{gameObject.name} kırıldı!");
        
        // Eğer sunucudaysak (Server) objeyi PoolManager ile ağdan despawn ediyoruz (veya havuza yolluyoruz)
        if (IsServer)
        {
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null)
            {
                PoolManager.Instance.DespawnNetwork(netObj);
            }
            else
            {
                // NetworkObject yoksa (olası değil ama güvenli kod yazalım)
                Destroy(gameObject);
            }
        }
    }
}
