using UnityEngine;

public class TactGloveManager : MonoBehaviour
{
    public static TactGloveManager Instance;

    [Header("Configuración")]
    [Range(0f, 1f)]
    public float defaultIntensity = 0.7f;

    public float defaultDuration = 0.1f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Vibrate(float intensity = -1f, float duration = -1f)
    {
        if (intensity < 0)
            intensity = defaultIntensity;

        if (duration < 0)
            duration = defaultDuration;

        Debug.Log(
            $"[TACTGLOVE] Vibración -> Intensidad: {intensity}, Duración: {duration}"
        );

        // AQUÍ irá la llamada al SDK bHaptics
    }
}