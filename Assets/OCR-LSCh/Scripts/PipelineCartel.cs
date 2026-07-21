// Orquestador del flujo completo on-device para Meta Quest 3:
//   frame de camara -> preprocesamiento -> OCR -> PLN -> glosa LSCh.
//
// Sin dependencias de pago: el preprocesamiento es C# puro (OcrLsch.Vision)
// y el OCR usa Unity Inference Engine (com.unity.ai.inference, el antiguo
// Sentis).
//
// PUNTO DE ENTRADA: ProcesarFrame(Texture). Recibe el frame crudo de la
// Passthrough Camera API y devuelve el string listo para AnimatorManager.
// Es un reemplazo directo de InferenceEngine.ProcesarImagen(Texture): misma
// firma, mismo contrato ("" cuando no se reconocio nada).

using OcrLsch.Vision;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Events;

public class PipelineCartel : MonoBehaviour
{
    [Header("Modelos (unity/Modelos)")]
    public ModelAsset modeloCraft;
    public ModelAsset modeloCrnn;
    public TextAsset charsetCtc;

    [Header("Datos (unity/Datos)")]
    public TextAsset lexicoEs;

    [Header("Backend de inferencia")]
    public BackendType backend = BackendType.GPUCompute;

    [Header("Camara")]
    [Tooltip("Invierte el frame en Y antes de procesarlo. Segun la API grafica el " +
             "Blit puede entregar la textura al reves, y con el texto de cabeza el " +
             "OCR no reconoce nada. Si un cartel bien encuadrado devuelve vacio o " +
             "basura, esto es lo primero que hay que probar.")]
    public bool voltearVertical = false;

    [Header("Resultado del ultimo procesamiento")]
    public string TextoOcr;
    public string TextoLsch;

    public UnityEvent<string> AlObtenerGlosa;

    MotorOcr _ocr;
    TransformadorLSCh _pln;
    readonly TexturaLegible _lector = new TexturaLegible();

    // Carga perezosa: los dos ONNX pesan ~95 MB juntos, asi que no se suben a
    // memoria hasta que se use realmente el modo OCR (el usuario puede pasar
    // toda la sesion en modo CNN).
    void Asegurar()
    {
        _ocr ??= new MotorOcr(modeloCraft, modeloCrnn, charsetCtc, backend);
        _pln ??= new TransformadorLSCh(lexicoEs);
    }

    void OnDestroy()
    {
        _ocr?.Dispose();
        _lector.Liberar();
    }

    /// <summary>
    /// FUNCION PRINCIPAL del modo OCR. Recibe el frame crudo de la camara
    /// passthrough -- lo que devuelve PassthroughCameraAccess.GetTexture() --
    /// lo pasa por preprocesamiento, OCR y PLN, y devuelve el string que se
    /// le entrega a AnimatorManager.Interpret() para que el avatar lo deletree.
    ///
    /// Devuelve "" si no se reconocio texto, igual que el camino CNN, asi que
    /// se usa exactamente igual:
    ///
    ///     string resultado = pipelineOcr.ProcesarFrame(texture);
    ///     if (resultado != "") { tmp.text = resultado;
    ///                            avatar3D.SetActive(true);
    ///                            animManager.Interpret(resultado); }
    ///
    /// Cuidado: bloquea el hilo principal varios cientos de ms (preprocesamiento
    /// en CPU + CRAFT + un CRNN por linea de texto). Conviene llamarlo desde una
    /// corrutina y ceder un frame antes, para alcanzar a mostrar un aviso de
    /// "Procesando..." en vez de que la imagen se congele sin explicacion.
    ///
    /// El texto crudo del OCR, antes del PLN, queda en TextoOcr (util para
    /// depurar: distingue "el OCR no leyo nada" de "el PLN descarto todo").
    /// </summary>
    public string ProcesarFrame(Texture frame)
    {
        // El CNN sube la textura directo a la GPU, pero el preprocesamiento de
        // aqui (contornos, homografia, CLAHE) corre en CPU y necesita los
        // pixeles: de ahi el paso por TexturaLegible.
        Texture2D legible = _lector.Obtener(frame, voltearVertical);
        if (legible == null)
        {
            TextoOcr = "";
            TextoLsch = "";
            return "";
        }
        return ProcesarTextura(legible);
    }

    /// <summary>Procesa un frame ya convertido a Texture2D. Usar
    /// ProcesarFrame() si lo que se tiene es la textura de la camara.</summary>
    public string ProcesarTextura(Texture2D frame)
    {
        ImagenGris cartel = PuenteImagen.PrepararDesdeTextura(frame);
        return ProcesarCartel(cartel);
    }

    /// <summary>Procesa una imagen ya cargada como ImagenColor (para pruebas
    /// en Editor con las fixtures de unity/Fixtures).</summary>
    public string ProcesarImagen(ImagenColor bgr)
    {
        ImagenGris cartel = PreprocesadorCartel.PrepararCartel(bgr);
        return ProcesarCartel(cartel);
    }

    string ProcesarCartel(ImagenGris cartel)
    {
        Asegurar();
        TextoOcr = _ocr.Leer(cartel);
        TextoLsch = string.IsNullOrWhiteSpace(TextoOcr) ? "" : _pln.Transformar(TextoOcr);
        AlObtenerGlosa?.Invoke(TextoLsch);
        return TextoLsch;
    }
}
