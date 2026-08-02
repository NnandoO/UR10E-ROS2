using System;
using System.Collections;
using UnityEngine;
using SofaUnity;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SofaUnityXR
{
    public class GrabSofa : MonoBehaviour
    {
        [Header("1. El Agarre Físico (Dedo Medio)")]
        [Tooltip("El componente de Unity que agarra las pinzas físicamente")]
        public UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor manoIzquierdaXR;
        public UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor manoDerechaXR;

#if ENABLE_INPUT_SYSTEM
        [Header("2. El Botón de Acción (Dedo Índice / Trigger)")]
        [Tooltip("Asigna aquí la acción del gatillo izquierdo (XRI LeftHand/Activate)")]
        [SerializeField] private InputActionReference m_leftTriggerButton = null;
        
        [Tooltip("Asigna aquí la acción del gatillo derecho (XRI RightHand/Activate)")]
        [SerializeField] private InputActionReference m_rightTriggerButton = null;
#endif

        [Header("3. La Acción Matemática (SOFA)")]
        [SerializeField] private SofaLaserModel m_leftSofaLaserModel = null;
        [SerializeField] private SofaLaserModel m_rightSofaLaserModel = null;

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (m_leftTriggerButton != null && m_leftTriggerButton.action != null)
            {
                m_leftTriggerButton.action.performed += ActivateLeftSofa;
                m_leftTriggerButton.action.canceled += DesactivateLeftSofa;
            }
            if (m_rightTriggerButton != null && m_rightTriggerButton.action != null)
            {
                m_rightTriggerButton.action.performed += ActivateRightSofa;
                m_rightTriggerButton.action.canceled += DesactivateRightSofa;
            }
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (m_leftTriggerButton != null && m_leftTriggerButton.action != null)
            {
                m_leftTriggerButton.action.performed -= ActivateLeftSofa;
                m_leftTriggerButton.action.canceled -= DesactivateLeftSofa;
            }
            if (m_rightTriggerButton != null && m_rightTriggerButton.action != null)
            {
                m_rightTriggerButton.action.performed -= ActivateRightSofa;
                m_rightTriggerButton.action.canceled -= DesactivateRightSofa;
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        
        // --- MANO IZQUIERDA ---
        private void ActivateLeftSofa(InputAction.CallbackContext obj)
        {
            StartCoroutine(RetrasoSofaIzquierda());
        }

        private IEnumerator RetrasoSofaIzquierda()
        {
            yield return new WaitForSeconds(0.05f);

            // ¿El dedo medio izquierdo tiene una pinza agarrada? Si es así, aborta.
            if (manoIzquierdaXR != null && manoIzquierdaXR.hasSelection)
            {
                yield break; 
            }

            // Si la mano está vacía, el dedo índice jala el tejido en SOFA
            if (m_leftSofaLaserModel != null)
            {
                m_leftSofaLaserModel.ActivateTool = true;
            }
        }

        private void DesactivateLeftSofa(InputAction.CallbackContext obj)
        {
            if (m_leftSofaLaserModel != null)
            {
                m_leftSofaLaserModel.ActivateTool = false;
            }
        }


        // --- MANO DERECHA ---
        private void ActivateRightSofa(InputAction.CallbackContext obj)
        {
            StartCoroutine(RetrasoSofaDerecha());
        }

        private IEnumerator RetrasoSofaDerecha()
        {
            yield return new WaitForSeconds(0.05f);

            // ¿El dedo medio derecho tiene una pinza agarrada? Si es así, aborta.
            if (manoDerechaXR != null && manoDerechaXR.hasSelection)
            {
                yield break; 
            }

            // Si la mano está vacía, el dedo índice jala el tejido en SOFA
            if (m_rightSofaLaserModel != null)
            {
                m_rightSofaLaserModel.ActivateTool = true;
            }
        }

        private void DesactivateRightSofa(InputAction.CallbackContext obj)
        {
            if (m_rightSofaLaserModel != null)
            {
                m_rightSofaLaserModel.ActivateTool = false;
            }
        }

#endif
    }
}