// Puente entre las texturas de Unity y el pipeline OCR.
//
// El pipeline OCR (a diferencia del CNN, que sube la textura directo a la GPU
// con TextureConverter.ToTensor) necesita los pixeles en CPU porque el
// preprocesamiento -- deteccion de contornos, homografia, CLAHE -- corre en
// C# puro. Aqui se convierte cualquier Texture (RenderTexture, WebCamTexture o
// la que entregue PassthroughCameraAccess.GetTexture()) a una Texture2D
// legible, reutilizando siempre el mismo buffer para no generar basura.

using UnityEngine;

public class TexturaLegible
{
    Texture2D _buffer;
    RenderTexture _rt;

    /// <summary>Devuelve una Texture2D legible con el contenido de <paramref name="fuente"/>.
    /// El buffer se reutiliza entre llamadas: copiarlo si hace falta conservarlo.</summary>
    /// <param name="voltearVertical">Invierte la imagen en Y. Segun la API grafica
    /// el Blit puede entregar la textura al reves; si el OCR devuelve basura con
    /// carteles bien encuadrados, probar cambiando este valor.</param>
    public Texture2D Obtener(Texture fuente, bool voltearVertical = false)
    {
        if (fuente == null || fuente.width <= 16 || fuente.height <= 16) return null;

        int w = fuente.width, h = fuente.height;

        if (_buffer == null || _buffer.width != w || _buffer.height != h)
        {
            if (_buffer != null) Object.Destroy(_buffer);
            _buffer = new Texture2D(w, h, TextureFormat.RGBA32, false);
        }

        if (_rt == null || _rt.width != w || _rt.height != h)
        {
            if (_rt != null) _rt.Release();
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        }

        // Blit para salvar el caso comun: la textura de la camara no es
        // legible desde CPU (no tiene Read/Write) y GetPixels32 fallaria.
        if (voltearVertical)
            Graphics.Blit(fuente, _rt, new Vector2(1f, -1f), new Vector2(0f, 1f));
        else
            Graphics.Blit(fuente, _rt);

        RenderTexture anterior = RenderTexture.active;
        RenderTexture.active = _rt;
        _buffer.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        _buffer.Apply(false);
        RenderTexture.active = anterior;

        return _buffer;
    }

    public void Liberar()
    {
        if (_buffer != null) { Object.Destroy(_buffer); _buffer = null; }
        if (_rt != null) { _rt.Release(); _rt = null; }
    }
}
