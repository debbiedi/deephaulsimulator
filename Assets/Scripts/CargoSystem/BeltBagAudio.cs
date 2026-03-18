using UnityEngine;
using FishNet.Object;

/// <summary>
/// Kemer çantası ses efektleri.
/// Player prefabına eklenir, BeltBagInventory event'lerini dinler.
/// </summary>
public class BeltBagAudio : NetworkBehaviour
{
    [Header("Referanslar")]
    public BeltBagInventory inventory;
    public AudioSource audioSource;

    [Header("Ses Klipleri")]
    [Tooltip("Eşya çantaya toplandığında çalan ses (şıngırtı/fermuar)")]
    public AudioClip collectSound;

    [Tooltip("Çanta dolu olduğunda çalan uyarı sesi")]
    public AudioClip bagFullSound;

    [Header("Ayarlar")]
    [Range(0f, 1f)]
    public float volume = 0.7f;

    void Start()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded += OnItemCollected;
        }
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= OnItemCollected;
        }
    }

    private void OnItemCollected(ItemData item)
    {
        if (base.IsOwner)
        {
            ServerPlaySound(true);
        }
    }

    public void PlayBagFullSound()
    {
        if (base.IsOwner)
        {
            ServerPlaySound(false);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerPlaySound(bool isCollectSound)
    {
        ObserversPlaySound(isCollectSound);
    }

    [ObserversRpc(BufferLast = false)]
    private void ObserversPlaySound(bool isCollectSound)
    {
        AudioClip clip = isCollectSound ? collectSound : bagFullSound;
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }
}
