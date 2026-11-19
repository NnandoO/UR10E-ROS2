using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using SofaUnityAPI; // <-- Soluciona el error CS0103 de SofaDefines

namespace SofaUnity
{
    [DefaultExecutionOrder(-500)]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(SofaLaserModel))]
    public class SofaInstrumentoQuirurgico : MonoBehaviour
    {
        [Header("1. Componentes")]
        public SphereCollider esferaPunta;
        public SofaLaserModel miLaserSofa;

        [Header("2. Botón de Acción SOFA (Dedo Medio / Grip)")]
        [Range(0.1f, 0.95f)]
        public float umbralBoton = 0.5f;
        public float alcancePunta = 0.08f;

        [Header("3. Tipo de Instrumento")]
        [Tooltip("Márcalo en TRUE en la Tijera o Hook para forzar que SOFA C++ lo cree como CuttingTool (0) y no como AttachTool (1).")]
        public bool esHerramientaDeCorte = false;

        [Tooltip("Ponlo en TRUE si quieres que corte con pequeños pulsos al mantener presionado el botón, o FALSE para corte continuo.")]
        public bool usarPulsosDeCorte = false;
        public float tiempoEntreCortes = 0.15f;

        [Header("4. Estado en Vivo (Solo lectura)")]
        public bool instrumentoTomado = false;
        public bool accionActiva = false;
        public float valorGripActual = 0f;
        public XRNode manoQueLoSostiene = XRNode.RightHand;

        [HideInInspector] public GameObject instrumentoRaiz;

        /// <summary>
        /// Si se asigna, el rayo SOFA apunta hacia este Transform en vez de usar el forward de la punta.
        /// Lo usa la Demo automática para dirigir el corte con precisión. Dejar en null en uso normal.
        /// </summary>
        [HideInInspector] public Transform apuntarHacia;

        private XRGrabInteractable grabPadre;
        private ControladorFulcroLaparoscopio fulcroPadre;
        private SofaContext contextoSofa;
        private Transform anclaEnLaPunta;
        private float ultimoTiempoCorte = -10f;

        void Awake()
        {
            esferaPunta = GetComponent<SphereCollider>();
            miLaserSofa = GetComponent<SofaLaserModel>();

            if (esferaPunta != null)
            {
                esferaPunta.isTrigger = true;
                esferaPunta.enabled = false;
            }

            grabPadre = GetComponentInParent<XRGrabInteractable>();
            instrumentoRaiz = grabPadre != null ? grabPadre.gameObject : transform.root.gameObject;
            fulcroPadre = instrumentoRaiz.GetComponent<ControladorFulcroLaparoscopio>();

            if (grabPadre != null)
            {
                grabPadre.selectEntered.AddListener(AlTomar);
                grabPadre.selectExited.AddListener(AlSoltar);
            }

            // Detecta automáticamente si es tijera o hook por el nombre de la herramienta
            string nombreRaiz = instrumentoRaiz.name.ToLower();
            if (nombreRaiz.Contains("tijera") || nombreRaiz.Contains("hook") || nombreRaiz.Contains("scissor") || nombreRaiz.Contains("corte"))
            {
                esHerramientaDeCorte = true;
            }

            // Nombre único ANTES de que SofaRayCaster.Start() cree el rayo en C++
            gameObject.name = $"PuntaActiva_{instrumentoRaiz.name}";

            IntegrarEnSofaContext();
        }

        private void IntegrarEnSofaContext()
        {
            contextoSofa = FindFirstObjectByType<SofaContext>();
            if (contextoSofa == null || miLaserSofa == null) return;

            if (transform.parent != contextoSofa.transform)
            {
                GameObject marcador = new GameObject($"AnclaPunta_{instrumentoRaiz.name}");
                anclaEnLaPunta = marcador.transform;
                anclaEnLaPunta.SetParent(transform.parent, false);
                anclaEnLaPunta.localPosition = transform.localPosition;
                anclaEnLaPunta.localRotation = transform.localRotation;
                anclaEnLaPunta.localScale = transform.localScale;

                transform.SetParent(contextoSofa.transform, true);
            }

            miLaserSofa.m_sofaContext = contextoSofa;

            // Fuerza el tipo correcto (CuttingTool vs AttachTool) antes de que corra SofaRayCaster.Start()
            if (esHerramientaDeCorte)
            {
                miLaserSofa.RayInteractionType = SofaDefines.SRayInteraction.CuttingTool;
            }
            else
            {
                miLaserSofa.RayInteractionType = SofaDefines.SRayInteraction.AttachTool;
            }

            miLaserSofa.ActivateRay = true;
            miLaserSofa.ActivateTool = false;
            miLaserSofa.Length = alcancePunta;
        }

        void OnDestroy()
        {
            if (grabPadre != null)
            {
                grabPadre.selectEntered.RemoveListener(AlTomar);
                grabPadre.selectExited.RemoveListener(AlSoltar);
            }

            if (anclaEnLaPunta != null)
            {
                Destroy(anclaEnLaPunta.gameObject);
            }
        }

        private void AlTomar(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            instrumentoTomado = true;
            DetectarMano(args.interactorObject.transform);
        }

        private void AlSoltar(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            if (fulcroPadre == null || fulcroPadre.mandoVRActual == null)
            {
                instrumentoTomado = false;
                ActualizarEstadoHerramienta(false);
            }
        }

        public void ConfigurarDesdeBrazoRobotico(bool tomadoPorRobot, XRNode manoRobot)
        {
            instrumentoTomado = tomadoPorRobot;
            manoQueLoSostiene = manoRobot;
            if (!tomadoPorRobot) ActualizarEstadoHerramienta(false);
        }

        /// <summary>
        /// Cambia el alcance del rayo SOFA en vivo. La longitud se fija en C++ al crear el rayo, así que se
        /// recrea alternando el tipo de interacción. La Demo lo acorta a ~1 cm antes de cortar el conducto
        /// cístico para no dañar el tejido que queda detrás, y luego lo restaura.
        /// </summary>
        public void AjustarAlcance(float metros)
        {
            if (miLaserSofa == null) return;
            alcancePunta = metros;
            miLaserSofa.Length = metros;
            var tipo = miLaserSofa.RayInteractionType;
            var otro = tipo == SofaDefines.SRayInteraction.CuttingTool
                ? SofaDefines.SRayInteraction.AttachTool
                : SofaDefines.SRayInteraction.CuttingTool;
            miLaserSofa.RayInteractionType = otro;   // destruye el rayo actual y crea otro
            miLaserSofa.RayInteractionType = tipo;   // vuelve a su tipo, ya con la longitud nueva
            miLaserSofa.ActivateRay = true;
            miLaserSofa.ActivateTool = accionActiva;
        }

        private void DetectarMano(Transform interactor)
        {
            if (interactor == null) return;
            string ruta = "";
            Transform t = interactor;
            while (t != null)
            {
                ruta += t.name.ToLower() + "/";
                t = t.parent;
            }
            if (ruta.Contains("seguimientol") || ruta.Contains("left") || ruta.Contains("izq"))
                manoQueLoSostiene = XRNode.LeftHand;
            else
                manoQueLoSostiene = XRNode.RightHand;
        }

        void LateUpdate()
        {
            if (anclaEnLaPunta != null)
            {
                transform.position = anclaEnLaPunta.position;
                transform.rotation = anclaEnLaPunta.rotation;
            }

            if (apuntarHacia != null)
            {
                Vector3 haciaObjetivo = apuntarHacia.position - transform.position;
                if (haciaObjetivo.sqrMagnitude > 1e-10f)
                    transform.rotation = Quaternion.LookRotation(haciaObjetivo.normalized, Vector3.up);
            }

            if (fulcroPadre != null && fulcroPadre.mandoVRActual != null)
            {
                instrumentoTomado = true;
            }

            bool botonGripPresionado = LeerGripMando();

            if (esHerramientaDeCorte && usarPulsosDeCorte)
            {
                if (botonGripPresionado)
                {
                    float dt = Time.time - ultimoTiempoCorte;
                    if (dt >= tiempoEntreCortes)
                    {
                        ultimoTiempoCorte = Time.time;
                        ActualizarEstadoHerramienta(true);
                    }
                    else if (dt > 0.05f && accionActiva)
                    {
                        ActualizarEstadoHerramienta(false);
                    }
                }
                else if (accionActiva)
                {
                    ActualizarEstadoHerramienta(false);
                }
            }
            else
            {
                if (botonGripPresionado != accionActiva)
                {
                    ActualizarEstadoHerramienta(botonGripPresionado);
                }
            }
        }

        // ---------- Control externo (Demo automática) ----------
        private bool accionForzada = false;
        private bool valorAccionForzada = false;

        /// <summary>Activa/desactiva la acción SOFA (agarre o corte) desde código, ignorando mandos y teclado.</summary>
        public void ForzarAccion(bool activa)
        {
            accionForzada = true;
            valorAccionForzada = activa;
        }

        /// <summary>Devuelve el control de la acción a los mandos/teclado.</summary>
        public void LiberarAccionForzada()
        {
            accionForzada = false;
            valorAccionForzada = false;
        }

        private bool LeerGripMando()
        {
            if (accionForzada)
            {
                valorGripActual = valorAccionForzada ? 1f : 0f;
                return valorAccionForzada;
            }

            if (Input.GetKey(KeyCode.G))
            {
                valorGripActual = 1f;
                return true;
            }

            if (!instrumentoTomado)
            {
                valorGripActual = 0f;
                return false;
            }

            InputDevice mando = InputDevices.GetDeviceAtXRNode(manoQueLoSostiene);
            if (mando.isValid && mando.TryGetFeatureValue(CommonUsages.grip, out valorGripActual))
            {
                return valorGripActual >= umbralBoton;
            }

            return false;
        }

        private void ActualizarEstadoHerramienta(bool estado)
        {
            accionActiva = estado;

            if (esferaPunta != null)
                esferaPunta.enabled = estado;

            if (miLaserSofa != null)
            {
                miLaserSofa.ActivateRay = true;
                miLaserSofa.ActivateTool = estado;
            }
        }
    }
}