using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ClipadoraLaparoscopica : MonoBehaviour
{
    [Header("Configuración de Clips")]
    [Tooltip("El modelo 3D del clip de titanio/polímero (Debe ser un Prefab)")]
    public GameObject prefabClip;          
    
    [Tooltip("El objeto vacío colocado exactamente entre las mandíbulas de la clipadora")]
    public Transform puntoDeAparicion;     
    
    [Tooltip("Cantidad máxima de clips que puedes disparar")]
    public int clipsDisponibles = 6;       

    private XRGrabInteractable grabInteractable;

    void Start()
    {
        // Buscamos el componente de agarre en el objeto principal (el mango)
        grabInteractable = GetComponentInParent<XRGrabInteractable>();

        if (grabInteractable != null)
        {
            // Vinculamos nuestra función de disparar al evento del gatillo (dedo índice)
            grabInteractable.activated.AddListener(AplicarClip);
        }
        else
        {
            Debug.LogError("No se encontró XRGrabInteractable en la clipadora ni en sus padres.");
        }
    }

    private void AplicarClip(ActivateEventArgs args)
    {
        if (clipsDisponibles <= 0)
        {
            Debug.Log("¡Cartucho vacío! Ya no te quedan clips en esta herramienta.");
            return;
        }

        if (prefabClip == null || puntoDeAparicion == null)
        {
            Debug.LogWarning("Faltan referencias: Asigna el Prefab del Clip y el Punto de Aparición en el Inspector.");
            return;
        }

        // Instanciamos (creamos) el clip exactamente en la punta de la herramienta, en cualquier lugar
        Instantiate(prefabClip, puntoDeAparicion.position, puntoDeAparicion.rotation);
        
        // Restamos 1 al cartucho
        clipsDisponibles--;
        Debug.Log("¡Clip disparado libremente! Quedan: " + clipsDisponibles);
    }
}