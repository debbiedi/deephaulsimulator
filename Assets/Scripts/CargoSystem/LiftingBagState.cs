/// <summary>
/// Kaldırma balonunun durumlarını tanımlar (state machine).
/// </summary>
public enum LiftingBagState
{
    Attaching, // Balon eşyaya takılıyor (kısa animasyon)
    Inflating, // Oyuncu aktif olarak pompalıyor
    Paused,    // Pompalama durdu, balon yavaşça söner
    Inflated,  // Tam şiş, kaldırma kuvveti aktif
    Floating,  // Eşya su yüzeyine ulaştı
    Detached   // Balon söküldü, pool'a dönüyor
}
