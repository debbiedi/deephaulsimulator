using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;

/// <summary>
/// Su yüzeyinde gemi yakınında trigger zone. Yüzeye çıkan eşyaları otomatik toplar.
/// LiftingBag Floating state'e geçtiğinde sunucu bu scripte bildirir.
/// Eşya gemiye doğru uçar + küçülür, sonra kargoya eklenir.
///
/// Kurulum:
/// 1. Gemi objesine child boş GameObject ekleyin (ShipCollectorZone).
/// 2. SphereCollider ekleyip "Is Trigger" işaretleyin, yarıçapı ~10-15 yapın.
/// 3. Bu scripti ekleyin.
/// 4. ShipCargo referansını atayın.
/// 5. collectTarget → güverte pozisyonu (boş Transform).
/// </summary>
public class ShipCollector : NetworkBehaviour
{
    // --- Statik registry (en yakın collector'ı bulmak için) ---
    public static readonly List<ShipCollector> ActiveCollectors = new List<ShipCollector>();

    [Header("Referanslar")]
    [Tooltip("Gemi kargo envanteri")]
    public ShipCargo shipCargo;

    [Tooltip("Eşyanın uçacağı hedef pozisyon (gemi güvertesi)")]
    public Transform collectTarget;

    [Header("Ayarlar")]
    [Tooltip("Toplama animasyonu süresi (saniye)")]
    public float collectAnimDuration = 1.0f;

    private void OnEnable()
    {
        if (!ActiveCollectors.Contains(this))
            ActiveCollectors.Add(this);
    }

    private void OnDisable()
    {
        ActiveCollectors.Remove(this);
    }

    /// <summary>
    /// Verilen pozisyona en yakın aktif ShipCollector'ı bulur.
    /// LiftingBag.NotifyShipCollector() tarafından çağrılır.
    /// </summary>
    public static ShipCollector FindNearest(Vector3 position)
    {
        ShipCollector nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < ActiveCollectors.Count; i++)
        {
            if (ActiveCollectors[i] == null) continue;
            float dist = Vector3.Distance(ActiveCollectors[i].transform.position, position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = ActiveCollectors[i];
            }
        }

        return nearest;
    }

    // --- Toplama animasyonu tracking ---
    private struct CollectAnimation
    {
        public GrabbableObject Item;
        public NetworkObject NetObj;
        public Vector3 StartPos;
        public Vector3 StartScale;
        public float Timer;
        public List<LiftingBag> Bags; // Balonlar (sökülecek)
    }

    private List<CollectAnimation> _activeAnimations = new List<CollectAnimation>();

    /// <summary>
    /// Sunucu tarafı: Yüzeye çıkan eşyayı toplama sürecini başlat.
    /// LiftingBag.NotifyShipCollector() tarafından çağrılır.
    /// </summary>
    [Server]
    public void ServerCollectFloatingItem(GrabbableObject item)
    {
        if (item == null || shipCargo == null) return;

        NetworkObject netObj = item.GetComponent<NetworkObject>();
        if (netObj == null) return;

        // Animasyon verisini hazırla
        var anim = new CollectAnimation
        {
            Item = item,
            NetObj = netObj,
            StartPos = item.transform.position,
            StartScale = item.transform.localScale,
            Timer = 0f,
            Bags = new List<LiftingBag>()
        };

        // Takılı balonları kaydet (animasyon bitince sökülecek)
        // GrabbableObject'ten balon listesine erişmek için biraz reflection gerekebilir
        // Ama public AttachedBagCount ve BreakObject pattern'ından yararlanabiliriz

        _activeAnimations.Add(anim);

        // Fizik etkileşimlerini devre dışı bırak
        Rigidbody rb = item.Rb;
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Tüm clientlara animasyon başlatma bildirimi
        ObserversStartCollectAnimation(netObj, item.transform.position);

        Debug.Log($"[ShipCollector] '{item.itemName}' toplama animasyonu başladı.");
    }

    /// <summary>
    /// Tüm clientlarda toplama animasyonu başlat bildirimi.
    /// </summary>
    [ObserversRpc]
    private void ObserversStartCollectAnimation(NetworkObject itemNetObj, Vector3 startPos)
    {
        // Client tarafı: animasyon tracking başlat
        if (itemNetObj == null) return;

        GrabbableObject item = itemNetObj.GetComponent<GrabbableObject>();
        if (item == null) return;

        var anim = new CollectAnimation
        {
            Item = item,
            NetObj = itemNetObj,
            StartPos = startPos,
            StartScale = item.transform.localScale,
            Timer = 0f
        };

        // Sadece client listesine ekle (sunucu kendi listesini yönetir)
        if (!base.IsServerInitialized)
        {
            _activeAnimations.Add(anim);
        }
    }

    void Update()
    {
        // Animasyonları güncelle (tüm clientlarda görsel, sunucuda + mantık)
        for (int i = _activeAnimations.Count - 1; i >= 0; i--)
        {
            var anim = _activeAnimations[i];

            if (anim.Item == null)
            {
                _activeAnimations.RemoveAt(i);
                continue;
            }

            anim.Timer += Time.deltaTime;
            float t = Mathf.Clamp01(anim.Timer / collectAnimDuration);

            // Ease-in: başta yavaş, sona doğru hızlanır
            float easedT = t * t;

            // Hedef pozisyon
            Vector3 targetPos = collectTarget != null ? collectTarget.position : transform.position;

            // Pozisyon ve scale animasyonu
            anim.Item.transform.position = Vector3.Lerp(anim.StartPos, targetPos, easedT);
            anim.Item.transform.localScale = Vector3.Lerp(anim.StartScale, Vector3.zero, easedT);

            _activeAnimations[i] = anim;

            // Animasyon tamamlandı
            if (t >= 1f)
            {
                // Scale'i geri yükle (despawn öncesi)
                anim.Item.transform.localScale = anim.StartScale;

                _activeAnimations.RemoveAt(i);

                // Sunucu tarafı: kargoya ekle + despawn
                if (base.IsServerInitialized)
                {
                    FinishCollect(anim);
                }
            }
        }
    }

    /// <summary>
    /// Sunucu tarafı: Animasyon bitti, eşyayı kargoya ekle ve despawn et.
    /// </summary>
    [Server]
    private void FinishCollect(CollectAnimation anim)
    {
        if (anim.Item == null || shipCargo == null) return;

        // Kargoya ekle
        shipCargo.AddItemToCargo(anim.Item);

        // Takılı balonları sök
        // GrabbableObject.BreakObject() zaten bunu yapıyor ama biz burada manual yapalım
        // çünkü eşya kırılmıyor, kargoya giriyor
        // attachedBags private olduğu için GrabbableObject üzerinden bir metod ekleyelim...
        // Aslında balon'un parent'ı eşya olduğu için despawn edilince balonlar da yetim kalır
        // En güvenli yol: balonları child olarak bulup detach et
        DetachBagsFromItem(anim.Item);

        // Eşyayı despawn et
        if (anim.NetObj != null)
        {
            PoolManager.Instance.DespawnNetwork(anim.NetObj);
        }

        Debug.Log($"[ShipCollector] '{anim.Item.itemName}' gemiye yüklendi!");
    }

    /// <summary>
    /// Eşyaya takılı balonları söker ve despawn eder.
    /// Balonlar artık child değil, sahnede bağımsız - TargetItem referansıyla bulunur.
    /// </summary>
    private void DetachBagsFromItem(GrabbableObject item)
    {
        if (item == null) return;

        LiftingBag[] allBags = FindObjectsByType<LiftingBag>(FindObjectsSortMode.None);
        foreach (var bag in allBags)
        {
            if (bag.TargetItem == item)
            {
                bag.Detach();
            }
        }
    }
}
