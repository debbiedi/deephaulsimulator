using UnityEngine;
using System.Collections;

/// <summary>
/// Bu scripti efektlere, seslere veya UI elemanlarına ekleyebilirsiniz.
/// İşleri bitince kendilerini otomatik olarak havuza iade eder.
/// </summary>
public class AutoReturnToPool : MonoBehaviour
{
    public enum ReturnCondition { TimeDelay, ParticleFinished, AudioFinished }
    
    [Tooltip("Objenin havuza dönüş şartı nedir?")]
    public ReturnCondition condition;
    
    [Tooltip("TimeDelay seçiliyse kaç saniye sonra dönecek?")]
    public float delayInSeconds = 2f;

    private ParticleSystem partSys;
    private AudioSource audSource;

    private Coroutine returnCoroutine;

    void Awake()
    {
        // Bileşenleri baştan bir kez Cache'le
        if (condition == ReturnCondition.ParticleFinished)
            partSys = GetComponent<ParticleSystem>();

        if (condition == ReturnCondition.AudioFinished)
            audSource = GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        if (returnCoroutine != null)
            StopCoroutine(returnCoroutine);

        switch (condition)
        {
            case ReturnCondition.TimeDelay:
                returnCoroutine = StartCoroutine(ReturnAfterTime(delayInSeconds));
                break;
            case ReturnCondition.ParticleFinished:
                returnCoroutine = StartCoroutine(ReturnWhenParticleFinished());
                break;
            case ReturnCondition.AudioFinished:
                returnCoroutine = StartCoroutine(ReturnWhenAudioFinished());
                break;
        }
    }

    void OnDisable()
    {
        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
            returnCoroutine = null;
        }
    }

    IEnumerator ReturnAfterTime(float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnNow();
    }

    IEnumerator ReturnWhenParticleFinished()
    {
        // Partikülün bitmesini bekle
        while (partSys != null && partSys.IsAlive(true))
        {
            yield return null; // Bir sonraki frame'e geç
        }
        ReturnNow();
    }

    IEnumerator ReturnWhenAudioFinished()
    {
        // Sesin bitmesini bekle
        while (audSource != null && audSource.isPlaying)
        {
            yield return null;
        }
        ReturnNow();
    }

    private void ReturnNow()
    {
        // Eğer PoolManager yoksa Destroy et, varsa DespawnLocal ile geri ver
        if (PoolManager.Instance != null)
        {
            PoolManager.Instance.DespawnLocal(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}