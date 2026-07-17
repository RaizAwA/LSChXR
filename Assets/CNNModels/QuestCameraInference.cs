using UnityEngine;
using System.Collections;

public class QuestCameraInference : MonoBehaviour
{
    [Header("Configuración de la IA")]
    public InferenceEngine inferenceEngine;
    
    [Tooltip("Cada cuántos segundos procesar la imagen")]
    public float intervaloProcesamiento = 0.15f;

    private WebCamTexture webcamTexture;
    private bool corriendo = false;

    void Start()
    {
        if (inferenceEngine == null)
            inferenceEngine = GetComponent<InferenceEngine>();

        StartCoroutine(IniciarCamara());
    }

    IEnumerator IniciarCamara()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            yield return new WaitForSeconds(1.0f);
        }
        #endif


        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("[Cámara] No se detectó ninguna cámara en este dispositivo.");
            yield break;
        }

        string camaraElegida = "";

  
        Debug.Log("Buscando camaras disponibles");
        for (int i = 0; i < WebCamTexture.devices.Length; i++)
        {
            string nombreCamara = WebCamTexture.devices[i].name;
            Debug.Log($"Cámara [{i}]: {nombreCamara}");

            if (nombreCamara.ToLower().Contains("iriun"))
            {
                camaraElegida = nombreCamara;
            }
        }

  
        if (!string.IsNullOrEmpty(camaraElegida))
        {
            Debug.Log("Camara Elegida");
        }
        else
        {
            camaraElegida = WebCamTexture.devices[0].name;
            Debug.LogWarning($"Usando camara por defecto: {camaraElegida}");
        }

        webcamTexture = new WebCamTexture(camaraElegida, 640, 640, 30);
        webcamTexture.Play();

        corriendo = true;
        StartCoroutine(BucleInferencia());
    }

    IEnumerator BucleInferencia()
    {
        while (corriendo)
        {
            if (webcamTexture != null && webcamTexture.isPlaying && webcamTexture.didUpdateThisFrame)
            {
                inferenceEngine.ProcesarImagen(webcamTexture);
            }
            yield return new WaitForSeconds(intervaloProcesamiento);
        }
    }

    void OnDestroy()
    {
        corriendo = false;
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
        }
    }
}