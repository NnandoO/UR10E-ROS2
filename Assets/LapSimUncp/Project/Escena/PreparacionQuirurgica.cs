using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Coloca los trocares en sus puertos del abdomen (XRSocketInteractor) al iniciar, como si el paciente
/// ya estuviera preparado. Cada socket está orientado hacia la vesícula, así que el trocar queda
/// atravesando la pared abdominal con su fulcro (PuntoEntrada) 1 cm bajo la piel.
///
/// Si prefieres colocar los trocares a mano (entrenamiento de colocación de puertos), desactiva
/// <see cref="colocarAlIniciar"/>: los trocares siguen en la mesa y los sockets los reciben igual.
/// </summary>
public class PreparacionQuirurgica : MonoBehaviour
{
    [Serializable]
    public class Puerto
    {
        public string nombre = "Puerto";
        public XRGrabInteractable trocar;
        public XRSocketInteractor socket;

        // Pose del trocar en la mesa al cargar la escena (antes de encajarlo en el socket)
        [NonSerialized] public Vector3 posMesa;
        [NonSerialized] public Quaternion rotMesa;
        [NonSerialized] public bool mesaValida;
    }

    public bool colocarAlIniciar = true;
    public List<Puerto> puertos = new List<Puerto>();

    void Reset()
    {
        puertos = new List<Puerto>();
        Agregar("Lateral izquierdo (brazo derecho: tijera / hook)", "Trocar", "Punto_Trocar_1");
        Agregar("Axilar anterior derecho (brazo izquierdo: grasper)", "Trocar (1)", "Punto_Trocar_2");
        Agregar("Umbilical (laparoscopio)", "Trocar (2)", "Punto_Trocar_3");
    }

    void Agregar(string nombre, string trocar, string socket)
    {
        var t = GameObject.Find(trocar);
        var s = GameObject.Find(socket);
        puertos.Add(new Puerto
        {
            nombre = nombre,
            trocar = t != null ? t.GetComponent<XRGrabInteractable>() : null,
            socket = s != null ? s.GetComponent<XRSocketInteractor>() : null
        });
    }

    void Awake()
    {
        foreach (var p in puertos)
        {
            if (p.trocar == null) continue;
            p.posMesa = p.trocar.transform.position;
            p.rotMesa = p.trocar.transform.rotation;
            p.mesaValida = true;
            // Pivote sobre el fulcro y retirada del obturador una vez colocado
            if (p.trocar.GetComponent<TrocarQuirurgico>() == null) p.trocar.gameObject.AddComponent<TrocarQuirurgico>();
        }
    }

    IEnumerator Start()
    {
        if (!colocarAlIniciar) yield break;
        yield return null; // el XRInteractionManager registra los interactores en el primer frame
        Colocar();
    }

    /// <summary>Encaja en su socket cada trocar que aún no esté colocado.</summary>
    public void Colocar()
    {
        foreach (var p in puertos)
        {
            if (p.trocar == null || p.socket == null) continue;
            if (p.socket.hasSelection) continue;
            var manager = p.socket.interactionManager != null ? p.socket.interactionManager : FindFirstObjectByType<XRInteractionManager>();
            if (manager == null) { Debug.LogWarning("PreparacionQuirurgica: no hay XRInteractionManager."); return; }
            if (p.trocar.isSelected) manager.CancelInteractableSelection((IXRSelectInteractable)p.trocar);
            manager.SelectEnter((IXRSelectInteractor)p.socket, (IXRSelectInteractable)p.trocar);
        }
    }

    Puerto PuertoValido(int indice)
    {
        if (indice < 0 || indice >= puertos.Count) return null;
        var p = puertos[indice];
        return p.trocar != null && p.socket != null ? p : null;
    }

    static XRInteractionManager Manager(Puerto p) =>
        p.socket.interactionManager != null ? p.socket.interactionManager : FindFirstObjectByType<XRInteractionManager>();

    /// <summary>¿Está el trocar de este puerto encajado en su socket?</summary>
    public bool EstaColocado(int indice)
    {
        var p = PuertoValido(indice);
        return p != null && p.socket.hasSelection && p.socket.IsSelecting((IXRSelectInteractable)p.trocar);
    }

    /// <summary>Encaja el trocar de un puerto en su socket (reactiva el socket si estaba desactivado).</summary>
    public void ColocarUno(int indice)
    {
        var p = PuertoValido(indice);
        if (p == null) return;
        p.socket.socketActive = true;
        if (EstaColocado(indice)) return;
        var manager = Manager(p);
        if (manager == null) return;
        if (p.trocar.isSelected) manager.CancelInteractableSelection((IXRSelectInteractable)p.trocar);
        manager.SelectEnter((IXRSelectInteractor)p.socket, (IXRSelectInteractable)p.trocar);
    }

    /// <summary>
    /// Saca el trocar de su socket. Con <paramref name="desactivarSocket"/> el socket deja de atrapar
    /// trocares por cercanía hasta el próximo <see cref="ColocarUno"/> (lo usa la demo, que lleva el trocar
    /// con el brazo hasta el puerto y no quiere que salte antes de tiempo).
    /// </summary>
    public void Liberar(int indice, bool desactivarSocket)
    {
        var p = PuertoValido(indice);
        if (p == null) return;
        if (EstaColocado(indice))
        {
            var manager = Manager(p);
            if (manager != null) manager.SelectExit((IXRSelectInteractor)p.socket, (IXRSelectInteractable)p.trocar);
        }
        if (desactivarSocket) p.socket.socketActive = false;
    }

    /// <summary>
    /// Los tres trocares son iguales: si en el socket de este puerto se encajó el trocar asignado a otro
    /// puerto, se intercambian las asignaciones. Devuelve el índice del otro puerto, o -1 si no hubo cambio.
    /// </summary>
    public int AdoptarTrocarEncajado(int indice)
    {
        var p = PuertoValido(indice);
        if (p == null || !p.socket.hasSelection) return -1;
        var encajado = p.socket.firstInteractableSelected as XRGrabInteractable;
        if (encajado == null || encajado == p.trocar) return -1;
        for (int j = 0; j < puertos.Count; j++)
        {
            if (j == indice || puertos[j].trocar != encajado) continue;
            IntercambiarTrocares(indice, j);
            return j;
        }
        return -1;
    }

    /// <summary>Intercambia qué trocar corresponde a cada uno de dos puertos (los tres trocares son iguales).</summary>
    public void IntercambiarTrocares(int i, int j)
    {
        if (i == j || i < 0 || j < 0 || i >= puertos.Count || j >= puertos.Count) return;
        Puerto a = puertos[i], b = puertos[j];
        (a.trocar, b.trocar) = (b.trocar, a.trocar);
        (a.posMesa, b.posMesa) = (b.posMesa, a.posMesa);
        (a.rotMesa, b.rotMesa) = (b.rotMesa, a.rotMesa);
    }

    /// <summary>Si alguien tiene agarrado con la mano el trocar de este puerto, lo suelta.</summary>
    public void SoltarDeLaMano(int indice)
    {
        var p = PuertoValido(indice);
        if (p == null || !p.trocar.isSelected || EstaColocado(indice)) return;
        var manager = Manager(p);
        if (manager != null) manager.CancelInteractableSelection((IXRSelectInteractable)p.trocar);
    }

    /// <summary>¿Están todos los trocares en sus sockets?</summary>
    public bool TodosColocados()
    {
        foreach (var p in puertos)
            if (p.trocar != null && p.socket != null && !(p.socket.hasSelection && p.socket.IsSelecting((IXRSelectInteractable)p.trocar))) return false;
        return true;
    }

    /// <summary>Punto de fulcro (PuntoEntrada) del trocar de un puerto.</summary>
    public Transform Fulcro(int indice)
    {
        if (indice < 0 || indice >= puertos.Count || puertos[indice].trocar == null) return null;
        return puertos[indice].trocar.transform.Find("PuntoEntrada");
    }
}
