#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Services;

/// <summary>
/// Herramienta de desarrollo: al cargar el editor (y tras cada recompilación) reconecta la sesión de
/// "MCP for Unity" si no está activa, para no tener que pulsar "Start Session" a mano.
/// También disponible en el menú Tools > Reconectar MCP.
/// </summary>
[InitializeOnLoad]
public static class ReconectarMCP
{
    static ReconectarMCP()
    {
        EditorApplication.delayCall += () => Reconectar(false);
    }

    [MenuItem("Tools/Reconectar MCP")]
    public static void ReconectarDesdeMenu() => Reconectar(true);

    static async void Reconectar(bool avisar)
    {
        try
        {
            if (MCPServiceLocator.Bridge.IsRunning) { if (avisar) Debug.Log("MCP: la sesión ya estaba activa."); return; }
            bool ok = await MCPServiceLocator.Bridge.StartAsync();
            if (avisar || ok) Debug.Log(ok ? "MCP: sesión reconectada." : "MCP: no se pudo reconectar (¿está el servidor en marcha?).");
        }
        catch (System.Exception e)
        {
            if (avisar) Debug.LogWarning("MCP: error al reconectar: " + e.Message);
        }
    }
}
#endif
