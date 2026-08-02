using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using SofaUnity;

namespace SofaUnityXR
{
    public class ToolTriggerSofa : MonoBehaviour
    {
        [Tooltip("El componente XRGrabInteractable principal de la pinza")]
        public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable xrGrabInteractable;

        [Tooltip("El láser que está en la PUNTA de esta pinza")]
        public SofaLaserModel puntaMatematica;

        private void Awake()
        {
            if (xrGrabInteractable == null) xrGrabInteractable = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (puntaMatematica == null) puntaMatematica = GetComponent<SofaLaserModel>();
        }

        private void OnEnable()
        {
            if (xrGrabInteractable != null)
            {
                // Cuando aprietas el gatillo (Activate) mientras sostienes la pinza
                xrGrabInteractable.activated.AddListener(AlApretarGatillo);
                xrGrabInteractable.deactivated.AddListener(AlSoltarGatillo);
            }
        }

        private void OnDisable()
        {
            if (xrGrabInteractable != null)
            {
                xrGrabInteractable.activated.RemoveListener(AlApretarGatillo);
                xrGrabInteractable.deactivated.RemoveListener(AlSoltarGatillo);
            }
        }

        private void AlApretarGatillo(ActivateEventArgs args)
        {
            if (puntaMatematica != null) puntaMatematica.ActivateTool = true;
        }

        private void AlSoltarGatillo(DeactivateEventArgs args)
        {
            if (puntaMatematica != null) puntaMatematica.ActivateTool = false;
        }
    }
}