using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class EngineTest
{
    InferenceEngine CrearEngine()
    {
        var go = new GameObject("engine");
        return go.AddComponent<InferenceEngine>();  // Start() corre, pero modelAsset es null → no crea worker
    }

    [UnityTest]
    public IEnumerator ProcesarImagen_TexturaNull_DevuelveVacio()
    {
        var engine = CrearEngine();
        yield return null;                       // deja que Unity ejecute Start()
        Assert.AreEqual("", engine.ProcesarImagen(null));
        Object.Destroy(engine.gameObject);
    }

    [UnityTest]
    public IEnumerator ProcesarImagen_TexturaMuyPequena_DevuelveVacio()
    {
        var engine = CrearEngine();
        yield return null;
        var tex = new Texture2D(8, 8);           // width <= 16 → guarda de la línea 44
        Assert.AreEqual("", engine.ProcesarImagen(tex));
        Object.Destroy(tex); Object.Destroy(engine.gameObject);
    }

    [UnityTest]
    public IEnumerator ProcesarImagen_SinModelo_DevuelveVacio()
    {
        var engine = CrearEngine();              // modelAsset == null → worker nunca se crea
        yield return null;
        var tex = new Texture2D(64, 64);
        Assert.AreEqual("", engine.ProcesarImagen(tex));
        Object.Destroy(tex); Object.Destroy(engine.gameObject);
    }
}