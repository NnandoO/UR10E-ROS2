using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class PinzaRetraccionSinFisicas : MonoBehaviour
{
    [Header("Configuración de Agarre y Rango")]
    [Tooltip("El objeto vacío en las pinzas dentadas de la punta")]
    public Transform puntaPinza;
    
    [Tooltip("La capa de la vesícula (ej. 'Corte_Organo')")]
    public LayerMask capaOrgano;

    [Tooltip("Distancia máxima estricta (en metros). Ej: 0.05 para 5 centímetros.")]
    public float distanciaMaximaTension = 0.05f; 

    private XRGrabInteractable grabInteractable;
    
    private GameObject organoAgarrado;
    private Vector3 posicionOriginal;
    private Quaternion rotacionOriginal;
    
    // Distancia fija de desfase al momento de agarrarlo para que no rote ni salte
    private Vector3 offsetPosicionMundo;

    void Start()
    {
        grabInteractable = GetComponentInParent<XRGrabInteractable>();

        if (grabInteractable != null)
        {
            grabInteractable.activated.AddListener(AlternarAgarre);
        }
        else
        {
            Debug.LogError("No se encontró XRGrabInteractable en la Pinza de Retracción.");
        }
    }

    void Update()
    {
        if (organoAgarrado != null && puntaPinza != null)
        {
            // 1. Verificamos la distancia desde el origen original
            float distanciaActual = Vector3.Distance(posicionOriginal, puntaPinza.position);

            // 2. Si supera los 0.05m (5 cm), se suelta por tensión
            if (distanciaActual > distanciaMaximaTension)
            {
                Debug.LogWarning("⚠️ ¡Tensión máxima alcanzada! El tejido se zafó.");
                SoltarOrgano();
                return;
            }

            // Mantenemos la posición relativa inicial pero siguiendo el movimiento de la pinza,
            // manteniendo su rotación original intacta para que no gire de forma extraña.
            organoAgarrado.transform.position = puntaPinza.position + offsetPosicionMundo;
            organoAgarrado.transform.rotation = rotacionOriginal; 
        }
    }

    private void AlternarAgarre(ActivateEventArgs args)
    {
        if (organoAgarrado == null)
        {
            Collider[] tocados = Physics.OverlapSphere(puntaPinza.position, 0.05f, capaOrgano);
            
            if (tocados.Length > 0)
            {
                organoAgarrado = tocados[0].gameObject;
                
                // Guardamos sus valores estáticos iniciales
                posicionOriginal = organoAgarrado.transform.position;
                rotacionOriginal = organoAgarrado.transform.rotation;
                
                // Calculamos el desfase exacto para que no dé ningún "salto" visual al morderlo
                offsetPosicionMundo = organoAgarrado.transform.position - puntaPinza.position;
                
                Debug.Log("🔒 ¡Pinza BLOQUEADA! Posición y rotación fijas.");
            }
            else
            {
                Debug.Log("Has apretado el gatillo, pero no estás tocando la vesícula.");
            }
        }
        else
        {
            SoltarOrgano();
            Debug.Log("🔓 Pinza DESBLOQUEADA de forma manual.");
        }
    }

    private void SoltarOrgano()
    {
        if (organoAgarrado != null)
        {
            // Regresa exactamente a su sitio y orientación original
            organoAgarrado.transform.position = posicionOriginal;
            organoAgarrado.transform.rotation = rotacionOriginal;
            organoAgarrado = null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (puntaPinza != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(puntaPinza.position, 0.05f);
        }
    }
}