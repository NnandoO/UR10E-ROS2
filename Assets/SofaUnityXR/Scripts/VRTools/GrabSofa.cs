using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SofaUnity;


#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SofaUnityXR
{
    public class GrabSofa : MonoBehaviour
    {
        public UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor manoIzquierdaXR;
        public UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor manoDerechaXR;

#if ENABLE_INPUT_SYSTEM
        [SerializeField] private InputActionReference m_leftGripButton = null;
        [SerializeField] private InputActionReference m_rightGripButton = null;
#endif

        [SerializeField] private SofaLaserModel m_leftSofaLaserModel = null;
        [SerializeField] private SofaLaserModel m_rightSofaLaserModel = null;

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            m_leftGripButton.action.performed += ActivateLeftGrab;
            m_leftGripButton.action.canceled += DesactivateLeftGrab;
            m_rightGripButton.action.performed += ActivateRightGrab;
            m_rightGripButton.action.canceled += DesactivateRightGrab;
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            m_leftGripButton.action.performed -= ActivateLeftGrab;
            m_leftGripButton.action.canceled -= DesactivateLeftGrab;
            m_rightGripButton.action.performed -= ActivateRightGrab;
            m_rightGripButton.action.canceled -= DesactivateRightGrab;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        
        // --- MANO IZQUIERDA ---
        private void ActivateLeftGrab(InputAction.CallbackContext obj)
        {
            // Iniciamos el temporizador en lugar de actuar de inmediato
            StartCoroutine(RetrasoSofaIzquierda());
        }

        private IEnumerator RetrasoSofaIzquierda()
        {
            // 1. Esperamos 5 centésimas de segundo para que Unity agarre el bisturí
            yield return new WaitForSeconds(0.05f);

            // 2. Ahora sí revisamos el candado. Si Unity agarró algo, abortamos.
            if (manoIzquierdaXR != null && manoIzquierdaXR.hasSelection)
            {
                yield break; 
            }

            // 3. Si la mano está vacía, jalamos el tejido de SOFA
            m_leftSofaLaserModel.ActivateTool = true;
        }

        private void DesactivateLeftGrab(InputAction.CallbackContext obj)
        {
            // Soltar sí debe ser instantáneo
            m_leftSofaLaserModel.ActivateTool = false;
        }


        // --- MANO DERECHA ---
        private void ActivateRightGrab(InputAction.CallbackContext obj)
        {
            StartCoroutine(RetrasoSofaDerecha());
        }

        private IEnumerator RetrasoSofaDerecha()
        {
            yield return new WaitForSeconds(0.05f);

            if (manoDerechaXR != null && manoDerechaXR.hasSelection)
            {
                yield break; 
            }

            m_rightSofaLaserModel.ActivateTool = true;
        }

        private void DesactivateRightGrab(InputAction.CallbackContext obj)
        {
            m_rightSofaLaserModel.ActivateTool = false;
        }

#endif
    }
}