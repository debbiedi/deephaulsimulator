using UnityEngine;
using FishNet.Object;

/// <summary>
/// Sahne yüklendiğinde eşyaları (cargo) sunucu tarafından spawn eden sistem.
/// Sahne1'deki boş bir GameObject'e eklenir.
/// Sadece sunucu (host) spawn işlemini yapar, tüm istemciler otomatik görür.
/// </summary>
public class CargoSystem : NetworkBehaviour
{
    [Header("Spawn Edilecek Eşyalar")]
    [Tooltip("Spawn edilecek eşya prefab'ları")]
    public SpawnEntry[] spawnEntries;

    [System.Serializable]
    public struct SpawnEntry
    {
        [Tooltip("Spawn edilecek prefab (NetworkObject gerekli)")]
        public GameObject prefab;

        [Tooltip("Spawn pozisyonu (sahnedeki boş obje)")]
        public Transform spawnPoint;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Sadece sunucu eşyaları spawn eder
        if (spawnEntries == null) return;

        foreach (var entry in spawnEntries)
        {
            if (entry.prefab == null) continue;

            Vector3 pos = entry.spawnPoint != null ? entry.spawnPoint.position : transform.position;
            Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : Quaternion.identity;

            // PoolManager üzerinden Network Objesini havuzdan (veya yoksa instantiate ederek) spawn ediyoruz
            NetworkObject networkPrefab = entry.prefab.GetComponent<NetworkObject>();
            if (networkPrefab != null)
            {
                PoolManager.Instance.SpawnNetwork(networkPrefab, pos, rot);
            }
            else
            {
                Debug.LogError($"[CargoSystem] {entry.prefab.name} prefabında NetworkObject bileşeni yok!");
            }
        }

        Debug.Log($"[CargoSystem] {spawnEntries.Length} eşya spawn edildi.");
    }
}
