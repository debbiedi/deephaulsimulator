using UnityEngine;

/// <summary>
/// Karakter değişim zone'u. Sahneye konulacak bir trigger alanı.
/// Oyuncu bu alana girdiğinde DalgicDegisim component'ine bildirilir
/// ve ekranda F tuşu prompt'u gösterilir.
/// 
/// Kullanım:
/// 1. Sahnede boş bir GameObject oluşturun
/// 2. BoxCollider ekleyin ve Is Trigger = true yapın
/// 3. Bu scripti ekleyin
/// 4. Zone'u istediğiniz yere taşıyın
/// </summary>
public class CharacterSwapZone : MonoBehaviour
{
    [Header("Ayarlar")]
    [Tooltip("Ekranda gösterilecek mesaj")]
    public string interactionText = "Dalgıç Kıyafetini Giy";

    [Tooltip("Geri dönüşte gösterilecek mesaj")]
    public string revertText = "Normal Kıyafete Dön";

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[CharacterSwapZone] OnTriggerEnter! Giren obje: {other.gameObject.name} (Tag: {other.tag})");
        
        // Player'ın DalgicDegisim component'ini bul
        DalgicDegisim degisim = other.GetComponentInParent<DalgicDegisim>();
        if (degisim != null)
        {
            Debug.Log("[CharacterSwapZone] DalgicDegisim bulundu! EnterZone çağrılıyor...");
            degisim.EnterZone(this);
        }
        else
        {
            Debug.LogWarning($"[CharacterSwapZone] {other.gameObject.name} objesinde DalgicDegisim bulunamadı!");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        DalgicDegisim degisim = other.GetComponentInParent<DalgicDegisim>();
        if (degisim != null)
        {
            degisim.ExitZone();
        }
    }
}
