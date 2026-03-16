/// <summary>
/// Eşya boyutlarını tanımlar.
/// Small: Kemer çantasına konulabilir (yüzük, kolye, sikke vb.)
/// Medium: Kaldırma balonu ile yüzeye çıkarılabilir (1 balon yeter)
/// MediumLarge: Kaldırma balonu ile yüzeye çıkarılabilir (3 balon gerekli)
/// Large: Balonla kaldırılamaz, farklı mekanik gerektirir (çok büyük/ağır)
/// </summary>
public enum ItemSize
{
    Small,        // Çantaya eklenebilir
    Medium,       // 1 balonla kaldırılabilir
    MediumLarge,  // 3 balonla kaldırılabilir
    Large         // Balonla kaldırılamaz
}
