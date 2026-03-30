using UnityEngine;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Object.Synchronizing;

[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : NetworkBehaviour
{
    [Header("Item Identity")]
    public string itemName = "Unnamed Item";
    public string itemId = "";

    [Header("Size Classification")]
    public ItemSize itemSize = ItemSize.Large; // Varsayılan: Large (mevcut davranış korunur)
    [Tooltip("Bu obje bir alışveriş sepeti veya ağır bir araç mı?")]
    public bool isHeavyVehicle = false;
    [Tooltip("Araç çekilirken oyuncuya bakacak uç kısım (Örn: Sepet Sapı). Boş bırakılırsa varsayılan yönü kullanır.")]
    public Transform playerFacingNode;

    [Header("Rarity")]
    [Tooltip("Bu eşyanın nadirlik seviyesi")]
    public RarityTier rarityTier = RarityTier.Siradan;

    [Header("Weight")]
    public float weight = 1f; // Kilogram cinsinden ağırlık

    [Header("Value Settings")]
    public float basePrice = 100f;
    public float currentPrice;

    [Header("Damage Settings")]
    public float fragility = 5f; // Çarpma şiddetinin ne kadarı hasara dönüşecek
    public float damageThreshold = 3f; // Hasar almak için gereken minimum çarpma hızı (velocity)

    [Tooltip("Bu katmanlarla çarpışınca hasar ALINMAZ (Örn: Drone katmanı)")]
    public LayerMask safeCollisionLayers;

    [Header("Lifting Bags")]
    [Tooltip("Bu eşyayı yüzeye çıkarmak için gereken balon sayısı")]
    public int requiredBagCount = 1;

    [Header("Glow Outline Settings")]
    [Tooltip("Eğer bu obje (taşıma arabası veya sepet gibi) parlamayacaksa buradaki tiki kaldırın.")]
    public bool enableOutline = true;
    public Color outlineColor = Color.yellow;
    [Range(0f, 10f)] public float outlineWidth = 2.0f;

    private Rigidbody rb;
    public readonly SyncVar<bool> isFixedInCart = new SyncVar<bool>(false);
    private List<LiftingBag> attachedBags = new List<LiftingBag>();

    // Glow için referanslar
    private Material _outlineMaterial;
    private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
    private bool _isOutlined = false;

    // --- Public Erişimler (Lifting Bag sistemi için) ---
    public Rigidbody Rb { get { if (rb == null) rb = GetComponent<Rigidbody>(); return rb; } }
    public int AttachedBagCount => attachedBags.Count;
    public bool HasEnoughBags => attachedBags.Count >= requiredBagCount;

    private void Awake()
    {
        isFixedInCart.OnChange += OnFixedInCartChanged;
    }

    private void OnDestroy()
    {
        isFixedInCart.OnChange -= OnFixedInCartChanged;
        
        if (_outlineMaterial != null)
        {
            Destroy(_outlineMaterial);
        }
    }

    private void OnFixedInCartChanged(bool prev, bool next, bool asServer)
    {
        UpdatePhysicsAuthority();
    }

    /// <summary>
    /// Bu eşyaya kaldırma balonu takılabilir mi?
    /// Sadece Medium ve MediumLarge eşyalar desteklenir.
    /// </summary>
    public bool CanAttachBag()
    {
        return (itemSize == ItemSize.Medium || itemSize == ItemSize.MediumLarge)
            && attachedBags.Count < requiredBagCount;
    }

    public void OnBagAttached(LiftingBag bag)
    {
        if (!attachedBags.Contains(bag))
            attachedBags.Add(bag);
    }

    public void OnBagDetached(LiftingBag bag)
    {
        attachedBags.Remove(bag);
    }

    void Start()
    {
        currentPrice = basePrice;
        rb = GetComponent<Rigidbody>();

        if (!enableOutline) return; // Parlama istenmiyorsa shader ayarlarını hiç kurma

        // Outline sistemi hazırlığı
        Shader outlineShader = Shader.Find("Custom/URP_Outline");
        if (outlineShader != null)
        {
            _outlineMaterial = new Material(outlineShader);

            // GameManager üzerinden rarity (nadirlik) rengini otomatik çeker, 
            // yoksa varsayılan outlineColor rengini kullanır.
            Color finalColor = GameManager.Instance != null 
                             ? GameManager.Instance.GetColorForRarity(rarityTier) 
                             : outlineColor;

            _outlineMaterial.SetColor("_OutlineColor", finalColor);
            _outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);
        }
        else
        {
            Debug.LogWarning("Outline shader bulunamadı. Lütfen URP_Outline shader'ın projede olduğundan emin olun.");
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            // Parçacık vb. Renderer'ları outline dışında tut
            if (r is ParticleSystemRenderer) continue;
            _originalMaterials[r] = r.sharedMaterials;
        }

        // Obje dünyada ilk spawn olduğunda parlasın
        SetOutline(true);
    }

    public void SetOutline(bool enabled)
    {
        if (!enableOutline || _outlineMaterial == null) return;
        if (_isOutlined == enabled) return;

        _isOutlined = enabled;

        foreach (var kvp in _originalMaterials)
        {
            Renderer r = kvp.Key;
            if (r == null) continue; // Nesne bu sırada silinmişse atla

            if (enabled)
            {
                // Material dizisinin en sonuna Outline materyali ekle
                Material[] orig = kvp.Value;
                Material[] withOutline = new Material[orig.Length + 1];
                orig.CopyTo(withOutline, 0);
                withOutline[orig.Length] = _outlineMaterial;
                r.sharedMaterials = withOutline;
            }
            else
            {
                // Orijinal görünene geri dön (Glow kapat)
                r.sharedMaterials = kvp.Value;
            }
        }
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
    public void UpdatePhysicsAuthority()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();

        if (isFixedInCart.Value)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            return;
        }

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
        // ARAÇLAR (Sepet vb.) HİÇBİR ZAMAN HASAR ALMAZ!
        if (isHeavyVehicle) return;

        // 1. GÜVENLİK: Çarptığımız obje güvenli bir katmandaysa hasar alma (Örn: Drone layer'ı inspector'dan seçilmişse)
        if ((safeCollisionLayers.value & (1 << collision.gameObject.layer)) > 0)
        {
            return;
        }

        // 2. GÜVENLİK: Çarptığımız obje veya ebeveyni CarrierDrone veya CartController içeriyorsa kesinlikle hasar alma
        if (collision.gameObject.GetComponentInParent<CarrierDrone>() != null || 
            collision.gameObject.GetComponentInParent<CartController>() != null)
        {
            return;
        }

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

        if (IsServer)
        {
            // Takılı kaldırma balonlarını sök ve despawn et
            for (int i = attachedBags.Count - 1; i >= 0; i--)
            {
                if (attachedBags[i] != null)
                    attachedBags[i].Detach();
            }
            attachedBags.Clear();

            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null)
            {
                PoolManager.Instance.DespawnNetwork(netObj);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
