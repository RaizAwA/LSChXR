// Medicion de latencias de extremo a extremo de HandSign 3D (para el paper).
//
// Cada escaneo (gesto de captura) genera una fila en un CSV guardado en el
// visor: Application.persistentDataPath/latencias.csv
// En Meta Quest: Android/data/<paquete de la app>/files/latencias.csv
// (visible desde el PC al conectar el visor por USB y permitir el acceso).
//
// Las etapas se miden con Stopwatch (alta resolucion) en el hilo principal.
// Limitaciones conocidas: no incluye la latencia del sensor de la camara
// (desde la luz hasta que la textura passthrough esta disponible), y el inicio
// de la animacion es el instante en que se ordena la primera sena (CrossFade),
// no el instante en que el fotograma se muestra en pantalla.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class Latencias
{
    /// <summary>Activa o desactiva toda la medicion.</summary>
    public static bool Activo = true;
    /// <summary>Muestra un resumen de la medicion en el panel de texto del avatar.</summary>
    public static bool MostrarEnPantalla = true;

    const string NombreArchivo = "latencias.csv";

    // Etapas en el orden en que se escriben en el CSV (ms).
    public static readonly string[] Etapas =
    {
        "lectura_textura",      // OCR: copia GPU -> CPU del fotograma
        "preprocesamiento",     // CNN: textura -> tensor 640x640 | OCR: localizar y corregir cartel
        "inferencia_cnn",       // CNN: ejecucion del modelo + espera/lectura de la GPU
        "postproceso_cnn",      // CNN: seleccion de la clase de mayor confianza
        "carga_modelos_ocr",    // OCR: carga perezosa de los modelos (solo la primera vez)
        "ocr_deteccion",        // OCR: detector DB de PP-OCRv4
        "ocr_reconocimiento",   // OCR: reconocedor CRNN/CTC (todas las lineas)
        "ocr_filtrado",         // OCR: filtrado y orden de detecciones
        "pln",                  // OCR: reordenamiento a glosa (TransformadorLSCh)
    };

    public class Medicion
    {
        public int Id;
        public string Modo;
        public bool PrimeraEjecucion;
        public double TGesto;
        public double TTextoListo = -1;
        public double TInicioAnimacion = -1;
        public double TFinAnimacion = -1;
        public int NAnimaciones;
        public bool Cancelada;
        public string Resultado = "";
        public readonly Dictionary<string, double> Valores = new Dictionary<string, double>();
        public bool Guardada;
    }

    static readonly Stopwatch Reloj = Stopwatch.StartNew();
    static readonly HashSet<string> ModosEjecutados = new HashSet<string>();
    static int _contador;

    /// <summary>Medicion en curso (la que reciben las etapas).</summary>
    public static Medicion Actual { get; private set; }

    public static double AhoraMs { get { return Reloj.Elapsed.TotalMilliseconds; } }

    public static string RutaArchivo
    {
        get { return Path.Combine(Application.persistentDataPath, NombreArchivo); }
    }

    /// <summary>Inicia una medicion en el instante del gesto de captura.</summary>
    public static Medicion Iniciar(string modo)
    {
        if (!Activo) { Actual = null; return null; }
        var m = new Medicion
        {
            Id = ++_contador,
            Modo = modo,
            PrimeraEjecucion = ModosEjecutados.Add(modo),
            TGesto = AhoraMs,
        };
        Actual = m;
        return m;
    }

    /// <summary>Suma <paramref name="ms"/> a la etapa indicada de la medicion en curso.</summary>
    public static void Etapa(string nombre, double ms)
    {
        Medicion m = Actual;
        if (m == null) return;
        double previo;
        m.Valores[nombre] = (m.Valores.TryGetValue(nombre, out previo) ? previo : 0) + ms;
    }

    public static void MarcarPrimeraEjecucion()
    {
        if (Actual != null) Actual.PrimeraEjecucion = true;
    }

    /// <summary>Texto breve para mostrar en el visor.</summary>
    public static string Resumen(Medicion m)
    {
        if (m == null) return "";
        var sb = new StringBuilder();
        sb.Append('#').Append(m.Id).Append(' ').Append(m.Modo);
        if (m.TTextoListo >= 0)
            sb.Append(" | reconocimiento ").Append(F(m.TTextoListo - m.TGesto)).Append(" ms");
        if (m.TFinAnimacion >= 0)
            sb.Append(" | total ").Append(F(m.TFinAnimacion - m.TGesto)).Append(" ms");
        if (m.PrimeraEjecucion) sb.Append(" (1a ejec.)");
        return sb.ToString();
    }

    /// <summary>Escribe la medicion como una fila del CSV (una sola vez).</summary>
    public static void Guardar(Medicion m)
    {
        if (m == null || m.Guardada) return;
        m.Guardada = true;
        if (Actual == m) Actual = null;
        try
        {
            string ruta = RutaArchivo;
            bool nuevo = !File.Exists(ruta);
            var sb = new StringBuilder();
            if (nuevo)
            {
                sb.Append("id,fecha_hora,modo,primera_ejecucion");
                foreach (string e in Etapas) sb.Append(',').Append(e).Append("_ms");
                sb.Append(",reconocimiento_total_ms,hasta_inicio_animacion_ms,animacion_ms,total_ms,n_animaciones,cancelada,resultado\n");
            }
            sb.Append(m.Id).Append(',')
              .Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append(m.Modo).Append(',')
              .Append(m.PrimeraEjecucion ? 1 : 0);
            foreach (string e in Etapas)
            {
                double v;
                sb.Append(',').Append(m.Valores.TryGetValue(e, out v) ? F(v) : "");
            }
            sb.Append(',').Append(m.TTextoListo >= 0 ? F(m.TTextoListo - m.TGesto) : "");
            sb.Append(',').Append(m.TInicioAnimacion >= 0 ? F(m.TInicioAnimacion - m.TGesto) : "");
            sb.Append(',').Append(m.TInicioAnimacion >= 0 && m.TFinAnimacion >= 0 ? F(m.TFinAnimacion - m.TInicioAnimacion) : "");
            double fin = m.TFinAnimacion >= 0 ? m.TFinAnimacion : m.TTextoListo;
            sb.Append(',').Append(fin >= 0 ? F(fin - m.TGesto) : "");
            sb.Append(',').Append(m.NAnimaciones);
            sb.Append(',').Append(m.Cancelada ? 1 : 0);
            sb.Append(',').Append('"').Append((m.Resultado ?? "").Replace("\"", "'").Replace("\n", " ")).Append('"');
            sb.Append('\n');
            File.AppendAllText(ruta, sb.ToString(), Encoding.UTF8);
            Debug.Log("[Latencia] " + Resumen(m) + " -> " + ruta);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Latencia] No se pudo guardar la medicion: " + ex.Message);
        }
    }

    static string F(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
}
