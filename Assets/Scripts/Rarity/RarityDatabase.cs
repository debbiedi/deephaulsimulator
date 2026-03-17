using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tüm nadirlik seviyelerinin verilerini tutan merkezi veritabanı.
/// Projede tek bir tane oluşturulur (Assets/Data/Rarity/RarityDatabase.asset).
/// GrabbableObject ve UI sistemleri buradan veri çeker.
/// </summary>
[CreateAssetMenu(fileName = "RarityDatabase", menuName = "Deep Haul/Rarity Database")]
public class RarityDatabase : ScriptableObject
{
    [Tooltip("Tüm nadirlik seviyeleri (Hurda'dan Hazine'ye sıralı)")]
    public RarityData[] rarityEntries;

    // Hızlı arama için runtime cache
    private Dictionary<RarityTier, RarityData> _lookup;

    /// <summary>
    /// Verilen nadirlik seviyesinin verilerini döndürür.
    /// İlk çağrıda dictionary oluşturulur, sonraki çağrılar O(1).
    /// </summary>
    public RarityData GetData(RarityTier tier)
    {
        if (_lookup == null)
            BuildLookup();

        if (_lookup.TryGetValue(tier, out RarityData data))
            return data;

        Debug.LogWarning($"[RarityDatabase] '{tier}' için RarityData bulunamadı!");
        return null;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<RarityTier, RarityData>();
        if (rarityEntries == null) return;

        foreach (var entry in rarityEntries)
        {
            if (entry == null) continue;
            if (!_lookup.ContainsKey(entry.tier))
                _lookup.Add(entry.tier, entry);
            else
                Debug.LogWarning($"[RarityDatabase] '{entry.tier}' seviyesi birden fazla tanımlanmış!");
        }
    }

    private void OnEnable()
    {
        // Editor'da yeniden derlemelerden sonra cache'i yenile
        _lookup = null;
    }
}
