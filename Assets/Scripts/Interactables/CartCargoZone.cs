using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;

public class CartCargoZone : NetworkBehaviour
{
    private List<GrabbableObject> itemsInCart = new List<GrabbableObject>();

    [Tooltip("Arabaya binen objeler buranın altına taşınacak")]
    public Transform cargoContainer;

    [Tooltip("Eşya hız limiti. Bu hızın altındaysa araca sabitlenir.")]
    public float sleepVelocityThreshold = 0.5f;

    private Rigidbody cartRb;
    private bool wasCartHeld;

    private void Start()
    {
        CartController cart = GetComponentInParent<CartController>();
        if (cart != null) cartRb = cart.GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!IsServerInitialized) return;

        // Sepet şu an bir oyuncu tarafından tutuluyor mu?
        CartController cartCtrl = GetComponentInParent<CartController>();
        bool cartIsHeld = cartCtrl != null && cartCtrl.Owner.IsValid;
        
        // Sepet yeni tutulmaya başlandıysa, içindeki TÜM serbest eşyaları anında sabitle
        if (cartIsHeld && !wasCartHeld)
        {
            FixAllItemsImmediately();
        }
        wasCartHeld = cartIsHeld;

        // Sepetteki eşyaları kontrol et ve uykuya/sabit duruma geçenleri parent yap
        for (int i = 0; i < itemsInCart.Count; i++)
        {
            var item = itemsInCart[i];
            if (item == null) continue;

            NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
            if (itemNetObj == null) continue;

            // Eğer obje birisi tarafından tutulmuyorsa sabitle
            if (!itemNetObj.Owner.IsValid) 
            {
                if (!item.isFixedInCart.Value && item.Rb != null)
                {
                    // Eşyanın sepete göre bağıl hızını hesapla.
                    // Böylece sepet hızlı hareket etse bile, içinde oturmuş eşya düşük bağıl hıza sahip olur
                    // ve doğru (yerleşmiş) pozisyonda sabitlenir.
                    Vector3 relativeVelocity = item.Rb.linearVelocity;
                    if (cartRb != null) relativeVelocity -= cartRb.linearVelocity;
                    
                    if (relativeVelocity.magnitude < sleepVelocityThreshold && itemNetObj.transform.parent != this.transform)
                    {
                        item.isFixedInCart.Value = true;
                        itemNetObj.SetParent(this.NetworkObject);
                        item.UpdatePhysicsAuthority();
                        RpcIgnoreCollisions(itemNetObj, true);
                    }
                }
            }
            else
            {
                // Birisi objeyi eline alırsa (Owner isValid) artık sabit değildir
                if (item.isFixedInCart.Value)
                {
                    item.isFixedInCart.Value = false;
                    item.UpdatePhysicsAuthority();
                    RpcIgnoreCollisions(itemNetObj, false);
                }
            }
        }
    }

    [ObserversRpc]
    private void RpcIgnoreCollisions(NetworkObject itemNetObj, bool ignore)
    {
        if (itemNetObj == null) return;
        
        CartController cart = GetComponentInParent<CartController>();
        if (cart == null) return;

        Collider[] cartCols = cart.GetComponentsInChildren<Collider>();
        Collider[] itemCols = itemNetObj.GetComponentsInChildren<Collider>();

        foreach (var c1 in cartCols)
        {
            if (c1.isTrigger) continue;
            foreach (var c2 in itemCols)
            {
                if (c2.isTrigger) continue;
                Physics.IgnoreCollision(c1, c2, ignore);
            }
        }
    }

    /// <summary>
    /// Sepet tutulduğunda içindeki tüm serbest eşyaları anında kinematic yapıp sepete bağlar.
    /// Böylece sepet hareket ettirildiğinde eşyalar geriye kalıp yandan çıkamaz.
    /// </summary>
    private void FixAllItemsImmediately()
    {
        for (int i = 0; i < itemsInCart.Count; i++)
        {
            var item = itemsInCart[i];
            if (item == null || item.isFixedInCart.Value) continue;

            NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
            if (itemNetObj == null) continue;
            if (itemNetObj.Owner.IsValid) continue; // Birisi tutuyorsa dokunma

            item.isFixedInCart.Value = true;
            itemNetObj.SetParent(this.NetworkObject);
            item.UpdatePhysicsAuthority();
            RpcIgnoreCollisions(itemNetObj, true);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        GrabbableObject item = other.GetComponentInParent<GrabbableObject>();
        if (item != null && item != this.GetComponentInParent<GrabbableObject>() && !itemsInCart.Contains(item))
        {
            if (!item.isHeavyVehicle)
            {
                itemsInCart.Add(item);
                
                // Eşyanın çarpışma algılamasını iyileştir: hızlı hareket sırasında sepetin duvarlarından geçmesini önle
                if (item.Rb != null)
                {
                    item.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        GrabbableObject item = other.GetComponentInParent<GrabbableObject>();
        if (item != null && itemsInCart.Contains(item))
        {
            itemsInCart.Remove(item);
            
            // Çıkarken parent'ı sıfırla ve serbest bırak
            NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
            if (itemNetObj != null && IsServerInitialized)
            {
                if (itemNetObj.transform.parent == this.transform)
                {
                    itemNetObj.UnsetParent();
                }
                
                if (item.isFixedInCart.Value)
                {
                    item.isFixedInCart.Value = false;
                    item.UpdatePhysicsAuthority();
                    RpcIgnoreCollisions(itemNetObj, false);
                }
            }
        }
    }
}
