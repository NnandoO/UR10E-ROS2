using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using SofaUnity;

namespace SofaUnityXR
{
    public class SofaBotonPersonalizado : MonoBehaviour
    {
        public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable xrGrabInteractable;
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
                // Sistema Nativo: Escucha cuando aprietas el gatillo MIENTRAS sostienes la pinza
                xrGrabInteractable.activated.AddListener(AlApretar);
                xrGrabInteractable.deactivated.AddListener(AlSoltar);
            }
        }

        private void OnDisable()
        {
            if (xrGrabInteractable != null)
            {
                xrGrabInteractable.activated.RemoveListener(AlApretar);
                xrGrabInteractable.deactivated.RemoveListener(AlSoltar);
            }
        }

        private void AlApretar(ActivateEventArgs args)
        {
            Debug.Log("¡Gatillo de la pinza APRETADO!");
            if (puntaMatematica != null) puntaMatematica.ActivateTool = true;
        }

        private void AlSoltar(DeactivateEventArgs args)
        {
            Debug.Log("¡Gatillo de la pinza SOLTADO!");
            if (puntaMatematica != null) puntaMatematica.ActivateTool = false;
        }
    }
}