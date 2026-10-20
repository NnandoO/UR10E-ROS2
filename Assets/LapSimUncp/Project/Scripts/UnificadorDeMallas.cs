using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor; // Necesario para guardar archivos físicos
#endif

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class UnificadorDeMallas : MonoBehaviour
{
    [ContextMenu("Unir Mallas y Guardar Archivo")]
    public void UnirMallas()
    {
        MeshFilter[] filtrosHijos = GetComponentsInChildren<MeshFilter>();
        List<CombineInstance> combinadores = new List<CombineInstance>();
        List<Material> materiales = new List<Material>();

        for (int i = 0; i < filtrosHijos.Length; i++)
        {
            if (filtrosHijos[i].gameObject == this.gameObject) continue;

            CombineInstance ci = new CombineInstance();
            ci.mesh = filtrosHijos[i].sharedMesh;
            ci.transform = transform.worldToLocalMatrix * filtrosHijos[i].transform.localToWorldMatrix;
            combinadores.Add(ci);

            MeshRenderer renderHijo = filtrosHijos[i].GetComponent<MeshRenderer>();
            if (renderHijo != null)
            {
                materiales.AddRange(renderHijo.sharedMaterials);
                renderHijo.gameObject.SetActive(false); 
            }
        }

        Mesh mallaGigante = new Mesh();
        mallaGigante.name = "Malla_Fusionada_" + gameObject.name;
        mallaGigante.CombineMeshes(combinadores.ToArray(), false); 

        GetComponent<MeshFilter>().sharedMesh = mallaGigante;
        GetComponent<MeshRenderer>().sharedMaterials = materiales.ToArray();

        // --- NUEVA PARTE: GUARDAR COMO ARCHIVO FÍSICO ---
#if UNITY_EDITOR
        // Ruta donde se guardará (Directamente en la carpeta principal de Assets)
        string ruta = $"Assets/Malla_Fusionada_{gameObject.name}.asset";
        
        // Crear el archivo físico
        AssetDatabase.CreateAsset(mallaGigante, AssetDatabase.GenerateUniqueAssetPath(ruta));
        AssetDatabase.SaveAssets();
        
        Debug.Log($"✅ Malla fusionada guardada permanentemente en: {ruta}");
#else
        Debug.Log("✅ Mallas unidas solo en memoria (no estás en el Editor).");
#endif
    }
}