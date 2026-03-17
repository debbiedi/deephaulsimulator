/// <summary>
/// Eşyaların nadirlik seviyelerini tanımlar.
/// Hurda (0) en yaygın, Hazine (4) en nadir.
/// Değer sıralaması: Hurda < Sıradan < Değerli < Antika < Hazine
/// </summary>
public enum RarityTier
{
    Hurda = 0,       // Scrap - Gri (#808080)
    Siradan = 1,     // Common - Beyaz (#FFFFFF)
    Degerli = 2,     // Valuable - Mavi (#4169E1)
    Antika = 3,      // Antique - Mor (#9B30FF)
    Hazine = 4       // Relic - Altın (#FFD700)
}
