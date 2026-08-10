using UnityEngine;

// IMPORTANTE: Necesitamos la librería del Editor para poder guardar archivos
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshCollider))]
public class FusionarColisionador : MonoBehaviour
{
    [Header("Configuración de Guardado")]
    [Tooltip("El nombre con el que se guardará el archivo de la malla")]
    public string nombreDeLaMalla = "Colisionador_Herramienta";

    // Al agregar [ContextMenu], creamos un botón en el menú de Unity
    [ContextMenu("Generar y Guardar Malla Ahora")]
    public void GenerarMalla()
    {
        // 1. Buscamos todas las partes de la malla
        MeshFilter[] filtrosDeMalla = GetComponentsInChildren<MeshFilter>();
        if (filtrosDeMalla.Length == 0)
        {
            Debug.LogWarning("No se encontraron partes de malla (MeshFilter) en este objeto ni en sus hijos.");
            return;
        }

        CombineInstance[] piezas = new CombineInstance[filtrosDeMalla.Length];

        // 2. Extraemos la geometría y la ajustamos a la posición principal
        for (int i = 0; i < filtrosDeMalla.Length; i++)
        {
            piezas[i].mesh = filtrosDeMalla[i].sharedMesh;
            piezas[i].transform = transform.worldToLocalMatrix * filtrosDeMalla[i].transform.localToWorldMatrix;
        }

        // 3. Creamos la malla nueva unificada
        Mesh mallaUnificada = new Mesh();
        mallaUnificada.name = nombreDeLaMalla;
        mallaUnificada.CombineMeshes(piezas);

        // 4. Inyectamos la malla al MeshCollider
        MeshCollider colisionador = GetComponent<MeshCollider>();
        colisionador.sharedMesh = mallaUnificada;
        colisionador.convex = true;

// 5. Lógica para guardar la malla como archivo físico (Solo funciona en el Editor)
#if UNITY_EDITOR
        // Guardaremos el archivo directamente en la carpeta principal Assets
        string rutaCompleta = $"Assets/{nombreDeLaMalla}.asset";

        // Revisamos si ya existe una malla vieja con ese nombre para sobreescribirla
        Mesh mallaExistente = AssetDatabase.LoadAssetAtPath<Mesh>(rutaCompleta);
        if (mallaExistente != null)
        {
            mallaExistente.Clear();
            EditorUtility.CopySerialized(mallaUnificada, mallaExistente);
            colisionador.sharedMesh = mallaExistente;
        }
        else
        {
            // Si no existe, creamos el archivo físico nuevo
            AssetDatabase.CreateAsset(mallaUnificada, rutaCompleta);
            colisionador.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(rutaCompleta);
        }

        // Le decimos a Unity que guarde y actualice los archivos
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"✅ ¡Éxito! Malla fusionada y guardada físicamente en: {rutaCompleta}");
#else
        Debug.LogWarning("El guardado de archivos solo funciona dentro del Editor de Unity, no en el juego compilado.");
#endif
    }
}