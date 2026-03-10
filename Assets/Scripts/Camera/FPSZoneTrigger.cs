using UnityEngine;

/// <summary>
/// Belirli bir bölgeye girildiğinde FPS modunu zorunlu kılar.
/// Bu scripti bir Trigger Collider içeren GameObject'e ekleyin.
/// Oyuncu bu bölgeye girdiğinde otomatik olarak FPS moduna geçilir,
/// çıktığında ise serbest moda (TPS) dönülür.
/// 
/// Kullanım:
/// 1. Sahneye bir boş GameObject ekleyin.
/// 2. BoxCollider (veya başka collider) ekleyip "Is Trigger" işaretleyin.
/// 3. Bu scripti ekleyin.
/// 4. Oyuncu objesinin Tag'ini "Player" olarak ayarlayın.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FPSZoneTrigger : MonoBehaviour
{
    [Header("Ayarlar")]
    [Tooltip("Bölgeye girildiğinde zorlanacak kamera modu")]
    public CameraMode forcedMode = CameraMode.FPS;

    [Tooltip("Oyuncu tag'i")]
    public string playerTag = "Player";

    [Tooltip("Gizmo rengi (editörde gösterim için)")]
    public Color gizmoColor = new Color(1f, 0f, 0f, 0.25f);

    private void Reset()
    {
        // Collider'ın trigger olduğundan emin ol
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (CameraModeManager.Instance != null)
        {
            CameraModeManager.Instance.ForceMode(forcedMode);
            Debug.Log($"[FPSZoneTrigger] Oyuncu '{gameObject.name}' bölgesine girdi. Mod: {forcedMode}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (CameraModeManager.Instance != null)
        {
            CameraModeManager.Instance.ReleaseForceMode();
            Debug.Log($"[FPSZoneTrigger] Oyuncu '{gameObject.name}' bölgesinden çıktı.");
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        var col = GetComponent<Collider>();

        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius * transform.lossyScale.x);
            Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius * transform.lossyScale.x);
        }
    }
}
