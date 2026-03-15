using UnityEngine;
using TMPro;

/// <summary>
/// Tek bir toplama bildirimi UI elemanı.
/// BeltBagUI tarafından prefab olarak oluşturulur.
/// Fade-in, bekle, fade-out animasyonu yapar ve kendini yok eder.
/// </summary>
public class PickupNotification : MonoBehaviour
{
    [Header("UI Referansları")]
    public TextMeshProUGUI titleText;      // "+1 Altın Yüzük"
    public TextMeshProUGUI subtitleText;   // "Değer: 250₺"
    public CanvasGroup canvasGroup;        // Fade efekti için

    [Header("Animasyon")]
    public float fadeInDuration = 0.2f;
    public float fadeOutDuration = 0.5f;

    private float _displayDuration;
    private float _timer;

    private enum State { FadeIn, Display, FadeOut }
    private State _state = State.FadeIn;

    public void Setup(string title, string subtitle, float duration)
    {
        if (titleText != null) titleText.text = title;
        if (subtitleText != null) subtitleText.text = subtitle;
        _displayDuration = duration;
        _timer = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    void Update()
    {
        _timer += Time.deltaTime;

        switch (_state)
        {
            case State.FadeIn:
                if (canvasGroup != null)
                    canvasGroup.alpha = Mathf.Clamp01(_timer / fadeInDuration);
                if (_timer >= fadeInDuration)
                {
                    _state = State.Display;
                    _timer = 0f;
                }
                break;

            case State.Display:
                if (_timer >= _displayDuration)
                {
                    _state = State.FadeOut;
                    _timer = 0f;
                }
                break;

            case State.FadeOut:
                if (canvasGroup != null)
                    canvasGroup.alpha = 1f - Mathf.Clamp01(_timer / fadeOutDuration);
                if (_timer >= fadeOutDuration)
                    Destroy(gameObject);
                break;
        }
    }
}
