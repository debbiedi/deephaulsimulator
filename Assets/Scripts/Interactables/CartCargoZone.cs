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

    private void Update()
    {
        if (!IsServerInitialized) return;

        // Sepetteki eşyaları kontrol et ve uykuya/sabit duruma geçenleri parent yap
        for (int i = 0; i < itemsInCart.Count; i++)
        {
            var item = itemsInCart[i];
            if (item == null) continue;

            NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
            if (itemNetObj == null) continue;

            // Eğer obje birisi tarafından tutulmuyorsa ve hızı düşükse sabitle
            if (!itemNetObj.Owner.IsValid) 
            {
                if (!item.isFixedInCart.Value && item.Rb != null && item.Rb.linearVelocity.magnitude < sleepVelocityThreshold)
                {
                    // Parent olarak sepeti ayarla ve fiziği dondur (Kinematic yap)
                    if (itemNetObj.transform.parent != this.transform)
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
            if (c1.isTrigger) continue; // Triggerları (CargoZone alanı vs.) iptal etme, yoksa item sepetten çıktığını sanıp düşer
            foreach (var c2 in itemCols)
            {
                if (c2.isTrigger) continue;
                Physics.IgnoreCollision(c1, c2, ignore);
            }
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
