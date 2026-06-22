using UnityEngine;
using UnityEngine.XR;

public class CambioModeloVR : MonoBehaviour
{
    [Header("=== MODELOS 3D ===")]
    public GameObject modeloMando;
    public GameObject modeloMano;

    [Header("=== CONFIGURACIÓN ===")]
    public bool esManoIzquierda = true;

    void Update()
    {
        if (modeloMando == null || modeloMano == null) return;

        // Buscamos qué hay conectado exactamente en el puerto izquierdo o derecho en este milisegundo
        XRNode nodo = esManoIzquierda ? XRNode.LeftHand : XRNode.RightHand;
        InputDevice dispositivo = InputDevices.GetDeviceAtXRNode(nodo);

        // Si hay algo rastreado activamente por las gafas
        if (dispositivo.isValid)
        {
            // Verificamos si ese dispositivo tiene la etiqueta de "Controlador"
            if ((dispositivo.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                if (!modeloMando.activeSelf) modeloMando.SetActive(true);
                if (modeloMano.activeSelf) modeloMano.SetActive(false);
            }
            // Verificamos si tiene la etiqueta de "Hand Tracking" (Seguimiento de manos)
            else if ((dispositivo.characteristics & InputDeviceCharacteristics.HandTracking) != 0)
            {
                if (modeloMando.activeSelf) modeloMando.SetActive(false);
                if (!modeloMano.activeSelf) modeloMano.SetActive(true);
            }
            else 
            {
                // Fallback de seguridad: si Unity se confunde, asumimos que son tus manos
                if (modeloMando.activeSelf) modeloMando.SetActive(false);
                if (!modeloMano.activeSelf) modeloMano.SetActive(true);
            }
        }
        else
        {
            // Si pierdes el tracking por completo (ej. te quitas las gafas), apagamos todo
            if (modeloMando.activeSelf) modeloMando.SetActive(false);
            if (modeloMano.activeSelf) modeloMano.SetActive(false);
        }
    }
}