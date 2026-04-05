using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using System.Collections.Generic;

/// <summary>
/// Vinç sepeti (crane basket). Gemiden deniz dibine indirilir, dalgıçlar ağır eşyaları içine bırakır,
/// sonra vinç yukarı çeker ve eşyalar ShipCargo'ya aktarılır.
///
/// CartCargoZone pattern'ını takip eder (trigger-based eşya yakalama, parent yapma, sabitlenme).
/// CarrierDrone pattern'ını takip eder (state-machine + SyncVar networking).
///
/// Kurulum:
/// 1. Sepet prefabına bu scripti ekleyin (NetworkObject + NetworkTransform gerekli).
/// 2. İçinde BoxCollider (Is Trigger) olan bir child obje ekleyin — eşya algılama alanı.
/// 3. Inspector'dan deckPoint ve lowerPoint Transform'larını atayın.
/// 4. shipCargo referansını gemideki ShipCargo component'ına atayın.
/// </summary>
public class CraneBasket : NetworkBehaviour
{
    [Header("Hareket Ayarları")]
    [Tooltip("Güvertedeki park pozisyonu")]
    public Transform deckPoint;

    [Tooltip("Deniz dibindeki hedef pozisyon")]
    public Transform lowerPoint;

    [Tooltip("Sepet dikey hareket hızı (m/s)")]
    public float moveSpeed = 3f;

    [Header("Kargo Ayarları")]
    [Tooltip("Gemi kargo envanteri referansı")]
    public ShipCargo shipCargo;

    [Tooltip("Maksimum taşıma kapasitesi (kg)")]
    public float maxWeight = 100f;

    [Tooltip("Maksimum eşya slotu")]
    public int maxSlots = 8;

    [Header("Boşaltma")]
    [Tooltip("Boşaltma süresi (saniye)")]
    public float unloadDuration = 2f;

    [Header("Ses (Opsiyonel)")]
    [Tooltip("Vinç motor sesi")]
    public AudioSource motorAudioSource;

    [Tooltip("Eşya düşme sesi")]
    public AudioClip itemDropSound;

    [Tooltip("Boşaltma tamamlanma sesi")]
    public AudioClip unloadCompleteSound;

    // --- SYNCED STATE ---
    public readonly SyncVar<CraneBasketState> State = new SyncVar<CraneBasketState>(CraneBasketState.Idle);
    public readonly SyncVar<float> NormalizedHeight = new SyncVar<float>(1f); // 0=deniz dibi, 1=güverte
    public readonly SyncVar<float> CurrentWeight = new SyncVar<float>(0f);
    public readonly SyncVar<int> ItemCount = new SyncVar<int>(0);

    // --- EVENTS ---
    public event Action<CraneBasketState> OnStateChanged;
    public event Action OnCargoChanged;

    // --- PRIVATE ---
    private List<GrabbableObject> _itemsInBasket = new List<GrabbableObject>();
    private float _unloadTimer;

    public override void OnStartClient()
    {
        base.OnStartClient();
        State.OnChange += OnStateValueChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        State.OnChange -= OnStateValueChanged;
    }

    private void OnStateValueChanged(CraneBasketState prev, CraneBasketState next, bool asServer)
    {
        OnStateChanged?.Invoke(next);
        UpdateMotorSound(next);
    }

    private void Update()
    {
        if (base.IsServerInitialized)
        {
            ServerUpdate();
        }

        // Client tarafı: pozisyon interpolasyonu
        if (!base.IsServerInitialized && deckPoint != null && lowerPoint != null)
        {
            transform.position = Vector3.Lerp(lowerPoint.position, deckPoint.position, NormalizedHeight.Value);
        }
    }

    /// <summary>
    /// Sunucu tarafı: State machine güncelleme.
    /// </summary>
    private void ServerUpdate()
    {
        switch (State.Value)
        {
            case CraneBasketState.Lowering:
                MoveTo(lowerPoint.position, 0f);
                if (NormalizedHeight.Value <= 0.001f)
                {
                    NormalizedHeight.Value = 0f;
                    State.Value = CraneBasketState.Lowered;
                    UnfixAllItems();
                }
                break;

            case CraneBasketState.Raising:
                FixAllItems();
                MoveTo(deckPoint.position, 1f);
                if (NormalizedHeight.Value >= 0.999f)
                {
                    NormalizedHeight.Value = 1f;
                    State.Value = CraneBasketState.Unloading;
                    _unloadTimer = unloadDuration;
                }
                break;

            case CraneBasketState.Unloading:
                _unloadTimer -= Time.deltaTime;
                if (_unloadTimer <= 0f)
                {
                    UnloadCargo();
                    State.Value = CraneBasketState.Idle;
                }
                break;
        }
    }

    /// <summary>
    /// Sepeti hedef pozisyona doğru hareket ettirir.
    /// </summary>
    private void MoveTo(Vector3 targetPos, float targetNormalized)
    {
        if (deckPoint == null || lowerPoint == null) return;

        float step = moveSpeed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, step);

        // Normalize edilmiş yüksekliği güncelle
        float totalDist = Vector3.Distance(lowerPoint.position, deckPoint.position);
        if (totalDist > 0.01f)
        {
            float currentDist = Vector3.Distance(transform.position, lowerPoint.position);
            NormalizedHeight.Value = Mathf.Clamp01(currentDist / totalDist);
        }
        else
        {
            NormalizedHeight.Value = targetNormalized;
        }
    }

    // ==========================================
    // PUBLIC API (CraneController tarafından çağrılır)
    // ==========================================

    /// <summary>
    /// Sepeti deniz dibine indir.
    /// </summary>
    [Server]
    public void ServerLower()
    {
        if (State.Value != CraneBasketState.Idle) return;
        State.Value = CraneBasketState.Lowering;
    }

    /// <summary>
    /// Sepeti güverteye çek.
    /// </summary>
    [Server]
    public void ServerRaise()
    {
        if (State.Value != CraneBasketState.Lowered) return;
        State.Value = CraneBasketState.Raising;
    }

    /// <summary>
    /// Şu an sepete eşya yüklenebilir mi?
    /// </summary>
    public bool CanLoadItems()
    {
        return State.Value == CraneBasketState.Lowered;
    }

    // ==========================================
    // EŞYA YÖNETİMİ (CartCargoZone pattern)
    // ==========================================

    private void OnTriggerEnter(Collider other)
    {
        if (!base.IsServerInitialized) return;

        GrabbableObject item = other.GetComponentInParent<GrabbableObject>();
        if (item == null || item.isHeavyVehicle) return;
        if (_itemsInBasket.Contains(item)) return;

        // Sadece Large ve MediumLarge eşyalar sepete girebilir
        if (item.itemSize != ItemSize.Large && item.itemSize != ItemSize.MediumLarge) return;

        // Kapasite kontrolü
        if (_itemsInBasket.Count >= maxSlots) return;
        if (CurrentWeight.Value + item.weight > maxWeight) return;

        _itemsInBasket.Add(item);
        CurrentWeight.Value += item.weight;
        ItemCount.Value = _itemsInBasket.Count;
        OnCargoChanged?.Invoke();

        // Çarpışma algılamasını iyileştir
        if (item.Rb != null)
        {
            item.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        // Eşya düşme sesi
        if (itemDropSound != null && motorAudioSource != null)
        {
            ObserversPlaySound(itemDropSound.name);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!base.IsServerInitialized) return;

        GrabbableObject item = other.GetComponentInParent<GrabbableObject>();
        if (item == null || !_itemsInBasket.Contains(item)) return;

        // Sepet hareket halindeyken eşya çıkmasın
        if (State.Value == CraneBasketState.Raising || State.Value == CraneBasketState.Lowering) return;

        RemoveItem(item);
    }

    private void RemoveItem(GrabbableObject item)
    {
        _itemsInBasket.Remove(item);
        CurrentWeight.Value = Mathf.Max(0f, CurrentWeight.Value - item.weight);
        ItemCount.Value = _itemsInBasket.Count;
        OnCargoChanged?.Invoke();

        // Parent'ı sıfırla
        NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
        if (itemNetObj != null && itemNetObj.transform.parent == this.transform)
        {
            itemNetObj.UnsetParent();
        }

        if (item.isFixedInCart.Value)
        {
            item.isFixedInCart.Value = false;
            item.UpdatePhysicsAuthority();
        }
    }

    /// <summary>
    /// Sepet yukarı çekilirken tüm eşyaları sabitle (düşmesin).
    /// </summary>
    [Server]
    private void FixAllItems()
    {
        for (int i = 0; i < _itemsInBasket.Count; i++)
        {
            var item = _itemsInBasket[i];
            if (item == null || item.isFixedInCart.Value) continue;

            NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
            if (itemNetObj == null) continue;
            if (itemNetObj.Owner.IsValid) continue; // Birisi tutuyorsa dokunma

            item.isFixedInCart.Value = true;
            itemNetObj.SetParent(this.NetworkObject);
            item.UpdatePhysicsAuthority();
        }
    }

    /// <summary>
    /// Sepet deniz dibine indiğinde eşyaların fiziğini geri aç.
    /// </summary>
    [Server]
    private void UnfixAllItems()
    {
        for (int i = 0; i < _itemsInBasket.Count; i++)
        {
            var item = _itemsInBasket[i];
            if (item == null || !item.isFixedInCart.Value) continue;

            item.isFixedInCart.Value = false;
            item.UpdatePhysicsAuthority();

            NetworkObject itemNetObj = item.GetComponent<NetworkObject>();
            if (itemNetObj != null)
            {
                itemNetObj.UnsetParent();
            }
        }
    }

    /// <summary>
    /// Güverteye ulaşınca sepetteki tüm eşyaları kargoya aktar ve despawn et.
    /// </summary>
    [Server]
    private void UnloadCargo()
    {
        if (shipCargo == null)
        {
            Debug.LogError("[CraneBasket] ShipCargo referansı atanmamış!");
            return;
        }

        for (int i = _itemsInBasket.Count - 1; i >= 0; i--)
        {
            var item = _itemsInBasket[i];
            if (item == null) continue;

            // Kargoya ekle
            shipCargo.AddItemToCargo(item);

            // Balonları sök
            DetachBagsFromItem(item);

            // Despawn
            NetworkObject netObj = item.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                PoolManager.Instance.DespawnNetwork(netObj);
            }
        }

        _itemsInBasket.Clear();
        CurrentWeight.Value = 0f;
        ItemCount.Value = 0;
        OnCargoChanged?.Invoke();

        // Boşaltma tamamlanma sesi
        if (unloadCompleteSound != null)
        {
            ObserversPlaySound(unloadCompleteSound.name);
        }

        Debug.Log("[CraneBasket] Tüm eşyalar gemiye boşaltıldı!");
    }

    /// <summary>
    /// Eşyaya takılı balonları söker.
    /// </summary>
    private void DetachBagsFromItem(GrabbableObject item)
    {
        if (item == null) return;
        LiftingBag[] allBags = FindObjectsByType<LiftingBag>(FindObjectsSortMode.None);
        foreach (var bag in allBags)
        {
            if (bag.TargetItem == item)
            {
                bag.Detach();
            }
        }
    }

    // ==========================================
    // SES
    // ==========================================

    private void UpdateMotorSound(CraneBasketState state)
    {
        if (motorAudioSource == null) return;

        bool shouldPlay = state == CraneBasketState.Lowering || state == CraneBasketState.Raising;
        if (shouldPlay && !motorAudioSource.isPlaying)
            motorAudioSource.Play();
        else if (!shouldPlay && motorAudioSource.isPlaying)
            motorAudioSource.Stop();
    }

    [ObserversRpc]
    private void ObserversPlaySound(string clipName)
    {
        if (motorAudioSource == null) return;

        AudioClip clip = null;
        if (itemDropSound != null && itemDropSound.name == clipName)
            clip = itemDropSound;
        else if (unloadCompleteSound != null && unloadCompleteSound.name == clipName)
            clip = unloadCompleteSound;

        if (clip != null)
            motorAudioSource.PlayOneShot(clip);
    }
}
