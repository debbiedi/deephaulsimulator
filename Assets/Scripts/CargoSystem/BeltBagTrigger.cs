using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Oyuncunun kemer/göğüs bölgesindeki görünmez SphereCollider trigger alanı.
/// Player prefabında bir child GameObject olarak eklenir.
/// Trigger alanına giren küçük eşyaları takip eder.
///
/// Kullanım:
/// 1. Player prefabına boş bir child GameObject ekleyin (BeltBagZone).
/// 2. SphereCollider ekleyip "Is Trigger" işaretleyin.
/// 3. Bu scripti ekleyin.
/// 4. Yarıçapı (radius) ~0.4-0.6 yapın.
/// 5. Pozisyonu karakterin göğüs/bel bölgesine ayarlayın.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class BeltBagTrigger : MonoBehaviour
{
    [Header("Ayarlar")]
    [Tooltip("Trigger alanının yarıçapı")]
    public float triggerRadius = 0.5f;

    [Header("Gizmo")]
    public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.15f);
    public Color gizmoWireColor = new Color(0f, 1f, 0.5f, 0.6f);

    // Şu an trigger alanı içinde olan GrabbableObject'ler
    private HashSet<GrabbableObject> _overlappingItems = new HashSet<GrabbableObject>();

    private SphereCollider _sphereCollider;

    private void Awake()
    {
        _sphereCollider = GetComponent<SphereCollider>();
        _sphereCollider.isTrigger = true;
        _sphereCollider.radius = triggerRadius;
    }

    private void Reset()
    {
        // Inspector'da script eklendiğinde otomatik ayarlar
        var col = GetComponent<SphereCollider>();
        if (col != null)
        {
            col.isTrigger = true;
            col.radius = triggerRadius;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        GrabbableObject grabbable = other.GetComponent<GrabbableObject>();
        if (grabbable != null && grabbable.itemSize == ItemSize.Small)
        {
            _overlappingItems.Add(grabbable);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        GrabbableObject grabbable = other.GetComponent<GrabbableObject>();
        if (grabbable != null)
        {
            _overlappingItems.Remove(grabbable);
        }
    }

    /// <summary>
    /// Belirtilen eşya şu an trigger alanı içinde mi?
    /// PlayerGrabber.Release() tarafından çağrılır.
    /// </summary>
    public bool IsItemInZone(GrabbableObject item)
    {
        // Yok edilmiş referansları temizle
        _overlappingItems.RemoveWhere(x => x == null);
        return _overlappingItems.Contains(item);
    }

    /// <summary>
    /// Eşyayı takip listesinden kaldır (toplandıktan sonra).
    /// </summary>
    public void RemoveItem(GrabbableObject item)
    {
        _overlappingItems.Remove(item);
    }

    private void OnDrawGizmos()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere != null)
        {
            Vector3 center = transform.position + sphere.center;
            float scaledRadius = sphere.radius * transform.lossyScale.x;

            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(center, scaledRadius);

            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireSphere(center, scaledRadius);
        }
    }
}
