using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GrabbableObject : MonoBehaviour
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

    void Start()
    {
        currentPrice = basePrice;
        rb = GetComponent<Rigidbody>();
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
        
        // TODO: İleride burada Object Pooling sistemine geri döndürülebilir
        // Şimdilik sadece yok ediyoruz. İsterseniz kırılma partikülleri falan da eklenebilir.
        Destroy(gameObject);
    }
}
