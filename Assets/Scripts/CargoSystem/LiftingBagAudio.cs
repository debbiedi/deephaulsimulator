using UnityEngine;

/// <summary>
/// Kaldırma balonu ses efektleri.
/// Player prefabına eklenir. InflationMiniGame eventlerini dinler.
///
/// Kurulum:
/// 1. Player prefabına AudioSource ekleyin.
/// 2. Bu scripti ekleyin ve ses kliplerini atayın.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class LiftingBagAudio : MonoBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Aynı player üzerindeki InflationMiniGame")]
    public InflationMiniGame miniGame;

    [Header("Ses Klipleri")]
    [Tooltip("Pompa basma sesi")]
    public AudioClip pumpClip;

    [Tooltip("Balon takma sesi")]
    public AudioClip attachClip;

    [Tooltip("Balon tam şişti sesi")]
    public AudioClip inflatedClip;

    [Tooltip("Eşya yüzeye çıktı sesi")]
    public AudioClip surfaceClip;

    [Tooltip("Gemiye yükleme sesi")]
    public AudioClip collectClip;

    [Header("Ses Ayarları")]
    [Range(0f, 1f)]
    public float pumpVolume = 0.5f;
    [Range(0f, 1f)]
    public float effectVolume = 0.8f;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D ses (kendi oyuncumuz için)
    }

    private void Start()
    {
        if (miniGame != null)
        {
            miniGame.OnPumped += OnPumped;
            miniGame.OnMiniGameStarted += OnMiniGameStarted;
            miniGame.OnMiniGameEnded += OnMiniGameEnded;
        }
    }

    private void OnDestroy()
    {
        if (miniGame != null)
        {
            miniGame.OnPumped -= OnPumped;
            miniGame.OnMiniGameStarted -= OnMiniGameStarted;
            miniGame.OnMiniGameEnded -= OnMiniGameEnded;
        }
    }

    private void OnPumped()
    {
        PlayClip(pumpClip, pumpVolume);
    }

    private void OnMiniGameStarted()
    {
        PlayClip(attachClip, effectVolume);
    }

    private void OnMiniGameEnded()
    {
        // Mini-game bitti - eğer balon tam şiştiyse inflated sesi çal
        if (miniGame.CurrentBag != null && miniGame.CurrentBag.InflationLevel.Value >= 1f)
        {
            PlayClip(inflatedClip, effectVolume);
        }
    }

    /// <summary>
    /// Eşya yüzeye çıktığında çağrılır (ShipCollector'dan tetiklenebilir).
    /// </summary>
    public void PlaySurfaceSound()
    {
        PlayClip(surfaceClip, effectVolume);
    }

    /// <summary>
    /// Eşya gemiye yüklendiğinde çağrılır.
    /// </summary>
    public void PlayCollectSound()
    {
        PlayClip(collectClip, effectVolume);
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (clip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(clip, volume);
        }
    }
}
