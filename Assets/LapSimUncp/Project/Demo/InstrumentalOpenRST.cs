using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Soporte del instrumental OpenRST (jcolan/OpenRST) para la demo automática.
///
/// Los dos UR10e de la escena ya llevan montado el instrumento OpenRST completo
/// (forceps_base → tubo interface de 31 cm → pitch → dos dedos). Esta clase:
///  - localiza esas piezas en la punta de cada brazo,
///  - carga en tiempo de ejecución las mordazas intercambiables del repositorio
///    (fenestrated / maryland / needle driver, en Models/OpenRST_EE) y las intercambia
///    en los dedos según la fase quirúrgica,
///  - anima la apertura y el cierre de la pinza,
///  - y restaura los dedos originales al terminar. No modifica assets ni la escena.
/// </summary>
public class InstrumentalOpenRST
{
    public enum Mordaza { Original, Fenestrada, Maryland, PortaAgujas }

    public Transform punta;            // interface_link (el que usa IntercambioHerramienta)
    public Transform eeLink;           // extremo del instrumento (entre los dedos)
    public Transform dedoIzq, dedoDer; // finger_left_link / finger_right_link
    private MeshFilter visIzq, visDer;
    private Mesh mallaIzqOriginal, mallaDerOriginal;
    private Quaternion rotIzqOriginal, rotDerOriginal;
    private float aperturaActual;

    private static readonly Dictionary<string, Mesh> cacheStl = new Dictionary<string, Mesh>();

    public static InstrumentalOpenRST Buscar(Transform puntaBrazo)
    {
        if (puntaBrazo == null) return null;
        var o = new InstrumentalOpenRST { punta = puntaBrazo };
        foreach (var t in puntaBrazo.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ee_link" && o.eeLink == null) o.eeLink = t;
            if (t.name == "finger_left_link" && o.dedoIzq == null && t.parent.name == "pitch_link") o.dedoIzq = t;
            if (t.name == "finger_right_link" && o.dedoDer == null && t.parent.name == "pitch_link") o.dedoDer = t;
            if (t.name == "finger_left_link_0" && o.visIzq == null) o.visIzq = t.GetComponent<MeshFilter>();
            if (t.name == "finger_right_link_0" && o.visDer == null) o.visDer = t.GetComponent<MeshFilter>();
        }
        if (o.eeLink == null || o.dedoIzq == null || o.dedoDer == null || o.visIzq == null || o.visDer == null)
        {
            Debug.LogWarning($"OpenRST: no se encontró el efector completo bajo '{puntaBrazo.name}'.");
            return null;
        }
        o.mallaIzqOriginal = o.visIzq.sharedMesh;
        o.mallaDerOriginal = o.visDer.sharedMesh;
        o.rotIzqOriginal = o.dedoIzq.localRotation;
        o.rotDerOriginal = o.dedoDer.localRotation;
        return o;
    }

    /// <summary>Vuelve a activar la punta OpenRST (IntercambioHerramienta la oculta al tomar un instrumento).</summary>
    public void Mostrar()
    {
        for (var t = eeLink; t != null && t != punta.parent; t = t.parent) t.gameObject.SetActive(true);
        foreach (Transform h in punta) if (!h.name.StartsWith("PivoteManoRobot")) h.gameObject.SetActive(true);
        foreach (Transform h in punta.parent) h.gameObject.SetActive(true);
    }

    public void CambiarMordaza(Mordaza m)
    {
        Mesh a = null, b = null;
        switch (m)
        {
            // La misma mordaza 'a' en ambos dedos: el espejo lo hacen los transforms de los links,
            // igual que el URDF usa un único STL de dedo para los dos lados.
            case Mordaza.Fenestrada:  a = b = Stl("fenestrated_jaw_link_a");   break;
            case Mordaza.Maryland:    a = b = Stl("maryland_jaw_link_a");      break;
            case Mordaza.PortaAgujas: a = b = Stl("needle_driver_jaw_link_a"); break;
        }
        visIzq.sharedMesh = a != null ? a : mallaIzqOriginal;
        visDer.sharedMesh = b != null ? b : mallaDerOriginal;
    }

    /// <summary>Apertura de la pinza en grados (0 = cerrada). Gira cada dedo sobre su eje de bisagra.</summary>
    public void Apertura(float grados)
    {
        aperturaActual = grados;
        dedoIzq.localRotation = rotIzqOriginal * Quaternion.AngleAxis(+grados, Vector3.forward);
        dedoDer.localRotation = rotDerOriginal * Quaternion.AngleAxis(-grados, Vector3.forward);
    }

    public void Restaurar()
    {
        if (visIzq != null) visIzq.sharedMesh = mallaIzqOriginal;
        if (visDer != null) visDer.sharedMesh = mallaDerOriginal;
        if (dedoIzq != null) dedoIzq.localRotation = rotIzqOriginal;
        if (dedoDer != null) dedoDer.localRotation = rotDerOriginal;
    }

    /// <summary>Solo para diagnóstico desde el editor.</summary>
    public static Mesh CargarStlBinarioPublico(string nombre) => Stl(nombre);

    static Mesh Stl(string nombre)
    {
        if (cacheStl.TryGetValue(nombre, out var m)) return m;
        string ruta = Path.Combine(Application.dataPath, "LapSimUncp/Models/OpenRST_EE", nombre + ".STL");
        m = CargarStlBinario(ruta);
        cacheStl[nombre] = m;
        return m;
    }

    /// <summary>Lector mínimo de STL binario (84 bytes de cabecera + 50 por triángulo, en metros).</summary>
    static Mesh CargarStlBinario(string ruta)
    {
        try
        {
            var datos = File.ReadAllBytes(ruta);
            int n = System.BitConverter.ToInt32(datos, 80);
            if (84 + n * 50 > datos.Length) { Debug.LogWarning("STL inesperado: " + ruta); return null; }
            var verts = new List<Vector3>(n * 3);
            var norms = new List<Vector3>(n * 3);
            var tris = new int[n * 3];
            int off = 84;
            for (int i = 0; i < n; i++)
            {
                float nx = System.BitConverter.ToSingle(datos, off), ny = System.BitConverter.ToSingle(datos, off + 4), nz = System.BitConverter.ToSingle(datos, off + 8);
                var nrm = new Vector3(nx, ny, nz);
                for (int k = 0; k < 3; k++)
                {
                    int p = off + 12 + k * 12;
                    verts.Add(new Vector3(System.BitConverter.ToSingle(datos, p), System.BitConverter.ToSingle(datos, p + 4), System.BitConverter.ToSingle(datos, p + 8)));
                    norms.Add(nrm);
                    tris[i * 3 + k] = i * 3 + k;
                }
                off += 50;
            }
            // Los STL de la carpeta CAD vienen en milímetros; los del URDF, en metros.
            // Si la pieza "mide" más de 30 cm es que está en mm: se pasa a metros.
            Vector3 mn = verts[0], mx = verts[0];
            foreach (var v in verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            if ((mx - mn).magnitude > 0.3f)
            {
                // Además de mm→m, los STL del CAD usan otro marco que los dedos del URDF:
                // la mordaza corre por +Y desde 0; el dedo corre por +X con la bisagra en el origen.
                // Rotación propia + recentrado: x' = y - 3.5 mm, y' = z - 3 mm, z' = 3.5 mm - x.
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 v = verts[i] * 0.001f;
                    verts[i] = new Vector3(v.y - 0.0035f, v.z - 0.003f, 0.0035f - v.x);
                }
                for (int i = 0; i < norms.Count; i++)
                {
                    Vector3 nv = norms[i];
                    norms[i] = new Vector3(nv.y, nv.z, -nv.x);
                }
            }

            var m = new Mesh { name = Path.GetFileNameWithoutExtension(ruta) };
            m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            Debug.Log($"OpenRST STL '{m.name}': bounds c={m.bounds.center.ToString("F4")} s={m.bounds.size.ToString("F4")}");
            return m;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"No se pudo leer el STL '{ruta}': {e.Message}");
            return null;
        }
    }
}
