using UnityEngine;
using FishNet.Object;

/// <summary>
/// Drone isik ve su alti baloncuk efektlerini yonetir.
/// On tarafta mavi spot isik, yanlarda beyaz spot isiklar.
/// Su altindayken pervanelerden baloncuk cikar.
/// </summary>
public class DroneLightAndEffects : NetworkBehaviour
{
    [Header("Isiklar")]
    [Tooltip("On mavi spot isik")]
    public Light frontLight;

    [Tooltip("Sol beyaz spot isik")]
    public Light leftLight;

    [Tooltip("Sag beyaz spot isik")]
    public Light rightLight;

    [Header("Baloncuk Efektleri")]
    [Tooltip("Arka pervane baloncuk particle system'leri")]
    public ParticleSystem[] bubbleEffects;

    [Header("Ayarlar")]
    [Tooltip("Isiklar sadece su altinda mi yansin?")]
    public bool lightsOnlyUnderwater = false;

    [Tooltip("Su alti kontrol araligi (saniye)")]
    public float checkInterval = 0.25f;

    private bool _isUnderwater;
    private float _checkTimer;

    void Update()
    {
        if (!base.IsSpawned) return;

        _checkTimer -= Time.deltaTime;
        if (_checkTimer <= 0f)
        {
            _checkTimer = checkInterval;
            CheckUnderwaterState();
        }
    }

    private void CheckUnderwaterState()
    {
        WaterZone zone = WaterZone.GetZoneForPosition(transform.position);
        bool underwater = zone != null && zone.IsUnderwater(transform.position.y);

        if (underwater != _isUnderwater)
        {
            _isUnderwater = underwater;
            UpdateEffects();
        }
    }

    private void UpdateEffects()
    {
        // Baloncuklar: sadece su altinda
        if (bubbleEffects != null)
        {
            for (int i = 0; i < bubbleEffects.Length; i++)
            {
                if (bubbleEffects[i] == null) continue;

                if (_isUnderwater)
                    bubbleEffects[i].Play();
                else
                    bubbleEffects[i].Stop();
            }
        }

        // Isiklar: su altinda veya her zaman (ayara gore)
        if (lightsOnlyUnderwater)
        {
            if (frontLight != null) frontLight.enabled = _isUnderwater;
            if (leftLight != null) leftLight.enabled = _isUnderwater;
            if (rightLight != null) rightLight.enabled = _isUnderwater;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Baslangicta isiklari ac, baloncuklari kapat
        if (frontLight != null) frontLight.enabled = !lightsOnlyUnderwater;
        if (leftLight != null) leftLight.enabled = !lightsOnlyUnderwater;
        if (rightLight != null) rightLight.enabled = !lightsOnlyUnderwater;

        if (bubbleEffects != null)
        {
            for (int i = 0; i < bubbleEffects.Length; i++)
            {
                if (bubbleEffects[i] != null)
                    bubbleEffects[i].Stop();
            }
        }
    }
}
