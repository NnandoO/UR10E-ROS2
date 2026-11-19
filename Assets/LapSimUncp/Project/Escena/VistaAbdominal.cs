using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

/// <summary>Cómo se ve la pared abdominal del paciente.</summary>
public enum ModoParedAbdominal
{
    [Tooltip("Piel y músculos opacos: el interior solo se ve con el laparoscopio (como en quirófano).")]
    Normal,
    [Tooltip("Pared semitransparente: se ven órganos e instrumentos desde fuera.")]
    Translucida,
    [Tooltip("Pared oculta sobre el campo operatorio.")]
    Oculta
}

/// <summary>
/// Permite ver el interior del abdomen sin abrir al paciente: vuelve semitransparente (u oculta)
/// la pared abdominal ANTERIOR sobre el campo operatorio (piel, músculos, cartílagos y costillas anteriores).
///
/// No modifica assets ni la escena: en Play sustituye temporalmente los materiales de esos renderers
/// y los restaura al volver a Normal o al salir. Tampoco toca SOFA ni colisionadores.
///
/// Controles: T (teclado) o clic del joystick IZQUIERDO para alternar Normal -> Translúcida -> Oculta.
/// </summary>
public class VistaAbdominal : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Raíz del modelo anatómico (HumanBody). Vacío = se busca por nombre.")]
    public Transform cuerpo;

    [Header("Campo operatorio (coordenadas de mundo)")]
    [Tooltip("Centro de la caja que define la zona de la pared a aclarar (sobre hígado y vesícula).")]
    public Vector3 centroCampo = new Vector3(0.125f, 0.84f, 0.24f);
    public Vector3 tamañoCampo = new Vector3(0.40f, 0.14f, 0.40f);
    [Tooltip("Se aclaran las estructuras que llegan por encima de esta altura (todo lo que cubre los órganos desde delante: piel, músculos, fascias, epiplón, costillas anteriores).")]
    public float alturaMinimaPared = 0.825f;
    [Tooltip("Estructuras más grandes que esto (p. ej. toda la piel del tronco) se ignoran.")]
    public float tamañoMaximo = 1.3f;

    [Header("Solapes con los órganos SOFA")]
    [Tooltip("Oculta siempre las estructuras del modelo anatómico (ganglios, vasos, nervios, vísceras) cuyo centro cae dentro de los órganos SOFA: quedan 'dentro' del hígado/vesícula y ensucian la vista del laparoscopio.")]
    public bool ocultarSolapadosConSofa = true;
    [Tooltip("Margen (m) alrededor de los órganos SOFA para considerar una estructura solapada.")]
    public float margenSolape = 0.015f;
    [Tooltip("Estructuras del modelo anatómico que se ocultan siempre (en cirugía se apartan del campo).")]
    public string[] ocultarSiempre = { "Greater omentum", "Lesser omentum" };

    [Header("Apariencia")]
    public ModoParedAbdominal modoInicial = ModoParedAbdominal.Translucida;
    public Color colorTranslucido = new Color(1f, 0.82f, 0.74f, 0.18f);

    [Header("Controles")]
    [Tooltip("No uses V/C: activan el láser de corte de Tool_cut (GameObjectController).")]
    public KeyCode teclaAlternar = KeyCode.T;
    public bool usarClicJoystickIzquierdo = true;

    public ModoParedAbdominal ModoActual { get; private set; } = ModoParedAbdominal.Normal;

    private readonly List<Renderer> pared = new List<Renderer>();
    private readonly Dictionary<Renderer, Material[]> originales = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, ShadowCastingMode> sombras = new Dictionary<Renderer, ShadowCastingMode>();
    private readonly HashSet<Renderer> esPiel = new HashSet<Renderer>();

    /// <summary>La piel del modelo anatómico cuelga de "Regions of human body…".</summary>
    bool EsPiel(Transform t)
    {
        for (var p = t; p != null && p != cuerpo; p = p.parent)
            if (p.name.StartsWith("Regions of human body")) return !t.name.StartsWith("Cross Section") && !t.name.Contains("Hair");
        return false;
    }
    private Material matTranslucido;
    private InputDevice mandoIzq;
    private bool clicAnterior;
    private float refresco = 1f;

    void Start()
    {
        if (cuerpo == null)
        {
            var go = GameObject.Find("HumanBody");
            if (go != null) cuerpo = go.transform;
        }
        RecolectarPared();
        if (ocultarSolapadosConSofa) OcultarSolapados(true);
        AplicarModo(modoInicial);
    }

    void OnDisable()
    {
        AplicarModo(ModoParedAbdominal.Normal);
        OcultarSolapados(false);
    }

    private readonly List<Renderer> solapados = new List<Renderer>();

    void OcultarSolapados(bool ocultar)
    {
        if (!ocultar)
        {
            foreach (var r in solapados) if (r != null) r.enabled = true;
            solapados.Clear();
            return;
        }
        if (cuerpo == null) return;
        // Volumen ocupado por los órganos SOFA (sus modelos visuales OglModel)
        var organos = new List<Bounds>();
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (mr.name.Contains("OglModel")) { var b = mr.bounds; b.Expand(margenSolape * 2f); organos.Add(b); }
        if (organos.Count == 0) return;
        foreach (var r in cuerpo.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            if (System.Array.IndexOf(ocultarSiempre, r.name) >= 0)
            {
                pared.Remove(r); // que el cambio de modo no lo vuelva a mostrar
                r.enabled = false; solapados.Add(r); continue;
            }
            if (pared.Contains(r)) continue;
            Vector3 c = r.bounds.center;
            foreach (var b in organos)
                if (b.Contains(c)) { r.enabled = false; solapados.Add(r); break; }
        }
        Debug.Log($"🩻 VistaAbdominal: {solapados.Count} estructuras del modelo anatómico ocultas por solaparse con los órganos SOFA.");
    }

    void OnDestroy()
    {
        if (matTranslucido != null) Destroy(matTranslucido);
    }

    void Update()
    {
        if (Input.GetKeyDown(teclaAlternar)) Alternar();
        if (usarClicJoystickIzquierdo)
        {
            refresco += Time.unscaledDeltaTime;
            if (!mandoIzq.isValid && refresco >= 1f) { refresco = 0f; mandoIzq = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand); }
            bool clic = false;
            if (mandoIzq.isValid) mandoIzq.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out clic);
            if (clic && !clicAnterior) Alternar();
            clicAnterior = clic;
        }
    }

    public void Alternar() => AplicarModo((ModoParedAbdominal)(((int)ModoActual + 1) % 3));

    public void AplicarModo(ModoParedAbdominal modo)
    {
        ModoActual = modo;
        foreach (var r in pared)
        {
            if (r == null) continue;
            switch (modo)
            {
                case ModoParedAbdominal.Normal:
                    r.enabled = true;
                    if (originales.TryGetValue(r, out var mats)) r.sharedMaterials = mats;
                    if (sombras.TryGetValue(r, out var sombra)) r.shadowCastingMode = sombra;
                    break;
                case ModoParedAbdominal.Translucida:
                    // Solo la piel queda translúcida (silueta del paciente); lo que hay debajo y tapa
                    // el campo (músculos, fascias, epiplón, costillas, vasos…) se oculta para ver limpio.
                    bool piel = esPiel.Contains(r);
                    r.enabled = piel;
                    if (piel)
                    {
                        var arr = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < arr.Length; i++) arr[i] = MaterialTranslucido();
                        r.sharedMaterials = arr;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    break;
                case ModoParedAbdominal.Oculta:
                    r.enabled = false;
                    break;
            }
        }
    }

    void RecolectarPared()
    {
        pared.Clear();
        if (cuerpo == null) return;
        var caja = new Bounds(centroCampo, tamañoCampo);
        foreach (var r in cuerpo.GetComponentsInChildren<Renderer>(true))
        {
            Bounds b = r.bounds;
            // Todo lo que se cruza con el campo operatorio y sobresale por delante de los órganos
            if (!caja.Intersects(b) || b.max.y < alturaMinimaPared) continue;
            if (b.size.magnitude > tamañoMaximo) continue;
            pared.Add(r);
            originales[r] = r.sharedMaterials;
            sombras[r] = r.shadowCastingMode;
            if (EsPiel(r.transform)) esPiel.Add(r);
        }
        Debug.Log($"🩻 VistaAbdominal: {pared.Count} estructuras sobre el campo operatorio ({esPiel.Count} de piel).");
    }

    Material MaterialTranslucido()
    {
        if (matTranslucido != null) return matTranslucido;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        matTranslucido = new Material(sh) { name = "ParedAbdominal_Translucida (runtime)" };
        matTranslucido.SetFloat("_Surface", 1f);
        matTranslucido.SetFloat("_Blend", 0f);
        matTranslucido.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        matTranslucido.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        matTranslucido.SetFloat("_ZWrite", 0f);
        matTranslucido.SetFloat("_Smoothness", 0.3f);
        matTranslucido.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        matTranslucido.renderQueue = (int)RenderQueue.Transparent;
        matTranslucido.SetColor("_BaseColor", colorTranslucido);
        return matTranslucido;
    }
}
