using UnityEngine;


public class PostProcessingUnit : MonoBehaviour
{
    [Header("Nombres de tus 14 Clases (En orden de Colab)")]
    public string[] classNames = new string[14]; 

    public string ParsearSalidaYOLO(Unity.InferenceEngine.Tensor<float> outputTensor, float threshold)
    {
        if (outputTensor == null) return null;
        double t0 = Latencias.AhoraMs;
        float[] data = outputTensor.DownloadToArray(); // espera a que la GPU termine la inferencia
        double t1 = Latencias.AhoraMs;
        Latencias.Etapa("inferencia_cnn", t1 - t0);

        int numRows = 18;
        int numAnchors = 8400;
        int mejorClaseId = -1;
        float mejorConfianza = 0f;

        for (int c = 0; c < numAnchors; c++)
        {
            float confianzaMaximaClase = 0f;
            int claseGanadoraParaEstaCaja = -1;


            for (int r = 4; r < numRows; r++)
            {
                float probabilidad = data[r * numAnchors + c];

                if (probabilidad > confianzaMaximaClase)
                {
                    confianzaMaximaClase = probabilidad;
                    claseGanadoraParaEstaCaja = r - 4;
                }
            }

            if (confianzaMaximaClase > threshold && confianzaMaximaClase > mejorConfianza)
            {
                mejorConfianza = confianzaMaximaClase;
                mejorClaseId = claseGanadoraParaEstaCaja;
            }
        }

        Latencias.Etapa("postproceso_cnn", Latencias.AhoraMs - t1);

        if (mejorClaseId != -1 && mejorClaseId < classNames.Length)
        {
            string señalDetectada = classNames[mejorClaseId];
            Debug.Log($"<color=cyan><b>[Detección]</b></color> {señalDetectada} ({mejorConfianza * 100f:F1}% de certeza)");
            return señalDetectada;
        }

        return null;
    }
}