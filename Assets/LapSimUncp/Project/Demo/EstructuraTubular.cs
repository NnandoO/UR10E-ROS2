using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Estructura tubular anatómica (conducto cístico, arteria cística, colédoco) para la demo.
///
/// Va de un extremo unido a la vesícula (que se mueve con ella: <see cref="extremoVesicula"/>) a un extremo
/// fijo en el paciente. Admite clips (grapas) y un corte entre clips: tras el corte, el muñón del lado del
/// paciente queda fijo con sus clips y el lado de la vesícula la acompaña (también cuando se extrae).
/// Malla generada por código en Play; no crea colisionadores ni toca SOFA.
/// </summary>
public class EstructuraTubular : MonoBehaviour
{
    public Func<Vector3> extremoVesicula;
    public Vector3 extremoFijo;
    public float radio = 0.0025f;
    public int segmentos = 24;
    public int lados = 10;

    private Vector3 a0, c1Rel, c2;            // forma de la curva al crearla
    private Mesh malla;
    private Material material;
    private Color color;

    // Corte
    public bool Cortado { get; private set; }
    private float sCorte;
    private readonly List<Vector3> munon = new List<Vector3>();        // lado del paciente (fijo)
    private readonly List<Vector3> ladoVesRel = new List<Vector3>();   // lado de la vesícula, relativo al extremo
    private const float hueco = 0.06f;                                 // separación entre los extremos cortados (fracción)

    private class Clip { public float s; public Transform t; }
    private readonly List<Clip> clips = new List<Clip>();
    private static Material matClip;

    public static EstructuraTubular Crear(string nombre, Transform padre, Func<Vector3> extremoVesicula, Vector3 extremoFijo,
                                          Vector3 curvatura, float radio, Color color)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var e = go.AddComponent<EstructuraTubular>();
        e.extremoVesicula = extremoVesicula;
        e.extremoFijo = extremoFijo;
        e.radio = radio;
        e.color = color;
        e.a0 = extremoVesicula();
        Vector3 d = extremoFijo - e.a0;
        e.c1Rel = d * 0.33f + curvatura;
        e.c2 = e.a0 + d * 0.66f + curvatura;
        e.malla = new Mesh { name = nombre };
        e.malla.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = e.malla;
        var mr = go.AddComponent<MeshRenderer>();
        e.material = CrearMaterial(color, transparente: true);
        mr.sharedMaterial = e.material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        e.Visibilidad = 0f;
        e.Reconstruir();
        return e;
    }

    /// <summary>0 = oculta bajo el peritoneo, 1 = disecada y visible.</summary>
    public float Visibilidad
    {
        get => material != null ? material.GetColor("_BaseColor").a : 0f;
        set
        {
            if (material == null) return;
            var c = color; c.a = Mathf.Clamp01(value);
            material.SetColor("_BaseColor", c);
            GetComponent<MeshRenderer>().enabled = c.a > 0.01f;
        }
    }

    // ------------------------------------------------------------------
    Vector3 Bezier(float s)
    {
        Vector3 p0 = extremoVesicula != null ? extremoVesicula() : a0;
        Vector3 p1 = p0 + c1Rel, p2 = c2, p3 = extremoFijo;
        float u = 1f - s;
        return u * u * u * p0 + 3f * u * u * s * p1 + 3f * u * s * s * p2 + s * s * s * p3;
    }

    /// <summary>Posición y tangente en la fracción s (0 = vesícula, 1 = paciente), con o sin corte.</summary>
    public void PosTangente(float s, out Vector3 pos, out Vector3 tan)
    {
        if (!Cortado)
        {
            pos = Bezier(s);
            tan = (Bezier(Mathf.Min(1f, s + 0.01f)) - Bezier(Mathf.Max(0f, s - 0.01f))).normalized;
            return;
        }
        if (s >= sCorte)
        {
            var pts = munon; float s0 = sCorte + hueco * 0.5f;
            float k = Mathf.InverseLerp(s0, 1f, s) * (pts.Count - 1);
            Muestra(pts, k, Vector3.zero, out pos, out tan);
        }
        else
        {
            Vector3 a = extremoVesicula != null ? extremoVesicula() : a0;
            float s1 = sCorte - hueco * 0.5f;
            float k = Mathf.InverseLerp(0f, s1, s) * (ladoVesRel.Count - 1);
            Muestra(ladoVesRel, k, a, out pos, out tan);
        }
    }

    static void Muestra(List<Vector3> pts, float k, Vector3 offset, out Vector3 pos, out Vector3 tan)
    {
        int i = Mathf.Clamp(Mathf.FloorToInt(k), 0, pts.Count - 2);
        float f = Mathf.Clamp01(k - i);
        pos = Vector3.Lerp(pts[i], pts[i + 1], f) + offset;
        tan = (pts[i + 1] - pts[i]).normalized;
    }

    /// <summary>Coloca un clip de titanio atravesado sobre la estructura en la fracción s.</summary>
    public Transform AgregarClip(float s)
    {
        var clip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(clip.GetComponent<Collider>());
        clip.name = $"Clip_{name}_{clips.Count + 1}";
        clip.transform.SetParent(transform, true);
        float largo = Mathf.Max(0.008f, radio * 4.2f);
        clip.transform.localScale = new Vector3(0.0016f, largo, 0.0012f);
        if (matClip == null)
        {
            matClip = CrearMaterial(new Color(0.78f, 0.80f, 0.84f), transparente: false);
            matClip.SetFloat("_Metallic", 0.9f);
            matClip.SetFloat("_Smoothness", 0.75f);
        }
        var r = clip.GetComponent<MeshRenderer>();
        r.sharedMaterial = matClip;
        r.shadowCastingMode = ShadowCastingMode.Off;
        clips.Add(new Clip { s = s, t = clip.transform });
        ActualizarClips();
        return clip.transform;
    }

    /// <summary>Secciona la estructura en s (entre clips).</summary>
    public void Cortar(float s)
    {
        if (Cortado) return;
        sCorte = s;
        munon.Clear(); ladoVesRel.Clear();
        Vector3 a = extremoVesicula != null ? extremoVesicula() : a0;
        float s0 = s + hueco * 0.5f, s1 = s - hueco * 0.5f;
        for (int i = 0; i <= segmentos; i++) munon.Add(Bezier(Mathf.Lerp(s0, 1f, i / (float)segmentos)));
        for (int i = 0; i <= segmentos; i++) ladoVesRel.Add(Bezier(Mathf.Lerp(0f, s1, i / (float)segmentos)) - a);
        Cortado = true;
        Reconstruir();
    }

    void LateUpdate()
    {
        Reconstruir();
    }

    void ActualizarClips()
    {
        foreach (var c in clips)
        {
            if (c.t == null) continue;
            PosTangente(c.s, out var p, out var t);
            Vector3 perp = Vector3.Cross(t, Vector3.up);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(t, Vector3.right);
            c.t.SetPositionAndRotation(p, Quaternion.LookRotation(t, perp.normalized));
        }
    }

    void Reconstruir()
    {
        if (malla == null) return;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
        if (!Cortado)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= segmentos; i++) pts.Add(Bezier(i / (float)segmentos));
            AgregarTubo(pts, verts, norms, tris);
        }
        else
        {
            Vector3 a = extremoVesicula != null ? extremoVesicula() : a0;
            var lado = new List<Vector3>(ladoVesRel.Count);
            foreach (var p in ladoVesRel) lado.Add(p + a);
            AgregarTubo(lado, verts, norms, tris);
            AgregarTubo(munon, verts, norms, tris);
        }
        // La malla se construye en coordenadas de mundo; el objeto queda en el origen
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        malla.Clear();
        malla.SetVertices(verts); malla.SetNormals(norms); malla.SetTriangles(tris, 0);
        malla.RecalculateBounds();
        ActualizarClips();
    }

    void AgregarTubo(List<Vector3> pts, List<Vector3> verts, List<Vector3> norms, List<int> tris)
    {
        int baseIdx = verts.Count;
        Vector3 nPrev = Vector3.zero;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 t = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            if (t.sqrMagnitude < 1e-8f) t = Vector3.forward;
            // Transporte paralelo: cada anillo conserva la orientación del anterior (sin torsión)
            Vector3 n = i == 0 ? Vector3.Cross(t, Vector3.up) : nPrev - t * Vector3.Dot(nPrev, t);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(t, Vector3.right);
            n.Normalize();
            nPrev = n;
            Vector3 b = Vector3.Cross(t, n);
            float r = radio * (i == 0 || i == pts.Count - 1 ? 0.85f : 1f);
            for (int k = 0; k < lados; k++)
            {
                float ang = k / (float)lados * Mathf.PI * 2f;
                Vector3 dir = n * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                verts.Add(pts[i] + dir * r);
                norms.Add(dir);
            }
        }
        for (int i = 0; i < pts.Count - 1; i++)
            for (int k = 0; k < lados; k++)
            {
                int a = baseIdx + i * lados + k, b2 = baseIdx + i * lados + (k + 1) % lados;
                int c = a + lados, d = b2 + lados;
                tris.Add(a); tris.Add(b2); tris.Add(c);   // caras hacia afuera (sentido horario de Unity)
                tris.Add(b2); tris.Add(d); tris.Add(c);
            }
    }

    void OnDestroy()
    {
        if (malla != null) Destroy(malla);
        if (material != null) Destroy(material);
    }

    public static Material CrearMaterial(Color color, bool transparente)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetFloat("_Smoothness", 0.6f);
        if (transparente)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        m.SetColor("_BaseColor", color);
        return m;
    }
}
