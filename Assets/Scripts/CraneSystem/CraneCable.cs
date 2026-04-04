using UnityEngine;

/// <summary>
/// Vinç kablosu görselleştirme. LineRenderer ile vinç ucu (cableTip) ile
/// sepet (basketAnchor) arasını çizer. Hafif catenary sarkma efekti uygular.
///
/// Kurulum:
/// 1. Vinç objesine bu scripti ekleyin.
/// 2. Aynı objeye LineRenderer component'ı ekleyin.
/// 3. Inspector'dan cableTip ve basketAnchor Transform'larını atayın.
/// 4. LineRenderer materyal/renk ayarlarını yapın (metalik zincir/halat).
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class CraneCable : MonoBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Vinç kolunun ucundaki kablo bağlantı noktası")]
    public Transform cableTip;

    [Tooltip("Sepetin kablo bağlantı noktası (sepetin üstü)")]
    public Transform basketAnchor;

    [Header("Kablo Ayarları")]
    [Tooltip("Kabloyu kaç segment ile çizeceğiz (daha fazla = daha pürüzsüz sarkma)")]
    [Range(2, 20)]
    public int segmentCount = 8;

    [Tooltip("Sarkma miktarı (0 = düz çizgi, pozitif = aşağı sarkma)")]
    public float sagAmount = 0.5f;

    [Tooltip("Kablo kalınlığı")]
    public float cableWidth = 0.05f;

    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.positionCount = segmentCount;
        _lineRenderer.startWidth = cableWidth;
        _lineRenderer.endWidth = cableWidth;
        _lineRenderer.useWorldSpace = true;
    }

    private void LateUpdate()
    {
        if (cableTip == null || basketAnchor == null)
        {
            _lineRenderer.enabled = false;
            return;
        }

        _lineRenderer.enabled = true;

        Vector3 start = cableTip.position;
        Vector3 end = basketAnchor.position;

        // Kablo uzunluğuna göre sarkma miktarını ayarla
        float distance = Vector3.Distance(start, end);
        float currentSag = sagAmount * (distance / 10f); // Uzun kabloda daha fazla sarkma

        for (int i = 0; i < segmentCount; i++)
        {
            float t = (float)i / (segmentCount - 1);
            Vector3 point = Vector3.Lerp(start, end, t);

            // Catenary (zincir eğrisi) basitleştirilmiş: parabolik sarkma
            // En fazla sarkma ortada (t=0.5), uçlarda sıfır
            float sag = currentSag * 4f * t * (1f - t); // Parabolic curve: 4t(1-t)
            point.y -= sag;

            _lineRenderer.SetPosition(i, point);
        }
    }

    /// <summary>
    /// Kabloyu göster/gizle.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (_lineRenderer != null)
            _lineRenderer.enabled = visible;
    }
}
