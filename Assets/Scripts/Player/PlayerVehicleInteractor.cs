using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Object;
using FishNet.Connection;

public class PlayerVehicleInteractor : NetworkBehaviour
{
    [Header("References")]
    public Transform playerCamera;
    public Transform holdPoint;
    public LayerMask grabMask = ~0; // Player'ı yoksaymalı
    
    [Header("Interaction Settings")]
    public float grabRange = 4f;
    public float minHoldDistance = 1.5f;
    public float maxHoldDistance = 6f;
    public float scrollSpeed = 2f;

    [Header("Vehicle Physics")]
    public float springForce = 50f; // Eşya uçurmaktan daha yumuşak olmalı
    public float damper = 5f;
    public float maxForce = 800f; // Çok abartı kuvvete ulaşmayı engelle
    public float rotationSpeed = 3f;

    private GrabbableObject heldVehicle;
    private Rigidbody heldRb;
    private float currentHoldDistance;
    private bool originalUseGravity;

    void Update()
    {
        if (!base.IsOwner) return;

        // Etkileşim Tuşu - Yeni Input Sistemi ile (Sol tık)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (heldVehicle == null)
            {
                TryGrabVehicle();
            }
            else
            {
                ReleaseVehicle();
            }
        }

        // Mouse scroll ile objeyi (aracı) yakınlaştırıp uzaklaştırma
        if (heldVehicle != null && Mouse.current != null)
        {
            float rawScroll = Mouse.current.scroll.y.ReadValue();
            if (Mathf.Abs(rawScroll) > 0.1f)
            {
                float scrollDir = Mathf.Sign(rawScroll);
                currentHoldDistance += scrollDir * scrollSpeed * Time.deltaTime * 5f;
                currentHoldDistance = Mathf.Clamp(currentHoldDistance, minHoldDistance, maxHoldDistance);
            }
        }
    }

    void FixedUpdate()
    {
        if (!base.IsOwner || heldRb == null) return;

        // --- PUSH / PULL MOVEMENT (3 BOYUTLU SÜRÜKLEME) ---
        Vector3 targetPosition = holdPoint != null ? 
            holdPoint.position + playerCamera.forward * currentHoldDistance : 
            playerCamera.position + playerCamera.forward * currentHoldDistance;

        Vector3 error = targetPosition - heldRb.position;

        // Araç çok uzağa kalırsa (Engellere takılırsa vs.) otomatik bırak
        if (error.magnitude > 6f)
        {
            ReleaseVehicle();
            return;
        }

        Vector3 force = (error * springForce) - (heldRb.linearVelocity * damper);

        if (force.magnitude > maxForce)
        {
            force = force.normalized * maxForce;
        }

        // Kütle ile çarp (Ağır olsa bile belli bir ivme kazanabilmesi için)
        force *= heldRb.mass;
        heldRb.AddForce(force, ForceMode.Force);

        // --- ROTATION (YÖN VE HİZALAMA) ---
        // Aracın kendi içinde bir "tutma yönü" (playerFacingNode) varsa o kısım oyuncuya bakacak şekilde çevrilir
        Vector3 flatPlayerPos = new Vector3(playerCamera.position.x, heldRb.position.y, playerCamera.position.z);
        Vector3 directionToPlayer = (flatPlayerPos - heldRb.position);
        
        if (directionToPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion targetCartRotation;
            
            if (heldVehicle.playerFacingNode != null)
            {
                Vector3 faceDir = (heldVehicle.playerFacingNode.position - heldRb.position);
                faceDir.y = 0; 
                
                if (faceDir.sqrMagnitude > 0.01f)
                {
                    Vector3 localFaceDir = heldRb.transform.InverseTransformDirection(faceDir.normalized);
                    localFaceDir.y = 0;

                    targetCartRotation = Quaternion.LookRotation(directionToPlayer.normalized, Vector3.up) * 
                                         Quaternion.Inverse(Quaternion.LookRotation(localFaceDir.normalized, Vector3.up));
                }
                else
                {
                    targetCartRotation = Quaternion.LookRotation(directionToPlayer.normalized, Vector3.up);
                }
            }
            else
            {
                targetCartRotation = Quaternion.LookRotation(directionToPlayer.normalized, Vector3.up);
            }
            
            Quaternion smoothRotation = Quaternion.Slerp(heldRb.rotation, targetCartRotation, Time.fixedDeltaTime * rotationSpeed);
            heldRb.MoveRotation(smoothRotation);
            
            // Jitter/Titremeyi engellemek için dönüş hızını sıfırla
            heldRb.angularVelocity = Vector3.zero;
        }
    }

    void TryGrabVehicle()
    {
        RaycastHit hit;
        if (Physics.Raycast(playerCamera.position, playerCamera.forward, out hit, grabRange, grabMask))
        {
            GrabbableObject grabbable = hit.collider.GetComponentInParent<GrabbableObject>();
            
            // Sadece 'isHeavyVehicle' olarak işaretlenmiş objeleri tutabiliriz
            if (grabbable != null && grabbable.isHeavyVehicle)
            {
                if (grabbable.AttachedBagCount > 0)
                {
                    Debug.Log("Bu araca balon takılı, taşınamaz.");
                    return;
                }

                heldVehicle = grabbable;
                heldRb = grabbable.GetComponent<Rigidbody>();
                
                // Network Sahipliğini İste (Eşya tutma ile aynı mantık)
                NetworkObject netObj = heldVehicle.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    ServerTakeOwnership(netObj);
                }

                Vector3 referencePos = holdPoint != null ? holdPoint.position : playerCamera.position;
                currentHoldDistance = Vector3.Distance(referencePos, heldRb.position);
                currentHoldDistance = Mathf.Clamp(currentHoldDistance, minHoldDistance, maxHoldDistance);

                originalUseGravity = heldRb.useGravity;
                heldRb.useGravity = false; // Havada süzülmesi için
                heldRb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }
    }

    void ReleaseVehicle()
    {
        if (heldVehicle == null) return;

        if (heldRb != null)
        {
            heldRb.useGravity = originalUseGravity;
        }

        NetworkObject netObj = heldVehicle.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            ServerRemoveOwnership(netObj);
        }

        heldVehicle = null;
        heldRb = null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerTakeOwnership(NetworkObject targetNetObj, NetworkConnection caller = null)
    {
        if (targetNetObj != null)
        {
            targetNetObj.GiveOwnership(caller);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerRemoveOwnership(NetworkObject targetNetObj)
    {
        if (targetNetObj != null)
        {
            targetNetObj.RemoveOwnership();
        }
    }
}
