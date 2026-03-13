using UnityEngine;
using Steamworks;

/// <summary>
/// Steamworks.NET başlatma yöneticisi.
/// Uygulama başladığında Steam API'yi başlatır, kapanırken kapatır.
/// Bu script sahnedeki herhangi bir objeye eklenmeli ve sahne yüklendiğinde çalışmalıdır.
/// 
/// Resmi Steamworks.NET SteamManager'ından basitleştirilmiş versiyon.
/// </summary>
[DisallowMultipleComponent]
public class SteamManager : MonoBehaviour
{
    private static SteamManager _instance;
    private static bool _initialized = false;

    public static bool Initialized
    {
        get { return _initialized; }
    }

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (!Packsize.Test())
        {
            Debug.LogError("[Steamworks.NET] Packsize hatalı! Platformunuz desteklenmiyor olabilir.");
            return;
        }

        if (!DllCheck.Test())
        {
            Debug.LogError("[Steamworks.NET] DLL kontrol hatası!");
            return;
        }

        try
        {
            _initialized = SteamAPI.Init();

            if (!_initialized)
            {
                Debug.LogError("[SteamManager] SteamAPI.Init() başarısız! Steam açık olduğundan ve steam_appid.txt dosyasının proje kök dizininde olduğundan emin olun.");
                return;
            }

            Debug.Log($"[SteamManager] ✅ Steam başarıyla başlatıldı! Oyuncu: {SteamFriends.GetPersonaName()}");
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError($"[SteamManager] Steam DLL bulunamadı: {e.Message}. steam_api64.dll veya steam_api.dll proje dizininde olmalı.");
            return;
        }
    }

    private void Update()
    {
        if (!_initialized) return;
        SteamAPI.RunCallbacks();
    }

    private void OnApplicationQuit()
    {
        if (!_initialized) return;
        SteamAPI.Shutdown();
        _initialized = false;
        Debug.Log("[SteamManager] Steam kapatıldı.");
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
