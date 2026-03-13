/// <summary>
/// Karakter hareket durumlarını tanımlar.
/// Walking: Karada normal yürüme (yerçekimi aktif)
/// Swimming: Suda serbest yüzme (3D hareket, azaltılmış yerçekimi)
/// UnderwaterWalking: Su altında zeminde yürüme (yavaş hareket, azaltılmış yerçekimi)
/// </summary>
public enum PlayerMovementState
{
    Walking,            // Karada normal yürüme
    Swimming,           // Suda serbest yüzme (3D hareket)
    UnderwaterWalking   // Su altında zeminde yürüme
}
