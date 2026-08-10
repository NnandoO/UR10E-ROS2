using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class AtraparEndobag : MonoBehaviour
{
    [Header("Configuración de Captura")]
    [Tooltip("Selecciona la capa (Layer) de tu vesícula (ej. 'Corte_Organo')")]
    public LayerMask capaVesicula;
    
    [Tooltip("Un objeto vacío (Empty) colocado en el fondo de la bolsa de plástico")]
    public Transform fondoDeLaBolsa;

    private void OnTriggerEnter(Collider other)
    {
        // Comparamos si la capa del objeto que acaba de entrar está dentro de nuestra LayerMask
        if ((capaVesicula.value & (1 << other.gameObject.layer)) > 0)
        {
            GameObject pedazoVesicula = other.gameObject;

            // 1. Apagamos el Rigidbody para que deje de flotar o rebotar
            Rigidbody rb = pedazoVesicula.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true; 
                rb.useGravity = false;
            }

            // 2. Destruimos el componente de agarre de XR
            // Esto evita que el usuario saque el pedazo por accidente
            XRGrabInteractable interactable = pedazoVesicula.GetComponent<XRGrabInteractable>();
            if (interactable != null)
            {
                Destroy(interactable); 
            }

            // 3. Emparentamos el pedazo a la bolsa
            pedazoVesicula.transform.SetParent(this.transform);

            // 4. Lo movemos al fondo visual de la bolsa
            if (fondoDeLaBolsa != null)
            {
                pedazoVesicula.transform.position = fondoDeLaBolsa.position;
            }

            Debug.Log("¡Extracción exitosa! Pedazo de tejido atrapado en la bolsa.");
        }
    }
}