// Motor OCR on-device (Unity Inference Engine 2.x) SIN OpenCV.
//
// Reemplaza a EasyOCR con sus modelos exportados a ONNX:
//   - craft_detector.onnx    (deteccion de texto; entrada fija 1x3x704x512)
//   - crnn_reconocedor.onnx  (reconocimiento CTC;  entrada fija 1x1x64x800)
//   - charset_ctc.txt        (alfabeto; indice 0 = blank de CTC)
//
// Toda la manipulacion de imagen usa la libreria pura OcrLsch.Vision
// (ImagenGris + Ops), asi que el proyecto no necesita ningun asset pagado.
// La entrada es el cartel corregido de 500x700 (PuenteImagen / Preprocesador).

using System.Collections.Generic;
using System.Linq;
using OcrLsch.Vision;
using Unity.InferenceEngine;
using UnityEngine;

public class MotorOcr : System.IDisposable
{
    const float ConfianzaAlta = 0.35f;
    const float ConfianzaMinima = 0.10f;
    const float AlturaRelativaMinima = 0.50f;
    const float AlturaRelativaBajaConfianza = 0.60f;

    const float UmbralTexto = 0.4f;
    const float UmbralEnlace = 0.4f;

    const int AnchoDet = 512, AltoDet = 704;
    const int AltoRec = 64, AnchoRec = 800;

    readonly Worker _detector;
    readonly Worker _reconocedor;
    readonly string[] _charset;

    struct CajaPix { public int X, Y, W, H; }

    class Deteccion
    {
        public string Texto;
        public float Confianza;
        public float X, Arriba, Abajo;
        public float Alto => Abajo - Arriba;
    }

    public MotorOcr(ModelAsset craft, ModelAsset crnn, TextAsset charsetCtc,
                    BackendType backend = BackendType.GPUCompute)
    {
        _detector = new Worker(ModelLoader.Load(craft), backend);
        _reconocedor = new Worker(ModelLoader.Load(crnn), backend);
        _charset = charsetCtc.text.Replace("\r", "").Split('\n');
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _reconocedor?.Dispose();
    }

    /// <summary>OCR completo sobre el cartel corregido en gris (500x700).</summary>
    public string Leer(ImagenGris cartel)
    {
        List<CajaPix> cajas = DetectarCajas(cartel);
        var detecciones = new List<Deteccion>();
        foreach (var caja in cajas)
        {
            (string texto, float conf) = ReconocerLinea(cartel, caja);
            if (string.IsNullOrWhiteSpace(texto)) continue;
            detecciones.Add(new Deteccion
            {
                Texto = texto.Trim(),
                Confianza = conf,
                X = caja.X,
                Arriba = caja.Y,
                Abajo = caja.Y + caja.H,
            });
        }
        return FiltrarYOrdenar(detecciones);
    }

    // ---------------- Deteccion (CRAFT) ----------------

    List<CajaPix> DetectarCajas(ImagenGris gris)
    {
        // Lienzo 512x704 con el cartel centrado; el resto en blanco (255).
        int dx = (AnchoDet - gris.Ancho) / 2, dy = (AltoDet - gris.Alto) / 2;
        var lienzo = new byte[AnchoDet * AltoDet];
        for (int i = 0; i < lienzo.Length; i++) lienzo[i] = 255;
        for (int y = 0; y < gris.Alto; y++)
            for (int x = 0; x < gris.Ancho; x++)
                lienzo[(y + dy) * AnchoDet + (x + dx)] = gris.Datos[y * gris.Ancho + x];

        // Normalizacion de CRAFT (RGB replicado, media/desv de ImageNet).
        var datos = new float[3 * AltoDet * AnchoDet];
        float[] media = { 0.485f, 0.456f, 0.406f };
        float[] desv = { 0.229f, 0.224f, 0.225f };
        for (int y = 0; y < AltoDet; y++)
            for (int x = 0; x < AnchoDet; x++)
            {
                float v = lienzo[y * AnchoDet + x] / 255f;
                for (int c = 0; c < 3; c++)
                    datos[c * AltoDet * AnchoDet + y * AnchoDet + x] = (v - media[c]) / desv[c];
            }

        using var entrada = new Tensor<float>(new TensorShape(1, 3, AltoDet, AnchoDet), datos);
        _detector.Schedule(entrada);
        using var salida = (_detector.PeekOutput("mapas") as Tensor<float>).ReadbackAndClone();

        int h2 = AltoDet / 2, w2 = AnchoDet / 2;
        var comb = new byte[w2 * h2];
        for (int y = 0; y < h2; y++)
            for (int x = 0; x < w2; x++)
            {
                float region = salida[0, y, x, 0];
                float enlace = salida[0, y, x, 1];
                comb[y * w2 + x] = (byte)((region >= UmbralTexto || enlace >= UmbralEnlace) ? 255 : 0);
            }

        int n = Ops.EtiquetarStats(comb, w2, h2, out _, out int[] left, out int[] top,
                                   out int[] ancho, out int[] alto, out int[] area);

        var cajas = new List<CajaPix>();
        for (int i = 1; i <= n; i++)
        {
            if (area[i] < 10) continue;
            int bh = alto[i];
            int margen = bh;
            int x0 = 2 * left[i] - margen - dx, y0 = 2 * top[i] - margen / 2 - dy;
            int x1 = 2 * (left[i] + ancho[i]) + margen - dx, y1 = 2 * (top[i] + alto[i]) + margen / 2 - dy;
            x0 = Mathf.Clamp(x0, 0, gris.Ancho - 1);
            y0 = Mathf.Clamp(y0, 0, gris.Alto - 1);
            x1 = Mathf.Clamp(x1, x0 + 1, gris.Ancho);
            y1 = Mathf.Clamp(y1, y0 + 1, gris.Alto);
            if (x1 - x0 < 8 || y1 - y0 < 8) continue;
            cajas.Add(new CajaPix { X = x0, Y = y0, W = x1 - x0, H = y1 - y0 });
        }
        return cajas;
    }

    // ---------------- Reconocimiento (CRNN + CTC greedy) ----------------

    (string, float) ReconocerLinea(ImagenGris gris, CajaPix caja)
    {
        ImagenGris recorte = Ops.RecortarGris(gris, caja.X, caja.Y, caja.W, caja.H);

        int anchoEscalado = Mathf.Clamp(
            (int)System.Math.Round(caja.W * (double)AltoRec / caja.H), 8, AnchoRec);
        ImagenGris escalado = Ops.RedimensionarGris(recorte, anchoEscalado, AltoRec);

        // Lienzo 64x800 relleno con gris neutro (127).
        var datos = new float[AltoRec * AnchoRec];
        for (int y = 0; y < AltoRec; y++)
            for (int x = 0; x < AnchoRec; x++)
            {
                byte v = x < anchoEscalado ? escalado.Datos[y * anchoEscalado + x] : (byte)127;
                datos[y * AnchoRec + x] = (v / 255f - 0.5f) / 0.5f;
            }

        using var entrada = new Tensor<float>(new TensorShape(1, 1, AltoRec, AnchoRec), datos);
        _reconocedor.Schedule(entrada);
        using var logits = (_reconocedor.PeekOutput() as Tensor<float>).ReadbackAndClone();

        int pasos = logits.shape[1], clases = logits.shape[2];
        int pasosValidos = Mathf.Clamp(
            (int)System.Math.Round(pasos * (double)anchoEscalado / AnchoRec), 1, pasos);

        var sb = new System.Text.StringBuilder();
        float sumaConf = 0f; int cuentaConf = 0; int anterior = -1;
        for (int t = 0; t < pasosValidos; t++)
        {
            int mejor = 0; float mejorLogit = float.MinValue;
            for (int c = 0; c < clases; c++)
            {
                float v = logits[0, t, c];
                if (v > mejorLogit) { mejorLogit = v; mejor = c; }
            }
            float sumaExp = 0f;
            for (int c = 0; c < clases; c++) sumaExp += Mathf.Exp(logits[0, t, c] - mejorLogit);
            float prob = 1f / sumaExp;

            if (mejor != 0 && mejor != anterior)
            {
                if (mejor - 1 < _charset.Length) sb.Append(_charset[mejor - 1]);
                sumaConf += prob; cuentaConf++;
            }
            anterior = mejor;
        }
        float confianza = cuentaConf > 0 ? sumaConf / cuentaConf : 0f;
        return (sb.ToString(), confianza);
    }

    // ---------------- Filtrado y ordenado ----------------

    string FiltrarYOrdenar(List<Deteccion> detecciones)
    {
        var cajas = detecciones.Where(d => d.Confianza >= ConfianzaMinima).ToList();
        if (cajas.Count == 0) return "";

        var confiables = cajas.Where(c => c.Confianza >= ConfianzaAlta).ToList();
        float altoReferencia = (confiables.Count > 0 ? confiables : cajas).Max(c => c.Alto);

        cajas = cajas.Where(c =>
            (c.Confianza >= ConfianzaAlta && c.Alto >= AlturaRelativaMinima * altoReferencia) ||
            (c.Confianza < ConfianzaAlta && c.Alto >= AlturaRelativaBajaConfianza * altoReferencia)
        ).ToList();
        if (cajas.Count == 0) return "";

        cajas = cajas.OrderBy(c => c.Arriba).ToList();
        var lineas = new List<(List<Deteccion> cajas, float arriba, float abajo)>();
        foreach (var caja in cajas)
        {
            if (lineas.Count > 0)
            {
                var actual = lineas[lineas.Count - 1];
                float centro = (caja.Arriba + caja.Abajo) / 2f;
                if (actual.arriba <= centro && centro <= actual.abajo)
                {
                    actual.cajas.Add(caja);
                    lineas[lineas.Count - 1] = (actual.cajas,
                        Mathf.Min(actual.arriba, caja.Arriba),
                        Mathf.Max(actual.abajo, caja.Abajo));
                    continue;
                }
            }
            lineas.Add((new List<Deteccion> { caja }, caja.Arriba, caja.Abajo));
        }

        float altoTipico = lineas.Average(l => l.abajo - l.arriba);
        var bloques = new List<List<string>>();
        var bloque = new List<string>();
        float abajoPrevio = float.NaN;
        foreach (var linea in lineas)
        {
            if (!float.IsNaN(abajoPrevio) && linea.arriba - abajoPrevio > 0.9f * altoTipico)
            {
                bloques.Add(bloque);
                bloque = new List<string>();
            }
            bloque.Add(string.Join(" ", linea.cajas.OrderBy(c => c.X).Select(c => c.Texto)));
            abajoPrevio = linea.abajo;
        }
        bloques.Add(bloque);

        return string.Join(". ", bloques
            .Where(b => b.Count > 0)
            .Select(b => string.Join(" ", b).TrimEnd('.')));
    }
}
