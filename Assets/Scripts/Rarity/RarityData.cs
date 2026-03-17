using UnityEngine;

/// <summary>
/// Tek bir nadirlik seviyesinin görsel ve oyun verilerini tutar.
/// Her tier için bir ScriptableObject oluşturulur (Assets/Data/Rarity/ altında).
/// Tasarımcılar kod değiştirmeden renk, efekt ve çarpanları düzenleyebilir.
/// </summary>
[CreateAssetMenu(fileName = "NewRarityData", menuName = "Deep Haul/Rarity Data")]
public class RarityData : ScriptableObject
{
    [Header("Temel Bilgiler")]
    [Tooltip("Nadirlik seviyesi (enum)")]
    public RarityTier tier;

    [Tooltip("Türkçe görünen isim (UI'da gösterilecek)")]
    public string displayNameTR;

    [Tooltip("İngilizce görünen isim")]
    public string displayNameEN;

    [Tooltip("Kısa açıklama (Türkçe)")]
    [TextArea(2, 4)]
    public string descriptionTR;

    [Header("Renk Ayarları")]
    [Tooltip("Nadirlik rengi (UI, isim rengi vb. için)")]
    public Color rarityColor = Color.white;

    [Tooltip("HDR parıltı rengi (shader emission için - yoğunluk ayarlanabilir)")]
    [ColorUsage(true, true)]
    public Color glowColorHDR = Color.white;

    [Header("Görsel Efekt Ayarları")]
    [Tooltip("Su altında eşyanın etrafındaki parıltı/parçacık efekti prefab'ı (opsiyonel)")]
    public GameObject vfxPrefab;

    [Tooltip("Parıltı (glow/emission) şiddeti (0 = yok, 1 = tam)")]
    [Range(0f, 3f)]
    public float glowIntensity = 0f;

    [Tooltip("Parıltı nabız hızı (saniye başına titreşim). 0 = sabit parıltı.")]
    [Range(0f, 5f)]
    public float glowPulseSpeed = 0f;

    [Header("Oyun Mekanikleri")]
    [Tooltip("Fiyat çarpanı (basePrice * priceMultiplier = gerçek fiyat)")]
    [Range(0.1f, 10f)]
    public float priceMultiplier = 1f;

    [Tooltip("Kırılganlık çarpanı (yüksek nadirlik = daha kırılgan)")]
    [Range(0.5f, 3f)]
    public float fragilityMultiplier = 1f;
}
