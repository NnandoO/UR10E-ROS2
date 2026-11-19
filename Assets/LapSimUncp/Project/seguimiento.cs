/// <summary>
/// Seguimiento del brazo UR10e DERECHO (servidor Python: seguimientoR.py, puerto 5005).
/// Toda la lógica (suavizado, clutch, reconexión, selector de hardware) está en
/// SeguimientoBrazoBase. Los campos conservan sus nombres, así que la configuración
/// del Inspector en la escena no se pierde.
/// </summary>
public class seguimiento : SeguimientoBrazoBase
{
    protected override int PuertoPorDefecto => 5005;
    protected override string NombreBrazo => "Brazo Der";
}
