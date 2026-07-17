using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class EfectoAparicion : MonoBehaviour
{
    private Image imagenUI;

    void Awake()
    {
        imagenUI = GetComponent<Image>();
        if (imagenUI != null)
        {
            // Nos aseguramos al 100% por código de que empiece totalmente invisible
            Color c = imagenUI.color;
            c.a = 0f;
            imagenUI.color = c;
        }
        
        // Desactivamos el objeto al arrancar para que no estorbe ni tape nada
        gameObject.SetActive(false);
    }

    // Esta función la llamaremos desde el script SaltarEscena
    public IEnumerator Aparecer(float tiempoAparicion)
    {
        if (imagenUI == null) yield break;

        // Lo encendemos justo cuando se presione la pantalla
        gameObject.SetActive(true);

        float t = 0f;
        Color c = imagenUI.color;

        while (t < tiempoAparicion)
        {
            t += Time.deltaTime;
            // Va subiendo el Alpha de 0 a 1 de forma lineal y suave
            c.a = Mathf.Lerp(0f, 1f, t / tiempoAparicion);
            imagenUI.color = c;
            yield return null;
        }

        // Forzamos a que quede totalmente opaca al final
        c.a = 1f;
        imagenUI.color = c;
    }
}