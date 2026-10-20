using UnityEngine;

using System.Collections.Generic;

public class IntercambioHerramienta : MonoBehaviour
{
    [Header("Referencias del Brazo")]
    [Tooltip("El GameObject en la punta del UR10e donde se anclará la herramienta.")]
    public Transform puntaDelBrazo;

    [Header("Corrección de Dirección del Robot")]
    [Tooltip("Ajusta estos grados (ej. Y en 90 o -90) para que mire al frente y no a la izquierda.")]
    public Vector3 ajusteRotacionRobot = Vector3.zero;

    private Dictionary<GameObject, GameObject> clonesPorHerramienta = new Dictionary<GameObject, GameObject>();
    private GameObject herramientaActualEnBrazo;

    public void AcoplarAlBrazo(GameObject instrumentoOriginal)
    {
        // 1. Soltar la herramienta anterior
        if (herramientaActualEnBrazo != null)
        {
            herramientaActualEnBrazo.transform.SetParent(null);
        }

        // 2. Limpieza de basura en la punta
        foreach (Transform hijo in puntaDelBrazo)
        {
            Destroy(hijo.gameObject);
        }

        // 3. Reutilización del clon
        GameObject clonAUsar;
        if (clonesPorHerramienta.ContainsKey(instrumentoOriginal) && clonesPorHerramienta[instrumentoOriginal] != null)
        {
            clonAUsar = clonesPorHerramienta[instrumentoOriginal];
        }
        else
        {
            clonAUsar = Instantiate(instrumentoOriginal);
            clonAUsar.name = instrumentoOriginal.name + "_CopiaRobot";

            UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabClon = clonAUsar.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grabClon != null) Destroy(grabClon);

            clonesPorHerramienta[instrumentoOriginal] = clonAUsar;
        }

        // 4. Apagar físicas
        Rigidbody rbCopia = clonAUsar.GetComponent<Rigidbody>();
        if (rbCopia != null)
        {
            rbCopia.linearVelocity = Vector3.zero;
            rbCopia.angularVelocity = Vector3.zero;
            rbCopia.isKinematic = true;
            rbCopia.useGravity = false;
        }

        // 5. Emparentar
        clonAUsar.transform.SetParent(puntaDelBrazo);
        herramientaActualEnBrazo = clonAUsar;

        // 6. ALINEACIÓN MATEMÁTICA CORREGIDA (Evita el Efecto Péndulo)
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabOriginal = instrumentoOriginal.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        Transform puntoAttach = grabOriginal != null ? grabOriginal.attachTransform : null;

        if (puntoAttach != null)
        {
            Vector3 offsetPosicionLocal = instrumentoOriginal.transform.InverseTransformPoint(puntoAttach.position);
            Quaternion offsetRotacionLocal = Quaternion.Inverse(instrumentoOriginal.transform.rotation) * puntoAttach.rotation;

            // PASO 1: Calculamos la rotación FINAL (Ajuste del brazo * Rotación inversa del Attach)
            Quaternion rotacionFinal = Quaternion.Euler(ajusteRotacionRobot) * Quaternion.Inverse(offsetRotacionLocal);
            
            // PASO 2: Aplicamos la rotación
            clonAUsar.transform.localRotation = rotacionFinal;
            
            // PASO 3: Calculamos la posición usando la rotación FINAL. 
            // Esto ancla el pivote exactamente en la punta, sin importar la forma o tamaño del instrumento.
            clonAUsar.transform.localPosition = -(rotacionFinal * offsetPosicionLocal);
        }
        else
        {
            clonAUsar.transform.localPosition = Vector3.zero;
            clonAUsar.transform.localRotation = Quaternion.Euler(ajusteRotacionRobot);
        }
    }

    public void SoltarDelBrazo()
    {
        if (herramientaActualEnBrazo != null)
        {
            herramientaActualEnBrazo.transform.SetParent(null);
            herramientaActualEnBrazo = null;
        }
    }
}