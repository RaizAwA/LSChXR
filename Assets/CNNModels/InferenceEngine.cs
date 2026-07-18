using UnityEngine;


public class InferenceEngine : MonoBehaviour
{
    [Header("Configuración del Modelo")]
    public Unity.InferenceEngine.ModelAsset modelAsset; 
    [Range(0f, 1f)]
    public float confidenceThreshold = 0.60f; 

    [Header("Componentes")]
    public PostProcessingUnit postProcessor; 

    [Header("Prueba Local")]
    public Texture2D imagenDePrueba; 

    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.Worker worker; 

    void Start()
    {
        InicializarIA();

        if (postProcessor == null) 
            postProcessor = GetComponent<PostProcessingUnit>();

        if (imagenDePrueba != null)
        {
            ProcesarImagen(imagenDePrueba);
        }
    }

    private void InicializarIA()
    {
        if (worker == null && modelAsset != null)
        {
            runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
        }
    }

    public string ProcesarImagen(Texture camaraTexture)
    {
        if (camaraTexture == null || camaraTexture.width <= 16 || camaraTexture.height <= 16)
        {
            return ""; 
        }
        if (worker == null)
        {
            InicializarIA();
            if (worker == null) return "";
        }

        Unity.InferenceEngine.TextureTransform transform = new Unity.InferenceEngine.TextureTransform()
            .SetDimensions(640, 640, 3);

        using Unity.InferenceEngine.Tensor<float> inputTensor = Unity.InferenceEngine.TextureConverter.ToTensor(camaraTexture, transform); 
        worker.Schedule(inputTensor); 
        Unity.InferenceEngine.Tensor<float> outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>; 

        if (postProcessor != null)
        {
            string resultado = postProcessor.ParsearSalidaYOLO(outputTensor, confidenceThreshold);
            
            if (resultado != null)
            {
                Debug.Log($"<color=green><b>[Éxito]</b></color> ¡Señal identificada con éxito: {resultado}!");
                return resultado;
            }
            else
            {
                Debug.LogWarning("[Info] No se detectó ninguna señal que supere el " + (confidenceThreshold * 100) + "% de confianza.");
                return "";
            }
        }
        else
        {
            Debug.LogWarning("PostPreporcesor no asignado");
            return "";
        }
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }
}