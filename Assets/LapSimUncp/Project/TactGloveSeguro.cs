using UnityEngine;
using Bhaptics.SDK2;

public class TactGlovePingFuerte : MonoBehaviour
{
    [Header("Configuración de Mano")]
    [Tooltip("Marca esta casilla SOLO si el script está en la mano izquierda")]
    public bool esManoIzquierda = false;

    private void OnTriggerEnter(Collider other)
    {
        // Ignorar choques con el jugador, la cámara o las propias manos
        if (other.CompareTag("Player") || other.CompareTag("MainCamera")) return;
        if (other.name.Contains("Hand") || other.name.Contains("Interactor")) return;

        Debug.Log("💥 ¡GOLPE / PING FUERTE! Tocando: " + other.gameObject.name);

        // Disparar vibración fuerte directo al hardware
        EjecutarVibracionFuerte();
    }

    private void EjecutarVibracionFuerte()
    {
        // El SDK2 de bHaptics usa un código numérico para los dispositivos:
        // 8 = TactGlove (Left), 9 = TactGlove (Right)
        int posicionDispositivo = esManoIzquierda ? 8 : 9;

        // Los TactGloves tienen 8 motores en total. Creamos un array con la intensidad máxima (100)
        int[] intensidadesMotores = new int[] { 100, 100, 100, 100, 100, 100, 100, 100 };

        try
        {
            // Método oficial de SDK2 para disparar motores directamente (Posición, Array de Motores, Duración en ms)
            BhapticsLibrary.PlayMotors(posicionDispositivo, intensidadesMotores, 150);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Error al enviar vibración fuerte: " + e.Message);
        }
    }
}