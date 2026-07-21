// Puente entre Unity y la librería de imagen en C# PURO (OcrLsch.Vision).
//
// Ya NO depende de OpenCV for Unity: toda la corrección de imagen vive en
// ProcesamientoImagen.cs + Ops.cs (sin dependencias externas). Este archivo
// solo convierte entre las texturas de Unity y las estructuras de la librería.

using OcrLsch.Vision;
using UnityEngine;

public static class PuenteImagen
{
    /// <summary>Convierte una Texture2D (RGBA32) a ImagenColor BGR.</summary>
    public static ImagenColor DesdeTextura(Texture2D tex)
    {
        Color32[] px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        var img = new ImagenColor(w, h);
        // Unity entrega las filas de abajo hacia arriba; se invierte en Y
        // para que la imagen quede con la orientación natural.
        for (int y = 0; y < h; y++)
        {
            int filaTex = (h - 1 - y) * w;
            int filaImg = y * w;
            for (int x = 0; x < w; x++)
            {
                Color32 c = px[filaTex + x];
                int i = (filaImg + x) * 3;
                img.Datos[i] = c.b;
                img.Datos[i + 1] = c.g;
                img.Datos[i + 2] = c.r;
            }
        }
        return img;
    }

    /// <summary>Corrige el cartel y devuelve la imagen en gris lista para OCR.</summary>
    public static ImagenGris PrepararDesdeTextura(Texture2D tex)
    {
        return PreprocesadorCartel.PrepararCartel(DesdeTextura(tex));
    }

    /// <summary>Convierte una ImagenGris a Texture2D (para depurar en el Editor).</summary>
    public static Texture2D AGrisTextura(ImagenGris g)
    {
        var tex = new Texture2D(g.Ancho, g.Alto, TextureFormat.RGBA32, false);
        var px = new Color32[g.Ancho * g.Alto];
        for (int y = 0; y < g.Alto; y++)
        {
            int filaG = y * g.Ancho;
            int filaTex = (g.Alto - 1 - y) * g.Ancho;
            for (int x = 0; x < g.Ancho; x++)
            {
                byte v = g.Datos[filaG + x];
                px[filaTex + x] = new Color32(v, v, v, 255);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}
