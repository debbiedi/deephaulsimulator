using UnityEngine;

/// <summary>
/// Eşya prefabına eklenir. Nadirlik seviyesine göre görsel efekt uygular.
/// - Material emission (parıltı/glow) rengi ve şiddeti
/// - Opsiyonel parçacık efekti (VFX prefab)
/// Sadece client tarafında çalışır (görsel, network gerektirmez).
///
/// Kurulum:
/// 1. GrabbableObject olan prefaba bu scripti ekleyin.
/// 2. RarityDatabase referansını atayın.
/// 3. targetRenderers boş bırakılırsa otomatik olarak Renderer'ları bulur.
/// </summary>
public class RarityVisualEffect : MonoBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Nadirlik veritabanı (merkezi SO)")]
    public RarityDatabase rarityDatabase;

    [Tooltip("Parıltı uygulanacak renderer'lar (boşsa otomatik bulunur)")]
    public Renderer[] targetRenderers;

    [Header("Ayarlar")]
    [Tooltip("Sadece su altındayken parıltı aktif olsun mu?")]
    public bool onlyUnderwaterGlow = true;

    // --- Dahili State ---
    private GrabbableObject _grabbable;
    private RarityData _rarityData;
    private MaterialPropertyBlock _propBlock;
    private GameObject _activeVfx;
    private bool _isSetup;

    // Shader property ID cache (string lookup önlemek için)
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        _grabbable = GetComponent<GrabbableObject>();
        _propBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        Setup();
    }

    /// <summary>
    /// GrabbableObject'ten nadirlik seviyesini okuyup görsel ayarları uygula.
    /// </summary>
    private void Setup()
    {
        if (_grabbable == null || rarityDatabase == null)
        {
            _isSetup = false;
            return;
        }

        _rarityData = rarityDatabase.GetData(_grabbable.rarityTier);
        if (_rarityData == null)
        {
            _isSetup = false;
            return;
        }

        // Renderer'ları bul (atanmamışsa)
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>();

        _isSetup = true;

        // Parıltı yok (Hurda/Siradan gibi) ise erken çık
        if (_rarityData.glowIntensity <= 0f)
        {
            ClearEmission();
            return;
        }

        // İlk emission rengini ayarla
        ApplyEmission(_rarityData.glowColorHDR * _rarityData.glowIntensity);
    }

    private void Update()
    {
        if (!_isSetup || _rarityData == null) return;
        if (_rarityData.glowIntensity <= 0f) return;

        // Nabız efekti (pulse) - 0.5 ile 1.0 arasında (tamamen sönmesin)
        float intensity = _rarityData.glowIntensity;
        if (_rarityData.glowPulseSpeed > 0f)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * _rarityData.glowPulseSpeed * Mathf.PI * 2f);
            intensity *= pulse;
        }

        // Su altı kontrolü (opsiyonel)
        if (onlyUnderwaterGlow)
        {
            WaterZone zone = WaterZone.GetZoneForPosition(transform.position);
            if (zone == null || !zone.IsUnderwater(transform.position.y))
            {
                ClearEmission();
                DespawnVfx();
                return;
            }
        }

        ApplyEmission(_rarityData.glowColorHDR * intensity);
        ManageVfx();
    }

    /// <summary>
    /// MaterialPropertyBlock ile emission rengi ayarla.
    /// Material instance oluşturmaz (bellek dostu, batching korunur).
    /// </summary>
    private void ApplyEmission(Color emissionColor)
    {
        if (targetRenderers == null) return;

        _propBlock.SetColor(EmissionColorId, emissionColor);

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] != null)
                targetRenderers[i].SetPropertyBlock(_propBlock);
        }
    }

    /// <summary>
    /// Emission'u kapat (su dışında veya parıltısı olmayan eşyalar için).
    /// </summary>
    private void ClearEmission()
    {
        if (targetRenderers == null) return;

        _propBlock.SetColor(EmissionColorId, Color.black);

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] != null)
                targetRenderers[i].SetPropertyBlock(_propBlock);
        }
    }

    /// <summary>
    /// Parçacık efektini yönet (VFX prefab spawn/despawn).
    /// PoolManager.SpawnLocal kullanır (network spawn gerekmez, sadece lokal görsel).
    /// </summary>
    private void ManageVfx()
    {
        if (_rarityData.vfxPrefab == null) return;

        // Zaten aktif efekt varsa pozisyon güncelle
        if (_activeVfx != null && _activeVfx.activeInHierarchy)
        {
            _activeVfx.transform.position = transform.position;
            return;
        }

        // Yeni efekt spawn et (lokal havuzdan)
        if (PoolManager.Instance != null)
        {
            _activeVfx = PoolManager.Instance.SpawnLocal(
                _rarityData.vfxPrefab,
                transform.position,
                Quaternion.identity
            );
        }
    }

    /// <summary>
    /// Parçacık efektini geri al.
    /// </summary>
    private void DespawnVfx()
    {
        if (_activeVfx != null && _activeVfx.activeInHierarchy)
        {
            if (PoolManager.Instance != null)
                PoolManager.Instance.DespawnLocal(_activeVfx);
            _activeVfx = null;
        }
    }

    private void OnDisable()
    {
        // Obje deaktive olduğunda (despawn/pool) efekti temizle
        ClearEmission();
        DespawnVfx();
    }
}
