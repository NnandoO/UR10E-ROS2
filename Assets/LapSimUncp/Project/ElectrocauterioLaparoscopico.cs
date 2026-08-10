using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ElectrocauterioLaparoscopico : MonoBehaviour
{
    [Header("Configuración Visual")]
    [Tooltip("Sistema de partículas para simular el humo (Arrastra aquí tu Particle System)")]
    public ParticleSystem humoCauterizacion;
    
    private XRGrabInteractable grabInteractable;
    private bool estaQuemando = false;

    void Start()
    {
        // Buscamos el componente de agarre en la herramienta
        grabInteractable = GetComponentInParent<XRGrabInteractable>();

        if (grabInteractable != null)
        {
            // El dedo índice controla el encendido y apagado
            grabInteractable.activated.AddListener(EncenderHook);
            grabInteractable.deactivated.AddListener(ApagarHook);
        }
        else
        {
            Debug.LogError("No se encontró XRGrabInteractable en el Electrocauterio.");
        }

        // Nos aseguramos de que el humo inicie apagado
        if (humoCauterizacion != null) humoCauterizacion.Stop(); 
    }

    private void EncenderHook(ActivateEventArgs args)
    {
        estaQuemando = true;
        if (humoCauterizacion != null) humoCauterizacion.Play();
        Debug.Log("🔥 Electrocauterio ENCENDIDO");
    }

    private void ApagarHook(DeactivateEventArgs args)
    {
        estaQuemando = false;
        if (humoCauterizacion != null) humoCauterizacion.Stop();
        Debug.Log("Electrocauterio APAGADO");
    }

    // Usamos OnTriggerStay para que el corte ocurra *mientras* la punta toca algo
    private void OnTriggerStay(Collider other)
    {
        // Solo quema si tienes el gatillo apretado
        if (estaQuemando)
        {
            // Busca si lo que está tocando tiene un "pegamento" físico (FixedJoint)
            FixedJoint union = other.GetComponent<FixedJoint>();
            
            // Si encuentra una unión, la rompe (sin importar qué órgano sea)
            if (union != null)
            {
                Destroy(union); 
                Debug.Log("¡Unión física quemada y separada libremente!");
            }
        }
    }
}