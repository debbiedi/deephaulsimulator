using UnityEngine;

/// <summary>
/// Su alanını tanımlayan trigger bölge scripti.
/// FPSZoneTrigger ile aynı mantıkta çalışır.
/// Oyuncu bu bölgeye girdiğinde Swimming moduna geçer, çıktığında Walking moduna döner.
/// 
/// Kullanım:
/// 1. Sahneye bir GameObject ekleyin (WaterZone).
/// 2. BoxCollider ekleyip "Is Trigger" işaretleyin.
/// 3. Bu scripti ekleyin.
/// 4. waterSurfaceY değerini BoxCollider'ın üst yüzeyinin Y pozisyonuna ayarlayın.
/// 5. Oyuncu objesinin Tag'ini "Player" olarak ayarlayın.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaterZone : MonoBehaviour
{
    [Header("Su Ayarları")]
    [Tooltip("Su yüzeyinin Y pozisyonu (BoxCollider'ın üst yüzeyi)")]
    public float waterSurfaceY = 0f;

    [Tooltip("Su direnci - hareket yavaşlatma katsayısı (0-1 arası, 1 = çok yavaş)")]
    [Range(0f, 0.95f)]
    public float waterDrag = 0.3f;

    [Tooltip("Yüzme yerçekimi (normal: -15, suda: -2 gibi azaltılmış)")]
    public float swimGravity = -2f;

    [Tooltip("Su altı yürüme yerçekimi")]
    public float underwaterWalkGravity = -5f;

    [Header("Görsel Ayarlar")]
    [Tooltip("Oyuncu tag'i")]
    public string playerTag = "Player";

    [Tooltip("Gizmo rengi (editörde gösterim için)")]
    public Color gizmoColor = new Color(0f, 0.4f, 1f, 0.25f);

    [Tooltip("Gizmo çizgi rengi")]
    public Color gizmoWireColor = new Color(0f, 0.6f, 1f, 0.8f);

    private void Reset()
    {
        // Collider'ın trigger olduğundan emin ol
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        // waterSurfaceY değerini BoxCollider'ın üst yüzeyine ayarla
        if (col is BoxCollider box)
        {
            waterSurfaceY = transform.position.y + box.center.y + (box.size.y * transform.localScale.y * 0.5f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        // Çarpışan objenin kendi sahipliğimizdeki karakter olduğundan emin olmak için
        PlayerMovementStateManager stateManager = other.GetComponent<PlayerMovementStateManager>();
        
        // Eğer objede stateManager varsa ve IsOwner (bizim kontrolümüzde) ise suya gir
        if (stateManager != null && stateManager.IsOwner)
        {
            stateManager.EnterWater(this);
            Debug.Log($"[WaterZone] Oyuncu '{other.gameObject.name}' su bölgesine girdi.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        PlayerMovementStateManager stateManager = other.GetComponent<PlayerMovementStateManager>();

        if (stateManager != null && stateManager.IsOwner)
        {
            stateManager.ExitWater();
            Debug.Log($"[WaterZone] Oyuncu '{other.gameObject.name}' su bölgesinden çıktı.");
        }
    }

    /// <summary>
    /// Verilen Y pozisyonunun su yüzeyinin altında olup olmadığını kontrol eder.
    /// </summary>
    public bool IsUnderwater(float yPosition)
    {
        return yPosition < waterSurfaceY;
    }

    /// <summary>
    /// Su yüzeyine olan derinliği döndürür (pozitif = su altında, negatif = su üstünde).
    /// </summary>
    public float GetDepth(float yPosition)
    {
        return waterSurfaceY - yPosition;
    }

    // ==================== Editor Gizmos ====================

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        var col = GetComponent<Collider>();

        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);

            // Çizgi rengi
            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireCube(box.center, box.size);

            // Su yüzeyi çizgisi (dünya koordinatlarında)
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
            Vector3 surfaceCenter = new Vector3(transform.position.x, waterSurfaceY, transform.position.z);
            float sizeX = box.size.x * transform.localScale.x;
            float sizeZ = box.size.z * transform.localScale.z;
            // Su yüzeyi düzlemi çizgisi
            Vector3 p1 = surfaceCenter + new Vector3(-sizeX * 0.5f, 0, -sizeZ * 0.5f);
            Vector3 p2 = surfaceCenter + new Vector3(sizeX * 0.5f, 0, -sizeZ * 0.5f);
            Vector3 p3 = surfaceCenter + new Vector3(sizeX * 0.5f, 0, sizeZ * 0.5f);
            Vector3 p4 = surfaceCenter + new Vector3(-sizeX * 0.5f, 0, sizeZ * 0.5f);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p4);
            Gizmos.DrawLine(p4, p1);
            Gizmos.DrawLine(p1, p3); // çapraz
            Gizmos.DrawLine(p2, p4); // çapraz
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius * transform.lossyScale.x);
            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius * transform.lossyScale.x);
        }
    }
}
