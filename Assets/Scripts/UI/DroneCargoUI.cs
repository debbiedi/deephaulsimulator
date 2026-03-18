using UnityEngine;
using TMPro;

/// <summary>
/// Drone kargo HUD'u. Oyuncu drone'a yaklastiginda goruntulenir.
/// Kargo agirligini, esya sayisini, drone durumunu ve kalkis zamanlayicisini gosterir.
///
/// Kurulum:
/// 1. GameHUD Canvas'ina bir UI paneli ekleyin (DroneCargoPanel).
/// 2. Icine: weightText, stateText, timerText, itemCountText, hintText
/// 3. Bu scripti panele ekleyin ve TMP referanslarini atayin.
/// 4. Panel baslangicta gizli olmali.
/// </summary>
public class DroneCargoUI : MonoBehaviour
{
    [Header("UI Elementleri")]
    [Tooltip("Kargo agirlik metni (orn: '12.5 / 20 kg')")]
    public TextMeshProUGUI weightText;

    [Tooltip("Drone durum metni (orn: 'Bekliyor', 'Yolda', vb.)")]
    public TextMeshProUGUI stateText;

    [Tooltip("Kalkis zamanlayicisi metni (orn: 'Kalkis: 3.2s')")]
    public TextMeshProUGUI timerText;

    [Tooltip("Esya sayisi metni")]
    public TextMeshProUGUI itemCountText;

    [Tooltip("Ipucu metni ([G] Esya Yukle)")]
    public TextMeshProUGUI hintText;

    [Header("Panel")]
    [Tooltip("Ana HUD paneli")]
    public GameObject dronePanel;

    [Header("Ayarlar")]
    [Tooltip("HUD'un goruntulenmesi icin maksimum mesafe")]
    public float displayRange = 8f;

    private CarrierDrone _trackedDrone;
    private bool _isVisible;
    private Transform _playerCamera;

    void Update()
    {
        // Kamerayi bul (henuz atanmamissa)
        if (_playerCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null) _playerCamera = cam.transform;
            else return;
        }

        // En yakin drone'u bul
        CarrierDrone nearest = FindNearestDrone();

        if (nearest != _trackedDrone)
        {
            // Onceki drone'dan event'leri coz
            if (_trackedDrone != null)
            {
                _trackedDrone.OnCargoChanged -= RefreshHUD;
                _trackedDrone.OnStateChanged -= HandleStateChanged;
            }

            _trackedDrone = nearest;

            // Yeni drone'a event'leri bagla
            if (_trackedDrone != null)
            {
                _trackedDrone.OnCargoChanged += RefreshHUD;
                _trackedDrone.OnStateChanged += HandleStateChanged;
            }
        }

        // Goruntuleme kontrolu
        bool shouldShow = _trackedDrone != null;
        if (shouldShow != _isVisible)
        {
            _isVisible = shouldShow;
            if (dronePanel != null)
                dronePanel.SetActive(_isVisible);
        }

        if (_isVisible)
        {
            UpdateHUD();
        }
    }

    private CarrierDrone FindNearestDrone()
    {
        if (_playerCamera == null) return null;

        CarrierDrone nearest = null;
        float nearestDist = displayRange;

        for (int i = 0; i < CarrierDrone.ActiveDrones.Count; i++)
        {
            var drone = CarrierDrone.ActiveDrones[i];
            if (drone == null) continue;

            float dist = Vector3.Distance(_playerCamera.position, drone.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = drone;
            }
        }

        return nearest;
    }

    private void HandleStateChanged(DroneState newState)
    {
        RefreshHUD();
    }

    private void RefreshHUD()
    {
        UpdateHUD();
    }

    private void UpdateHUD()
    {
        if (_trackedDrone == null) return;

        // Agirlik
        if (weightText != null)
            weightText.text = $"Kargo: {_trackedDrone.CurrentWeight.Value:F1} / {_trackedDrone.maxCargoWeight} kg";

        // Esya sayisi
        if (itemCountText != null)
            itemCountText.text = $"Esya: {_trackedDrone.CargoItems.Count} / {_trackedDrone.maxCargoSlots}";

        // Durum metni
        if (stateText != null)
            stateText.text = GetStateDisplayName(_trackedDrone.State.Value);

        // Zamanlayici
        if (timerText != null)
        {
            if (_trackedDrone.State.Value == DroneState.Loading)
            {
                float remaining = _trackedDrone.DepartureTimer.Value;
                timerText.text = $"Kalkis: {remaining:F1}s";
                timerText.gameObject.SetActive(true);
            }
            else
            {
                timerText.gameObject.SetActive(false);
            }
        }

        // Ipucu metni
        if (hintText != null)
        {
            DroneState state = _trackedDrone.State.Value;
            if (state == DroneState.Hovering || state == DroneState.Loading)
                hintText.text = "[G] Esya Yukle";
            else if (state == DroneState.Arriving)
                hintText.text = "Drone geliyor...";
            else
                hintText.text = "";
        }
    }

    private string GetStateDisplayName(DroneState state)
    {
        switch (state)
        {
            case DroneState.Idle:       return "Hazir [T] Cagir";
            case DroneState.Arriving:   return "Geliyor...";
            case DroneState.Hovering:   return "Bekliyor";
            case DroneState.Loading:    return "Yukleniyor...";
            case DroneState.Departing:  return "Kalkiyor";
            case DroneState.Delivering: return "Teslim Ediyor";
            case DroneState.Returning:  return "Donuyor";
            default:                    return "Bilinmiyor";
        }
    }

    void OnDestroy()
    {
        if (_trackedDrone != null)
        {
            _trackedDrone.OnCargoChanged -= RefreshHUD;
            _trackedDrone.OnStateChanged -= HandleStateChanged;
        }
    }
}
