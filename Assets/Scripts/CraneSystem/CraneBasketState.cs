/// <summary>
/// Vinç sepetinin durumlarını tanımlar (state machine).
/// </summary>
public enum CraneBasketState
{
    Idle,       // Güvertede bekliyor
    Lowering,   // Deniz dibine iniyor
    Lowered,    // Deniz dibinde, eşya yüklenebilir
    Raising,    // Yukarı çekiliyor
    Unloading   // Güverteye ulaştı, eşyalar kargoya aktarılıyor
}
