/// <summary>
/// Tasiyici drone'un durumlarini tanimlar (state machine).
/// </summary>
public enum DroneState
{
    Idle,        // Yukarida/spawn noktasinda bekliyor, cagirilmayi bekler
    Arriving,    // Yukaridan su yuzeyine dogru iniyor
    Hovering,    // Su yuzeyinde bekliyor, esya yuklenebilir
    Loading,     // Esya yuklendi, kalkis geri sayimi basliyor
    Departing,   // Teslimat noktasina dogru ucuyor
    Delivering,  // Teslimat noktasinda, esyalari satiyor/bosaltiyor
    Returning    // Spawn noktasina geri donuyor
}
