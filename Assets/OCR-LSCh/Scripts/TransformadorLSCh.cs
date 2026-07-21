// Transformacion PLN -> glosas LSCh (orden TSOV) en C# para Unity/Quest 3.
//
// Reemplaza la version Python basada en spaCy con un motor de reglas mas un
// lexico generado desde spaCy (Datos/lexico_es.json: palabra -> lema + POS).
// Es una aproximacion valida para el dominio de carteles (frases cortas e
// imperativas): no hay analisis de dependencias, asi que sujeto y objeto se
// conservan en su orden de aparicion, entre el Tiempo (al frente) y el
// Verbo (al final), con la negacion despues del verbo.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TransformadorLSCh
{
    [System.Serializable]
    class EntradaLexico { public string lema; public string pos; }

    readonly Dictionary<string, EntradaLexico> _lexico;

    static readonly HashSet<string> LexicoTemporal = new()
    {
        "hoy", "ayer", "mañana", "ahora", "luego", "después", "antes",
        "siempre", "nunca", "pronto", "temprano", "tarde", "noche", "día",
        "semana", "mes", "año", "momento", "emergencia", "sismo", "terremoto",
        "incendio", "caso",
    };

    // Palabras funcionales que la LSCh no sena (articulos, preposiciones,
    // conjunciones, copulas y clíticos).
    static readonly HashSet<string> PalabrasOmitidas = new()
    {
        "el", "la", "los", "las", "un", "una", "unos", "unas", "lo",
        "de", "del", "a", "al", "en", "por", "para", "con", "sin", "sobre",
        "y", "e", "o", "u", "que", "se", "su", "sus",
        "es", "son", "está", "están", "ser", "estar", "hay", "haber",
    };

    // Modales que si aportan significado y se conservan antes del verbo.
    static readonly HashSet<string> Modales = new()
    { "deber", "poder", "querer", "necesitar", "tener" };

    public TransformadorLSCh(TextAsset lexicoJson)
    {
        _lexico = new Dictionary<string, EntradaLexico>();
        // JsonUtility no soporta diccionarios: parseo minimo del JSON plano
        // { "palabra": {"lema": "...", "pos": "..."}, ... }
        foreach (var (clave, lema, pos) in ParsearLexico(lexicoJson.text))
            _lexico[clave] = new EntradaLexico { lema = lema, pos = pos };
    }

    static IEnumerable<(string, string, string)> ParsearLexico(string json)
    {
        var regex = new System.Text.RegularExpressions.Regex(
            "\"([^\"]+)\"\\s*:\\s*\\{\\s*\"lema\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"pos\"\\s*:\\s*\"([^\"]+)\"\\s*\\}");
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(json))
            yield return (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
    }

    /// <summary>Transforma texto en espanol a glosas TSOV. Oraciones de
    /// entrada separadas por "."; bloques de salida separados por " / ".</summary>
    public string Transformar(string texto)
    {
        texto = Normalizar(texto);
        var oraciones = texto.Split('.')
            .Select(TransformarOracion)
            .Where(o => o.Length > 0);
        return string.Join(" / ", oraciones);
    }

    static string Normalizar(string texto)
    {
        var letras = texto.Where(char.IsLetter).ToList();
        if (letras.Count > 0 && letras.Count(char.IsUpper) / (float)letras.Count > 0.8f)
            texto = texto.ToLowerInvariant();
        return texto;
    }

    (string lema, string pos) Consultar(string palabra)
    {
        if (_lexico.TryGetValue(palabra, out var e)) return (e.lema, e.pos);
        // Palabra fuera del lexico: heuristica minima. Los imperativos en
        // -e/-a de verbos comunes ya estan en el lexico; lo desconocido se
        // trata como sustantivo y se glosa tal cual.
        return (palabra, "NOUN");
    }

    string TransformarOracion(string oracion)
    {
        var tokens = oracion
            .Split(new[] { ' ', ',', ';', ':', '!', '¡', '¿', '?', '"', '(', ')' },
                   System.StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && t.Any(char.IsLetterOrDigit))
            .ToList();
        if (tokens.Count == 0) return "";

        var tiempo = new List<string>();
        var medio = new List<string>();   // sujeto y objeto en orden original
        var verbos = new List<string>();
        bool negacion = false;

        foreach (var token in tokens)
        {
            string palabra = token.ToLowerInvariant();
            if (palabra == "no") { negacion = true; continue; }
            if (PalabrasOmitidas.Contains(palabra)) continue;

            var (lema, pos) = Consultar(palabra);
            if (PalabrasOmitidas.Contains(lema)) continue;

            string glosa = (pos is "VERB" or "AUX" or "NOUN" or "ADJ" ? lema : palabra)
                .ToUpperInvariant();

            if (LexicoTemporal.Contains(palabra) || LexicoTemporal.Contains(lema))
                tiempo.Add(glosa);
            else if (pos == "VERB")
                verbos.Add(glosa);
            else if (pos == "AUX" && Modales.Contains(lema))
                verbos.Insert(0, glosa); // el modal precede al verbo principal
            else if (pos == "AUX")
                continue; // auxiliar sin carga semantica
            else
                medio.Add(glosa);
        }

        var glosas = tiempo.Concat(medio).Concat(verbos).ToList();
        if (negacion) glosas.Add("NO");
        return string.Join(" ", glosas);
    }
}
