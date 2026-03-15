using System;

/// <summary>
/// Envanter içindeki bir eşyanın ağ üzerinden senkronize edilebilir verisi.
/// FishNet SyncList ile kullanılır.
/// </summary>
[Serializable]
public struct ItemData
{
    public string ItemId;
    public string ItemName;
    public float BasePrice;
    public float CurrentPrice;
    public float Weight;

    public ItemData(GrabbableObject grabbable)
    {
        ItemId = grabbable.itemId;
        ItemName = grabbable.itemName;
        BasePrice = grabbable.basePrice;
        CurrentPrice = grabbable.currentPrice;
        Weight = grabbable.weight;
    }
}
