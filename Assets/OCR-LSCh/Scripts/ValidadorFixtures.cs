// Valida el port C# contra las salidas de referencia del pipeline Python
// (unity/Fixtures). Ejecutar en el Editor de Unity (Play Mode) antes de
// desplegar al Quest: asignar las texturas originales y el esperado.json.
//
// Las texturas deben tener Read/Write habilitado en el importador.
// La comparacion del texto OCR deberia coincidir en gran medida; la glosa
// LSCh puede diferir levemente (el PLN C# es una aproximacion por reglas
// del analisis de dependencias de spaCy) — revisar las diferencias a mano.

using UnityEngine;

public class ValidadorFixtures : MonoBehaviour
{
    [System.Serializable]
    public class Fixture
    {
        public string nombre;        // p. ej. "1", "3", "10", "12", "19"
        public Texture2D original;   // X_original.jpeg
        public string ocrEsperado;   // esperado.json -> texto_ocr
        public string lschEsperado;  // esperado.json -> texto_lsch
    }

    public PipelineCartel pipeline;
    public Fixture[] fixtures;

    void Start()
    {
        int coincideOcr = 0, coincideLsch = 0;
        foreach (var f in fixtures)
        {
            pipeline.ProcesarTextura(f.original);
            bool okOcr = pipeline.TextoOcr == f.ocrEsperado;
            bool okLsch = pipeline.TextoLsch == f.lschEsperado;
            if (okOcr) coincideOcr++;
            if (okLsch) coincideLsch++;
            Debug.Log(
                $"[Fixture {f.nombre}] OCR {(okOcr ? "OK" : "DIFIERE")}\n" +
                $"  esperado: {f.ocrEsperado}\n  obtenido: {pipeline.TextoOcr}\n" +
                $"LSCh {(okLsch ? "OK" : "DIFIERE")}\n" +
                $"  esperado: {f.lschEsperado}\n  obtenido: {pipeline.TextoLsch}");
        }
        Debug.Log($"[ValidadorFixtures] OCR {coincideOcr}/{fixtures.Length}, " +
                  $"LSCh {coincideLsch}/{fixtures.Length} identicos a la referencia Python.");
    }
}
