using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Comportamiento físico del trocar una vez colocado en su puerto. PreparacionQuirurgica lo añade
/// en tiempo de ejecución a cada trocar; no hay que configurarlo en la escena.
///
///  - Pivote: un trocar real no está rígido, bascula en la pared abdominal con el instrumento que lo
///    atraviesa. Aquí el trocar (y su socket) giran alrededor del punto de fulcro para seguir el eje del
///    instrumento, así el instrumento va siempre por el centro de la cánula en vez de atravesar sus paredes.
///    Al retirar el instrumento vuelve a su orientación de reposo.
///  - Obturador: el modelo trae el obturador puesto (punzón y mango negro). Con el trocar en la mesa se ve
///    entero; una vez colocado en su puerto el obturador se retira y queda la cánula hueca.
/// </summary>
[DefaultExecutionOrder(50)]
public class TrocarQuirurgico : MonoBehaviour
{
    [Tooltip("Retira el obturador automáticamente cuando el trocar queda en su socket y lo repone al sacarlo.")]
    public bool obturadorAutomatico = true;
    [Tooltip("Segundos que tarda el trocar en volver a su orientación de reposo al retirar el instrumento.")]
    public float tiempoRetorno = 0.5f;

    public bool ObturadorPuesto { get; private set; } = true;
    public bool TieneObturador => mallaCanula != null;
    /// <summary>X local del borde exterior del cabezal sin obturador (por donde entra el instrumento).</summary>
    public float XCabezal { get; private set; }

    private MeshFilter mf;
    private MeshRenderer mr;
    private Rigidbody rb;
    private XRGrabInteractable grab;
    private Transform fulcro;

    private Mesh mallaOriginal, mallaCanula, mallaObturador;
    private Material[] matsOriginal, matsCanula, matsObturador;

    private Transform socket;           // socket cuya pose de reposo está guardada
    private Vector3 reposoPos;
    private Quaternion reposoRot;
    private bool alineado;
    private float retorno = -1f;        // >= 0: volviendo al reposo
    private Quaternion retornoDesde;

    public static TrocarQuirurgico De(Transform puntoEntrada) =>
        puntoEntrada != null ? puntoEntrada.GetComponentInParent<TrocarQuirurgico>() : null;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        fulcro = transform.Find("PuntoEntrada");
        XCabezal = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.max.x : 0f;
        SepararObturador();
    }

    void OnDisable()
    {
        RestaurarReposo();
        PonerObturador(true);
    }

    void Update()
    {
        if (obturadorAutomatico && TieneObturador)
        {
            bool colocado = SocketActual() != null;
            if (colocado == ObturadorPuesto) PonerObturador(!colocado);
        }

        if (retorno >= 0f && socket != null)
        {
            retorno += Time.deltaTime;
            float u = tiempoRetorno > 0.01f ? Mathf.SmoothStep(0f, 1f, retorno / tiempoRetorno) : 1f;
            Quaternion objetivo = Quaternion.Slerp(retornoDesde, reposoRot, u);
            RotarSobreFulcro(objetivo * Quaternion.Inverse(socket.rotation));
            if (retorno >= tiempoRetorno) RestaurarReposo();
        }
    }

    // ------------------------------------------------------------------
    // Pivote
    // ------------------------------------------------------------------
    public Vector3 CentroFulcro
    {
        get
        {
            if (fulcro == null) return transform.position;
            var esfera = fulcro.GetComponent<SphereCollider>();
            return esfera != null ? fulcro.TransformPoint(esfera.center) : fulcro.position;
        }
    }

    /// <summary>Eje del tubo hacia dentro del paciente (el +X local de PuntoEntrada apunta al cabezal).</summary>
    public Vector3 EjeHaciaAdentro => fulcro != null ? -fulcro.right : -transform.right;

    XRSocketInteractor SocketActual() =>
        grab != null && grab.isSelected ? grab.firstInteractorSelecting as XRSocketInteractor : null;

    /// <summary>
    /// Gira el trocar alrededor de su fulcro para que el eje del tubo coincida con
    /// <paramref name="direccionHaciaAdentro"/> (el eje del instrumento). Solo actúa con el trocar en su socket.
    /// </summary>
    public void AlinearCon(Vector3 direccionHaciaAdentro)
    {
        var s = SocketActual();
        if (s == null || fulcro == null || direccionHaciaAdentro.sqrMagnitude < 1e-8f) return;
        if (socket != s.transform)
        {
            RestaurarReposo();
            socket = s.transform;
        }
        if (!alineado && retorno < 0f)
        {
            reposoPos = socket.position;
            reposoRot = socket.rotation;
        }
        alineado = true;
        retorno = -1f;
        Quaternion delta = Quaternion.FromToRotation(EjeHaciaAdentro, direccionHaciaAdentro.normalized);
        if (Quaternion.Angle(delta, Quaternion.identity) > 0.01f) RotarSobreFulcro(delta);
    }

    /// <summary>El instrumento salió: el trocar vuelve suavemente a su orientación de reposo.</summary>
    public void LiberarAlineacion()
    {
        if (!alineado || socket == null) return;
        retorno = 0f;
        retornoDesde = socket.rotation;
    }

    /// <summary>Vuelve de inmediato a la pose de reposo (la que dio el socket al colocar el trocar).</summary>
    public void RestaurarReposo()
    {
        if (socket != null && (alineado || retorno >= 0f))
        {
            RotarSobreFulcro(reposoRot * Quaternion.Inverse(socket.rotation));
            Vector3 ajuste = reposoPos - socket.position;
            socket.position = reposoPos;
            Mover(transform.position + ajuste, transform.rotation);
        }
        alineado = false;
        retorno = -1f;
    }

    void RotarSobreFulcro(Quaternion delta)
    {
        Vector3 c = CentroFulcro;
        socket.SetPositionAndRotation(c + delta * (socket.position - c), delta * socket.rotation);
        Mover(c + delta * (transform.position - c), delta * transform.rotation);
    }

    void Mover(Vector3 pos, Quaternion rot)
    {
        transform.SetPositionAndRotation(pos, rot);
        if (rb != null && rb.isKinematic) { rb.position = pos; rb.rotation = rot; }
    }

    // ------------------------------------------------------------------
    // Obturador
    // ------------------------------------------------------------------
    static bool EsDelObturador(Material m)
    {
        if (m == null) return false;
        string n = m.name.ToLowerInvariant();
        return n.Contains("handle") || n.Contains("steel") || n.Contains("blade");
    }

    void SepararObturador()
    {
        if (mf == null || mr == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
        mallaOriginal = mf.sharedMesh;
        matsOriginal = mr.sharedMaterials;
        var deCanula = new List<int>();
        var deObturador = new List<int>();
        for (int s = 0; s < mallaOriginal.subMeshCount; s++)
            (s < matsOriginal.Length && EsDelObturador(matsOriginal[s]) ? deObturador : deCanula).Add(s);
        if (deObturador.Count == 0 || deCanula.Count == 0) return;

        mallaCanula = Parte(mallaOriginal, deCanula, matsOriginal, out matsCanula, "Canula");
        mallaObturador = Parte(mallaOriginal, deObturador, matsOriginal, out matsObturador, "Obturador");
        XCabezal = mallaCanula.bounds.max.x;
    }

    static Mesh Parte(Mesh origen, List<int> submallas, Material[] mats, out Material[] matsParte, string sufijo)
    {
        var m = new Mesh { name = origen.name + "_" + sufijo, indexFormat = origen.indexFormat };
        Vector3[] v = origen.vertices;
        m.vertices = v;
        m.normals = origen.normals;
        m.uv = origen.uv;
        m.subMeshCount = submallas.Count;
        matsParte = new Material[submallas.Count];
        Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue;
        for (int k = 0; k < submallas.Count; k++)
        {
            int[] tri = origen.GetTriangles(submallas[k]);
            m.SetTriangles(tri, k);
            matsParte[k] = mats[submallas[k]];
            foreach (int i in tri) { mn = Vector3.Min(mn, v[i]); mx = Vector3.Max(mx, v[i]); }
        }
        m.bounds = new Bounds((mn + mx) * 0.5f, mx - mn);   // solo de los triángulos usados
        return m;
    }

    public void PonerObturador(bool puesto)
    {
        if (!TieneObturador || mf == null) return;
        ObturadorPuesto = puesto;
        mf.sharedMesh = puesto ? mallaOriginal : mallaCanula;
        mr.sharedMaterials = puesto ? matsOriginal : matsCanula;
    }

    /// <summary>
    /// Deja el trocar como cánula hueca y devuelve el obturador como objeto suelto, en la misma pose
    /// (para animar su retirada). Devuelve null si el modelo no tiene obturador separable.
    /// </summary>
    public GameObject SacarObturador()
    {
        if (!TieneObturador) return null;
        PonerObturador(false);
        var go = new GameObject("Obturador_" + name);
        go.transform.SetPositionAndRotation(transform.position, transform.rotation);
        go.transform.localScale = transform.lossyScale;
        go.AddComponent<MeshFilter>().sharedMesh = mallaObturador;
        go.AddComponent<MeshRenderer>().sharedMaterials = matsObturador;
        return go;
    }
}
