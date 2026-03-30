using UnityEngine;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Managing.Scened;
using FishNet;

public class RandomPlayerSpawner : MonoBehaviour
{
    [Header("Karakter Prefabları")]
    [Tooltip("Lobiye katılan oyunculara rastgele verilecek karakter prefablarını buraya sürükleyin")]
    public NetworkObject[] playerPrefabs;

    [Header("Spawn Noktaları")]
    [Tooltip("Oyuncuların doğacağı noktaları (Transform) buraya ekleyin. Boş kalırsa (0,0,0) noktasında doğarlar.")]
    public Transform[] spawnPoints;

    private int _nextSpawnIndex = 0;

    private void Start()
    {
        // Sunucuda sahneler yüklendiğinde tetiklenecek
        InstanceFinder.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
    }

    private void OnDestroy()
    {
        if (InstanceFinder.SceneManager != null)
        {
            InstanceFinder.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
        }
    }

    // Henüz seçilmemiş karakterlerin listesi
    private System.Collections.Generic.List<int> _availableCharacters = new System.Collections.Generic.List<int>();

    private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer) return; // Sadece sunucu spawn işlemi yapar

        // KESİN ÇÖZÜM: Eğer kullanıcının halihazırda doğmuş bir karakteri varsa, 2. kez DOĞURMA!
        // Bu kod, üst üste 2 adam doğması bug'ını sonsuza kadar engeller.
        if (conn.FirstObject != null)
        {
            Debug.Log($"[RandomPlayerSpawner] İptal edildi: Kullanıcının zaten bir karakteri var obj: {conn.FirstObject.name}");
            return; 
        }

        if (playerPrefabs == null || playerPrefabs.Length == 0)
        {
            Debug.LogError("[RandomPlayerSpawner] Karakter prefabları atanmamış!");
            return;
        }

        // Eğer müsait karakter listemiz boşsa (oyun yeni başladıysa), listeyi doldur (0, 1, 2, 3)
        if (_availableCharacters.Count == 0)
        {
            for (int i = 0; i < playerPrefabs.Length; i++)
            {
                _availableCharacters.Add(i);
            }
        }

        // Torbadan RASTGELE bir karakter numarası çek ve başkasına çıkmasın diye o numarayı torbadan at
        int randomPoolIndex = Random.Range(0, _availableCharacters.Count);
        int finalPrefabIndex = _availableCharacters[randomPoolIndex];
        _availableCharacters.RemoveAt(randomPoolIndex);

        // Seçilen karakter
        NetworkObject selectedPrefab = playerPrefabs[finalPrefabIndex];

        // 2. Spawn noktasını belirle
        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            // Sırayla veya rastgele doğma noktası seçimi
            Transform sp = spawnPoints[_nextSpawnIndex % spawnPoints.Length];
            if (sp != null)
            {
                spawnPosition = sp.position;
                spawnRotation = sp.rotation;
            }
            _nextSpawnIndex++;
        }

        // 3. Karakteri instantiate et ve ağ üzerinde spawnlaarak oyuncuya (conn) sahiplik ver
        NetworkObject spawnedPlayer = Instantiate(selectedPrefab, spawnPosition, spawnRotation);
        InstanceFinder.ServerManager.Spawn(spawnedPlayer, conn);
        
        Debug.Log($"[RandomPlayerSpawner] Oyuncu (Conn {conn.ClientId}) {selectedPrefab.name} karakteriyle doğdu.");
    }
}
