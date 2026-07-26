// Motor OCR on-device (Unity Inference Engine 2.x) con PaddleOCR PP-OCRv4, SIN OpenCV.
//
// Modelos (unity/Modelos), oficiales de PaddleOCR (los mismos que usa la
// referencia Python vía rapidocr — sin conversión, misma paridad):
//   - ppocrv4_det.onnx    (detector DB; entrada 1x3x1024x736 para el cartel 500x700)
//   - ppocrv4_rec.onnx    (reconocedor CRNN/CTC; entrada 1x3x48xW)
//   - charset_paddle.txt  (diccionario base; el CTC antepone blank y añade espacio)
//
// El pre/post-procesamiento (normalización, extracción de cajas DB, resize
// del reconocedor y decodificación CTC) está en OcrPaddle.cs (C# puro,
// validado en PC contra PP-OCRv4). Este archivo solo añade la inferencia
// con Inference Engine y la orquestación.

using System.Collections.Generic;
using OcrLsch.Vision;
using Unity.InferenceEngine;
using UnityEngine;

public class MotorOcr : System.IDisposable
{
    // El cartel corregido es 500x700; el detector DB de PaddleOCR reescala el
    // lado menor a 736 (múltiplo de 32) -> entrada fija 736x1024 (ideal para
    // Inference Engine, que rinde mejor con formas estáticas).
    const int DetW = 736, DetH = 1024;
    const int RecAlto = 48;

    readonly Worker _detector;
    readonly Worker _reconocedor;
    readonly string[] _charset;

    public MotorOcr(ModelAsset det, ModelAsset rec, TextAsset charsetBase,
                    BackendType backend = BackendType.GPUCompute)
    {
        _detector = new Worker(ModelLoader.Load(det), backend);
        _reconocedor = new Worker(ModelLoader.Load(rec), backend);
        string[] dictBase = charsetBase.text.Replace("\r", "").Split('\n');
        _charset = OcrPaddle.ConstruirCharset(dictBase);
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _reconocedor?.Dispose();
    }

    /// <summary>OCR completo sobre el cartel corregido en gris (500x700).</summary>
    public string Leer(ImagenGris cartel)
    {
        List<CajaTexto> cajas = DetectarCajas(cartel);
        var detecciones = new List<DeteccionOcr>();
        foreach (var caja in cajas)
        {
            (string texto, float conf) = ReconocerLinea(cartel, caja);
            if (string.IsNullOrWhiteSpace(texto)) continue;
            detecciones.Add(new DeteccionOcr
            {
                Texto = texto.Trim(),
                Confianza = conf,
                X = caja.X,
                Derecha = caja.X + caja.W,
                Arriba = caja.Y,
                Abajo = caja.Y + caja.H,
            });
        }
        return OcrPaddle.FiltrarYOrdenar(detecciones, cartel.Ancho, cartel.Alto);
    }

    // ---------------- Detector DB (PP-OCRv4) ----------------

    List<CajaTexto> DetectarCajas(ImagenGris cartel)
    {
        ImagenGris redim = Ops.RedimensionarGris(cartel, DetW, DetH);
        float[] datos = OcrPaddle.PreprocesarDet(redim); // [3,DetH,DetW]

        using var entrada = new Tensor<float>(new TensorShape(1, 3, DetH, DetW), datos);
        _detector.Schedule(entrada);
        using var salida = (_detector.PeekOutput() as Tensor<float>).ReadbackAndClone();
        // salida DB: [1, 1, DetH, DetW] -> mapa de probabilidad (fila-mayor)
        var mapa = salida.DownloadToArray(); // longitud DetH*DetW

        double escalaX = (double)cartel.Ancho / DetW;
        double escalaY = (double)cartel.Alto / DetH;
        return OcrPaddle.ExtraerCajasDB(mapa, DetW, DetH, escalaX, escalaY,
                                        cartel.Ancho, cartel.Alto);
    }

    // ---------------- Reconocedor CRNN/CTC (PP-OCRv4) ----------------

    (string, float) ReconocerLinea(ImagenGris cartel, CajaTexto caja)
    {
        ImagenGris recorte = Ops.RecortarGris(cartel, caja.X, caja.Y, caja.W, caja.H);
        float[] tensor = OcrPaddle.PreprocesarRec(recorte, out int anchoTensor, out _);

        using var entrada = new Tensor<float>(new TensorShape(1, 3, RecAlto, anchoTensor), tensor);
        _reconocedor.Schedule(entrada);
        using var logits = (_reconocedor.PeekOutput() as Tensor<float>).ReadbackAndClone();
        // logits: [1, T, clases]
        int pasos = logits.shape[1], clases = logits.shape[2];
        float[] probs = logits.DownloadToArray(); // longitud T*clases (fila-mayor)

        return OcrPaddle.DecodificarCTC(probs, pasos, clases, _charset);
    }
}
