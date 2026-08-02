using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class CambioModeloVR : MonoBehaviour
{
    [Header("=== MODELOS 3D ===")]
    public GameObject modeloMando;
    public GameObject modeloMano;

    [Header("=== CONFIGURACIÓN ===")]
    public bool esManoIzquierda = true;

    // Creamos una lista para guardar todos los dispositivos de ese brazo (mandos y manos)
    private List<InputDevice> dispositivos = new List<InputDevice>();

    void Update()
    {
        if (modeloMando == null || modeloMano == null) return;

        XRNode nodo = esManoIzquierda ? XRNode.LeftHand : XRNode.RightHand;
        
        // Llenamos la lista con TODOS los dispositivos conectados a esa mano
        InputDevices.GetDevicesAtXRNode(nodo, dispositivos);

        bool hayMandoActivo = false;
        bool hayManoActiva = false;

        // Revisamos uno por uno qué está activo en este milisegundo
        foreach (var dispositivo in dispositivos)
        {
            if (dispositivo.isValid)
            {
                if ((dispositivo.characteristics & InputDeviceCharacteristics.Controller) != 0)
                {
                    hayMandoActivo = true;
                }
                else if ((dispositivo.characteristics & InputDeviceCharacteristics.HandTracking) != 0)
                {
                    hayManoActiva = true;
                }
            }
        }

        // Lógica de prioridad: El mando le gana a la mano.
        if (hayMandoActivo)
        {
            if (!modeloMando.activeSelf) modeloMando.SetActive(true);
            if (modeloMano.activeSelf) modeloMano.SetActive(false);
        }
        else if (hayManoActiva) // Si no hay mando, pero sí hay mano
        {
            if (modeloMando.activeSelf) modeloMando.SetActive(false);
            if (!modeloMano.activeSelf) modeloMano.SetActive(true);
        }
        else // Si soltaste los mandos y escondiste las manos detrás de la espalda
        {
            if (modeloMando.activeSelf) modeloMando.SetActive(false);
            if (modeloMano.activeSelf) modeloMano.SetActive(false);
        }
    }
}