using UnityEngine;

/// <summary>
/// Drone navigasyon waypoint'i. Sahnede bos GameObject olarak yerlestirilir.
/// CarrierDrone inspector'undan sirali olarak atanir.
///
/// Kurulum:
/// 1. Sahnede bos GameObject'ler olusturun (Waypoint_01, Waypoint_02, ...).
/// 2. Bu scripti ekleyin.
/// 3. Deniz dibinde drone'un guvenli gecebilecegi noktalara yerlestirin.
/// 4. CarrierDrone'un outboundWaypoints dizisine sirali olarak atayin.
/// </summary>
public class DroneWaypoint : MonoBehaviour
{
    [Header("Ayarlar")]
    [Tooltip("Bu waypoint'te yavasla (true ise drone bu noktada hiz keser)")]
    public bool slowDown = false;

    [Tooltip("Yavaslatilmis hiz carpani (slowDown = true ise)")]
    [Range(0.2f, 1f)]
    public float speedMultiplier = 0.5f;

    [Tooltip("Gizmo rengi")]
    public Color gizmoColor = new Color(0f, 1f, 1f, 0.8f);

    [Tooltip("Gizmo yaricap")]
    public float gizmoRadius = 0.5f;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, gizmoRadius);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius * 2f);

        if (slowDown)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, gizmoRadius * 3f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawSphere(transform.position, gizmoRadius * 3f);
    }
}
