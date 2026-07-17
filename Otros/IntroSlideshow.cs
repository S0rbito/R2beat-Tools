using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class IntroSlideshow : MonoBehaviour
{
    public Sprite[] imagenes;
    public float tiempoPorImagen = 1f;
    public float tiempoFade = 0.5f;
    public string escenaSiguiente = "Intro_Island";
    public Image imagenUI;
    public Image fadePanel;

    void Start()
    {
        imagenUI.sprite = imagenes[0];
        SetFadeAlpha(1f);
        StartCoroutine(Slideshow());
    }

    void SetFadeAlpha(float a)
    {
        Color c = fadePanel.color;
        c.a = a;
        fadePanel.color = c;
    }

    IEnumerator Slideshow()
    {
        for (int i = 0; i < imagenes.Length; i++)
        {
            imagenUI.sprite = imagenes[i];
            yield return Fade(1f, 0f);
            yield return new WaitForSeconds(tiempoPorImagen);
            yield return Fade(0f, 1f);
        }
        SceneManager.LoadScene(escenaSiguiente);
    }

    IEnumerator Fade(float desde, float hasta)
    {
        float t = 0;
        while (t < tiempoFade)
        {
            t += Time.deltaTime;
            SetFadeAlpha(Mathf.Lerp(desde, hasta, t / tiempoFade));
            yield return null;
        }
        SetFadeAlpha(hasta);
    }
}