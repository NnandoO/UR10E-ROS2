/// <summary>
/// Seguimiento del brazo UR10e IZQUIERDO (servidor Python: seguimientoL.py, puerto 5006).
/// Brazo SOLO SIMULADO: el único UR10e real es el derecho, así que aquí no se puede elegir UR10eFisico.
/// Toda la lógica (suavizado, clutch, reconexión, selector de hardware) está en
/// SeguimientoBrazoBase. Los campos conservan sus nombres, así que la configuración
/// del Inspector en la escena no se pierde.
/// </summary>
public class seguimientoL : SeguimientoBrazoBase
{
    protected override int PuertoPorDefecto => 5006;
    protected override string NombreBrazo => "Brazo Izq";
    protected override bool PermiteHardwareFisico => false;
}
