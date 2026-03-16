using UnityEngine;
using FishNet;
using FishNet.Object;

/// <summary>
/// Tüm Havuz işlemlerini yöneten ana sınıf (Singleton).
/// Hem lokal "LocalObjectPool" ile çalışır, hem de "FishNet DefaultObjectPool" yeteneklerini kullanır.
/// </summary>
[RequireComponent(typeof(LocalObjectPool))]
public class PoolManager : MonoBehaviour
{
    // Singleton örneği, oyunda her yerden kolayca "PoolManager.Instance" ile çağırabiliriz
    public static PoolManager Instance { get; private set; }

    private LocalObjectPool localPool;

    private void Awake()
    {
        // Klasik Singleton kurulumu
        if (Instance == null)
        {
            Instance = this;
            localPool = GetComponent<LocalObjectPool>();
            DontDestroyOnLoad(gameObject); // Sahne geçişlerinde silinmesini engelliyoruz
        }
        else
        {
            Destroy(gameObject); // Çakışan managerı yok et
        }
    }

    // ==========================================
    // LOKAL OBJE İŞLEMLERİ (Particle, UI, Ses..)
    // ==========================================

    /// <summary>
    /// Sadece çağrıldığı kullanıcının ekranında bir obje oluşturur (veya havuzdan çıkarır).
    /// </summary>
    public GameObject SpawnLocal(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (localPool == null) return null;
        return localPool.SpawnFromPool(prefab, position, rotation);
    }

    /// <summary>
    /// Lokal objeyi havuza geri kapatır.
    /// </summary>
    public void DespawnLocal(GameObject obj)
    {
        if (localPool == null || obj == null) return;
        localPool.ReturnToPool(obj);
    }


    // ==========================================
    // NETWORK OBJE İŞLEMLERİ (Senkron Objeler)
    // ==========================================

    /// <summary>
    /// SADECE SERVER ÜZERİNDE ÇALIŞMALIDIR.
    /// FishNet aracılığıyla senkron bir obje spawn eder (Kendi native havuzunu kullanır).
    /// </summary>
    public NetworkObject SpawnNetwork(NetworkObject prefabNob, Vector3 position, Quaternion rotation)
    {
        if (!InstanceFinder.IsServer)
        {
            Debug.LogWarning("Network Obje sadece Server uzerinden Serialize edilebilir.");
            return null;
        }

        // Objeyi Instantiate ederiz. Daha sonra FishNet'in "Spawn" metodu, 
        // eger DefaultObjectPool konfigüre edilmişse otomatik olarak object pooling kullanacaktır.
        NetworkObject instantiatedNob = Instantiate(prefabNob, position, rotation);
        
        // FishNet'in ServerManager'ına bağlı Spawn modülü
        InstanceFinder.ServerManager.Spawn(instantiatedNob);
        
        return instantiatedNob;
    }

    /// <summary>
    /// SADECE SERVER ÜZERİNDE ÇALIŞMALIDIR.
    /// Network nesneyi havuza geri gönderir/yok eder.
    /// </summary>
    public void DespawnNetwork(NetworkObject nob)
    {
        if (!InstanceFinder.IsServer)
        {
            Debug.LogWarning("Network Obje sadece Server uzerinden Despawn edilebilir.");
            return;
        }

        if (nob != null)
        {
            // ServerManager.Despawn, eger FishNet Havuzu ayarlıysa objeyi kapatir, yoksa Destroy eder.
            InstanceFinder.ServerManager.Despawn(nob);
        }
        else
        {
            Debug.LogError("NetworkObject component'i olmayan bir seyi DespawnNetwork ile yok etmeye calisiyorsunuz!");
        }
    }
}