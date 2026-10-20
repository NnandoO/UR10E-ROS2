using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class GestorEspejoHerramientas : MonoBehaviour
{
    [Header("Configuración del Robot")]
    public Transform puntaDelRobot; // Arrastra aquí la punta de tu UR10e (o el target del brazo)
    
    private UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor interactor;
    private GameObject herramientaClonadaEnRobot;

    void Start()
    {
        interactor = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
        interactor.selectEntered.AddListener(AlAgarrarHerramienta);
        interactor.selectExited.AddListener(AlSoltarHerramienta);
    }

    public void AlAgarrarHerramienta(SelectEnterEventArgs args)
    {
        GameObject herramientaOriginal = args.interactableObject.transform.gameObject;

        // Validar que tenga la etiqueta correcta
        if (herramientaOriginal.CompareTag("Laparoscopico"))
        {
            // Si ya había otra herramienta en el robot, la borramos
            if (herramientaClonadaEnRobot != null) Destroy(herramientaClonadaEnRobot);

            // 1. INSTANCIAR EL CLON EXACTO EN EL ROBOT
            // Nota: Lo ideal es instanciar desde un Prefab asignado o duplicando el objeto. 
            // Si duplicamos el objeto directamente de la escena:
            herramientaClonadaEnRobot = Instantiate(herramientaOriginal, puntaDelRobot.position, puntaDelRobot.rotation);
            herramientaClonadaEnRobot.transform.SetParent(puntaDelRobot);

            // 2. LIMPIEZA DE INTERACCIÓN HUMANA EN EL CLON
            // El clon del robot NO debe ser agarrado por tus manos de VR, por lo que desactivamos el Grab y colliders físicos de agarre
            UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabClon = herramientaClonadaEnRobot.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grabClon != null) Destroy(grabClon);

            // 3. MARCAR COMO CLON PARA EL SCRIPT DE ACCIONES
            herramientaClonadaEnRobot.name = herramientaOriginal.name + "_ClonRobot";
            
            // Opcional: Le avisas al script interno de la herramienta que es un clon pasivo del robot
            ControladorInstrumentoSoafa clonScript = herramientaClonadaEnRobot.GetComponent<ControladorInstrumentoSoafa>();
            if (clonScript != null) clonScript.esClonPasivo = true;

            Debug.Log("🔄 Herramienta replicada con éxito en el brazo robótico con todas sus propiedades.");
        }
    }

    public void AlSoltarHerramienta(SelectExitEventArgs args)
    {
        GameObject herramientaOriginal = args.interactableObject.transform.gameObject;
        if (herramientaOriginal.CompareTag("Laparoscopico"))
        {
            if (herramientaClonadaEnRobot != null)
            {
                Destroy(herramientaClonadaEnRobot);
            }
        }
    }
}