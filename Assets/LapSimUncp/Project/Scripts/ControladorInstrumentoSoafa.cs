using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

public class ControladorInstrumentoSoafa : MonoBehaviour
{
    [Header("Configuración de la Acción")]
    [Tooltip("Marca esto si esta herramienta es el clon manejado por el robot")]
    public bool esClonPasivo = false;

    [Header("Referencias a SOFA / Lógica")]
    [Tooltip("Arrastra aquí el script de SOFA o la lógica que agarra/corta el órgano")]
    public MonoBehaviour scriptSofaOrgano; // O el componente que uses para interactuar con SOFA

    private InputDevice dispositivoMando;
    private bool gatilloPresionadoAnterior = false;

    void Start()
    {
        // Detectar si estamos usando el mando izquierdo o derecho automáticamente
        // (Si el objeto está en el robot, ignoramos el input directo del usuario y esperamos órdenes)
    }

    void Update()
    {
        // Si es el clon del robot, él no lee el gatillo físico directamente; 
        // el usuario controla la acción principal con su mano y el espejo la replica.
        if (esClonPasivo) return; 

        // Leer el input del gatillo del mando de VR (Trigger Button)
        // Asumimos que estamos usando el XR Node correspondiente
        bool triggerValue = RevisaGatilloVR();

        if (triggerValue && !gatilloPresionadoAnterior)
        {
            EjecutarAccionInstrumento();
            
            // REPLICAR EN EL CLON DEL ROBOT: 
            // Buscamos si hay un clon activo en la escena y le ordenamos hacer la misma acción
            ControladorInstrumentoSoafa[] todosLosInstrumentos = FindObjectsOfType<ControladorInstrumentoSoafa>();
            foreach (var inst in todosLosInstrumentos)
            {
                if (inst.esClonPasivo && inst.gameObject.name.Contains(gameObject.name))
                {
                    inst.EjecutarAccionInstrumento();
                }
            }
        }
        gatilloPresionadoAnterior = triggerValue;
    }

    bool RevisaGatilloVR()
    {
        // Lógica estándar para leer el gatillo en XR Interaction Toolkit / OpenXR
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand); // Cambiar a LeftHand si es el izquierdo
        float triggerValue = 0f;
        if (device.TryGetFeatureValue(CommonUsages.trigger, out triggerValue))
        {
            return triggerValue > 0.75f; // Umbral de presión del gatillo
        }
        // Fallback por teclado para pruebas en el Editor de Unity (Barra espaciadora o Tecla G)
        return Input.GetKeyDown(KeyCode.G);
    }

    public void EjecutarAccionInstrumento()
    {
        Debug.Log($"⚙️ Ejecutando acción de herramienta ({gameObject.name}): Agarrar / Cortar órgano SOFA.");
        
        // AQUÍ LLAMAS A TU LÓGICA DE SOFA FRAMEWORK:
        // Por ejemplo: si usas un componente personalizado para interactuar con los nodos de SOFA:
        if (scriptSofaOrgano != null)
        {
            // scriptSofaOrgano.Enabled = true; o el método que active la interacción con el órgano
        }
    }
}