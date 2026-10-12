using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // Necesario para XRI 3.0+

namespace SofaUnityXR
{
    /// <summary>
    /// Cambia de cámara cuando se agreda un objeto, pero solo si el objeto está en capas específicas.
    /// </summary>
    public class CameraSwitcherOnGrabWithLayers : MonoBehaviour
    {
        [Header("Capas Objectivo")]
        [Tooltip("Máscara de bits para las capas de objetos que activarán el cambio de cámara")]
        public LayerMask capasObjectivo = -1; // -1 = todas las capas

        [Header("Referencias de Cámaras")]
        [Tooltip("Cámara que se activa cuando se agarra el objeto (visor completo)")]
        public Camera cameraVisorCompleto = null;
        
        [Tooltip("Cámara que se activa cuando se suelta el objeto")]
        public Camera cameraSecundaria = null;

        [Header("Configuración")]
        [Tooltip("Delay en segundos antes de cambiar de cámara después de soltar")]
        public float delayCambioCamara = 0.1f;

        // CORRECCIÓN: Cambiado de XRInteractable a XRBaseInteractable
        private XRBaseInteractable m_interactable = null;
        private Camera m_cameraOriginal = null;
        private bool m_cambioActivo = false;

        private void Awake()
        {
            // CORRECCIÓN: Obtener el componente con la clase correcta
            m_interactable = GetComponent<XRBaseInteractable>();
            
            // Verificar configuración
            if (cameraVisorCompleto == null || cameraSecundaria == null)
            {
                Debug.LogError("CameraSwitcherOnGrabWithLayers: Faltan asignar ambas cámaras en el Inspector");
            }
        }

        private void OnEnable()
        {
            // Suscribirnos a los eventos de agarre
            if (m_interactable != null)
            {
                m_interactable.selectEntered.AddListener(OnObjectGrabbed);
                m_interactable.selectExited.AddListener(OnObjectReleased);
            }
        }

        private void OnDisable()
        {
            // Desuscribirnos para evitar fugas de memoria
            if (m_interactable != null)
            {
                m_interactable.selectEntered.RemoveListener(OnObjectGrabbed);
                m_interactable.selectExited.RemoveListener(OnObjectReleased);
            }
        }

        /// <summary>
        /// Verifica si el objeto está en una capa objetivo y luego cambia de cámara
        /// </summary>
        private void OnObjectGrabbed(SelectEnterEventArgs args) // CORRECCIÓN: Firma correcta para eventos XRI recientes
        {
            // Obtener el objeto interactuado desde los argumentos
            var interactedObject = args.interactableObject as MonoBehaviour;
            if (interactedObject == null) return;

            // Verificar la capa del objeto agarrado
            int capaObjeto = interactedObject.gameObject.layer;
            bool objetoEnCapaObjetivo = (capasObjectivo.value & (1 << capaObjeto)) > 0;

            Debug.Log($"[CameraSwitcher] Objeto agraced: {gameObject.name}, Capa: {capaObjeto}, EnObjetivo: {objetoEnCapaObjetivo}");

            // Si el objeto NO está en la capa objetivo, ignorar el agarre
            if (!objetoEnCapaObjetivo)
            {
                Debug.Log("[CameraSwitcher] Objeto no está en capa objetivo, ignorando cambio de cámara");
                return;
            }

            // Continuar con el cambio de cámara
            OnObjectGrabbedInternal();
        }

        /// <summary>
        /// Lógica interna de cambio de cámara (solo si la capa coincide)
        /// </summary>
        private void OnObjectGrabbedInternal()
        {
            Debug.Log($"[CameraSwitcher] Cambiando a cámara de visor completo");

            // Guardar la cámara original antes de cambiar
            if (m_cameraOriginal == null)
            {
                m_cameraOriginal = Camera.main;
            }

            // Activar la cámara del visor completo y desactivar la secundaria
            if (cameraVisorCompleto != null)
            {
                cameraVisorCompleto.gameObject.SetActive(true);
                // Llenar todo el viewport
                cameraVisorCompleto.rect = new Rect(0, 0, 1, 1);
            }

            if (cameraSecundaria != null)
            {
                cameraSecundaria.gameObject.SetActive(false);
            }

            // Opcional: Desactivar la cámara main si es diferente
            if (Camera.main != null && Camera.main != cameraVisorCompleto)
            {
                Camera.main.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Se llama cuando sueltan el objeto (verifica capa antes de restaurar)
        /// </summary>
        private void OnObjectReleased(SelectExitEventArgs args) // CORRECCIÓN: Firma correcta para eventos XRI recientes
        {
            var interactedObject = args.interactableObject as MonoBehaviour;
            if (interactedObject == null) return;

            // Verificar la capa del objeto soltado
            int capaObjeto = interactedObject.gameObject.layer;
            bool objetoEnCapaObjetivo = (capasObjectivo.value & (1 << capaObjeto)) > 0;

            // Si el objeto no está en la capa objetivo, no hacer nada
            if (!objetoEnCapaObjetivo)
            {
                Debug.Log("[CameraSwitcher] Objeto soltado no está en capa objetivo, sin cambio de cámara");
                return;
            }

            Debug.Log($"[CameraSwitcher] Objeto soltado: {gameObject.name}, restaurando cámara");

            // Esperar un pequeño delay antes de cambiar
            if (!m_cambioActivo)
            {
                m_cambioActivo = true;
                Invoke("RestaurarCamaraOriginal", delayCambioCamara);
            }
        }

        /// <summary>
        /// Restaura la cámara original que estaba activa antes de agarrar
        /// </summary>
        private void RestaurarCamaraOriginal()
        {
            // Reactivar la cámara original (main camera o la secundaria)
            if (m_cameraOriginal != null)
            {
                m_cameraOriginal.gameObject.SetActive(true);
            }

            // Ocultar la cámara del visor
            if (cameraVisorCompleto != null)
            {
                cameraVisorCompleto.gameObject.SetActive(false);
            }

            if (cameraSecundaria != null)
            {
                cameraSecundaria.gameObject.SetActive(true);
            }

            m_cambioActivo = false;
        }

        /// <summary>
        /// Dibujar información en el Inspector
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (cameraVisorCompleto == null || cameraSecundaria == null)
            {
                Debug.LogWarning("CameraSwitcherOnGrabWithLayers: Asigna ambas cámaras en el Inspector");
            }
            
            // Mostrar en la consola qué capa está configurada
            if (Application.isPlaying)
            {
                // CORRECCIÓN: "capasObjectivo" estaba mal escrito como "capasObjectigo"
                Debug.Log($"[CameraSwitcher] Capas objetivo configuradas: {capasObjectivo.value}");
            }
        }
    }
}