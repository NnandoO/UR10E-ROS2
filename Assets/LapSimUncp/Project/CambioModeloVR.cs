using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class ControladorIntercambioVR : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("El objeto o modelo 3D del mando físico")]
    public GameObject modeloMando;
    
    [Tooltip("El objeto o modelo 3D de la mano virtual (el objeto que tiene el XR Hand Mesh Controller)")]
    public GameObject modeloMano;

    [Header("Configuración")]
    public bool esManoIzquierda = true;

    private List<InputDevice> dispositivosMando = new List<InputDevice>();

    void Update()
    {
        if (modeloMando == null || modeloMano == null) return;

        XRNode nodo = esManoIzquierda ? XRNode.LeftHand : XRNode.RightHand;

        // Comprobamos si hay un mando físico activo conectado al nodo
        InputDevices.GetDevicesAtXRNode(nodo, dispositivosMando);
        
        bool mandoDetectado = false;
        foreach (var dispositivo in dispositivosMando)
        {
            if (dispositivo.isValid && (dispositivo.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                mandoDetectado = true;
                break;
            }
        }

        // Lógica de exclusión mutua limpia:
        if (mandoDetectado)
        {
            // Si hay mando físico encendido: mostramos mando, ocultamos mano
            if (!modeloMando.activeSelf) modeloMando.SetActive(true);
            if (modeloMano.activeSelf) modeloMano.SetActive(false);
        }
        else
        {
            // Si no hay mando físico encendido: ocultamos mando, permitimos que la mano actúe
            if (modeloMando.activeSelf) modeloMando.SetActive(false);
            if (!modeloMano.activeSelf) modeloMano.SetActive(true);
        }
    }
}