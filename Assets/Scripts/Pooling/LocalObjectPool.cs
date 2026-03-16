using System.Collections.Generic;
using UnityEngine;

public class LocalObjectPool : MonoBehaviour
{
    [System.Serializable]
    public class Pool
    {
        public string tag; // İsteğe bağlı, havuzu bulmak için
        public GameObject prefab;
        public int initialSize;
    }

    public List<Pool> pools;
    private Dictionary<GameObject, Queue<GameObject>> poolDictionary;
    private Dictionary<GameObject, GameObject> activeObjToPrefab; // Aktif objenin hangi prefab'a ait olduğunu tutar

    void Awake()
    {
        poolDictionary = new Dictionary<GameObject, Queue<GameObject>>();
        activeObjToPrefab = new Dictionary<GameObject, GameObject>();

        // Başlangıç havuzlarını oluştur
        foreach (Pool pool in pools)
        {
            if (pool.prefab == null) continue;

            Queue<GameObject> objectPool = new Queue<GameObject>();

            for (int i = 0; i < pool.initialSize; i++)
            {
                GameObject obj = Instantiate(pool.prefab, transform);
                obj.SetActive(false);
                objectPool.Enqueue(obj);
            }

            poolDictionary.Add(pool.prefab, objectPool);
        }
    }

    public GameObject SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        // Havuzda bu prefab yoksa otomatik oluştur (Dinamik Havuzlama)
        if (!poolDictionary.ContainsKey(prefab))
        {
            Debug.LogWarning("Havuzda şu prefab bulunamadı, dinamik olarak oluşturuluyor: " + prefab.name);
            CreateDynamicPool(prefab, 5); // Fallback olarak 5 tane oluştur
        }

        GameObject objectToSpawn;
        // Eğer havuzda boşta obje kalmadıysa yeni bir tane üretip listeye ekle
        if (poolDictionary[prefab].Count == 0)
        {
            objectToSpawn = Instantiate(prefab, transform);
        }
        else
        {
            objectToSpawn = poolDictionary[prefab].Dequeue();
        }

        objectToSpawn.SetActive(true);
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;

        activeObjToPrefab[objectToSpawn] = prefab;
        return objectToSpawn;
    }

    public void ReturnToPool(GameObject obj)
    {
        if (obj == null) return;

        if (!activeObjToPrefab.ContainsKey(obj))
        {
            // Eğer obje havuzdan doğmamışsa direkt yok et
            Debug.LogWarning("Havuza ait olmayan bir obje iade edilmeye çalışılıyor: " + obj.name);
            Destroy(obj);
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(transform); // Düzeni korumak için tekrar manager altına al
        
        GameObject originalPrefab = activeObjToPrefab[obj];
        poolDictionary[originalPrefab].Enqueue(obj);
        activeObjToPrefab.Remove(obj);
    }

    private void CreateDynamicPool(GameObject prefab, int size)
    {
        Queue<GameObject> objectPool = new Queue<GameObject>();
        for (int i = 0; i < size; i++)
        {
            GameObject obj = Instantiate(prefab, transform);
            obj.SetActive(false);
            objectPool.Enqueue(obj);
        }
        poolDictionary.Add(prefab, objectPool);
    }
}