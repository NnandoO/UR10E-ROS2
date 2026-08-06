using UnityEngine;
using EzySlice;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CorteBiliarLaparoscopico : MonoBehaviour
{
    [Header("Referencias de Corte")]
    public Transform planoDeCorte;          // El objeto vacío que está en la punta de tu bisturí/tijera
    public Transform planoPredefinido;      // El plano invisible colocado exactamente en el conducto cístico
    public LayerMask capaBiliar;            // La capa asignada a tu órgano ('Corte_Organo')
    public Material materialInterior;       // El material para la sección transversal del corte

    [Header("Configuración de Precisión")]
    public float distanciaDeActivacion = 0.5f; // Lo subí un poco para que no sea tan difícil atinarle en VR
    private bool yaFueCortado = false;

    // Variable para guardar el componente de agarre de la tijera
    private XRGrabInteractable interactableBisturi;

    void Start()
    {
        // EL CAMBIO CLAVE: Busca el componente en el objeto padre (el mango)
        interactableBisturi = GetComponentInParent<XRGrabInteractable>();

        if (interactableBisturi != null)
        {
            // Suscribimos nuestra función de corte al evento del gatillo (botón índice)
            interactableBisturi.activated.AddListener(IntentarCorteAlPresionar);
        }
        else
        {
            Debug.LogError("No se encontró un XRGrabInteractable en el bisturí ni en sus padres.");
        }
    }

    private void IntentarCorteAlPresionar(ActivateEventArgs args)
    {
        if (yaFueCortado || planoPredefinido == null || planoDeCorte == null) return;

        float distancia = Vector3.Distance(planoDeCorte.position, planoPredefinido.position);

        if (distancia <= distanciaDeActivacion)
        {
            Collider[] organosTocados = Physics.OverlapSphere(planoDeCorte.position, distanciaDeActivacion, capaBiliar);

            foreach (Collider col in organosTocados)
            {
                GameObject organoBiliar = col.gameObject;

                if (SeccionarMallaConPlanoFijo(organoBiliar, planoPredefinido))
                {
                    yaFueCortado = true;
                    Debug.Log("¡Corte quirúrgico perfecto ejecutado con el gatillo!");
                    break;
                }
            }
        }
        else
        {
            Debug.Log("Gatillo presionado, pero el bisturí está demasiado lejos. Distancia: " + distancia);
        }
    }

    private bool SeccionarMallaConPlanoFijo(GameObject organo, Transform planoFijo)
    {
        MeshFilter filtroMalla = organo.GetComponent<MeshFilter>();
        if (filtroMalla == null) return false;

        SlicedHull corte = organo.Slice(planoFijo.position, planoFijo.up);

        if (corte != null)
        {
            FixedJoint unionFija = organo.GetComponent<FixedJoint>();
            if (unionFija != null)
            {
                Destroy(unionFija);
            }

            GameObject mitadSuperior = corte.CreateUpperHull(organo, materialInterior);
            GameObject mitadInferior = corte.CreateLowerHull(organo, materialInterior);

            ConfigurarPedazo(mitadSuperior, organo);
            ConfigurarPedazo(mitadInferior, organo);

            Destroy(organo);
            return true;
        }
        
        return false;
    }

    private void ConfigurarPedazo(GameObject pedazo, GameObject original)
    {
        pedazo.transform.position = original.transform.position;
        pedazo.transform.rotation = original.transform.rotation;
        pedazo.transform.localScale = original.transform.localScale;

        Meshs_AssignLayerAndTag(pedazo, original);

        // CONFIGURACIÓN SIN GRAVEDAD
        Rigidbody rb = pedazo.AddComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false; // Apagamos la gravedad
        rb.isKinematic = true; // Congelamos el objeto en el espacio

        MeshCollider colliderMesh = pedazo.AddComponent<MeshCollider>();
        colliderMesh.convex = true; 

        XRGrabInteractable grabInteractable = pedazo.AddComponent<XRGrabInteractable>();
        
        // Al agarrar el pedazo con las pinzas, se libera para moverse pero sigue sin gravedad
        grabInteractable.selectEntered.AddListener((args) => {
            rb.isKinematic = false; 
        });
    }

    private void Meshs_AssignLayerAndTag(GameObject nuevoObjeto, GameObject modeloBase)
    {
        nuevoAnularCapa(nuevoObjeto, modeloBase.layer);
        nuevoObjeto.tag = modeloBase.tag;
    }

    private void nuevoAnularCapa(GameObject obj, int capa)
    {
        obj.layer = capa;
        foreach (Transform hijo in obj.transform)
        {
            nuevoAnularCapa(hijo.gameObject, capa);
        }
    }
}