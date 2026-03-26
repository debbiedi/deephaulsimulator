using UnityEngine;
using FishNet.Object;

[RequireComponent(typeof(Rigidbody))]
public class CartController : NetworkBehaviour
{
    private Rigidbody rb;

    [Header("Stabilization")]
    [Tooltip("Arabanın ağırlık merkezini ne kadar aşağıya çekeceğimiz")]
    public Vector3 centerOfMassOffset = new Vector3(0, -1.5f, 0);

    [Tooltip("Arabayı dik tutmak için uygulanacak tork gücü")]
    public float uprightTorque = 100f;
    
    [Tooltip("Dik tutma sırasında çok sallanmayı önlemek için açısal sönümleme")]
    public float uprightDamper = 15f;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // Ağırlık merkezini ayarla
        rb.centerOfMass = centerOfMassOffset;
    }

    private void FixedUpdate()
    {
        // Sahibi değılsek (Server ya da Owner değilsek) fizik çalıştırma
        if (!base.IsOwner && !base.IsServerInitialized) return;

        ApplyUprightForce();
    }

    private void ApplyUprightForce()
    {
        // Arabanın yukarı vektörünü (Y) dünyanın yukarı vektörüne (0,1,0) hizalamaya çalış.
        Quaternion currentRot = transform.rotation;
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, Vector3.up) * currentRot;

        // Döneceğimiz açı ve ekseni bulalım
        Quaternion deltaRot = targetRot * Quaternion.Inverse(currentRot);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360f;

        // Eğer açı çok düşükse hiç zorlama yapma
        if (Mathf.Abs(angle) > 1f)
        {
            // Tork = (Açı * Eksen * Güç) - (Açısal Hız * Damper)
            Vector3 torque = (axis * (angle * uprightTorque)) - (rb.angularVelocity * uprightDamper);
            rb.AddTorque(torque, ForceMode.Acceleration);
        }
    }
}
