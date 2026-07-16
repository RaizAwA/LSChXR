using UnityEngine;
using Unity.InferenceEngine;


public class SignRecognition : MonoBehaviour
{
    [SerializeField] 
    float threshold = 0.9f;
    public Texture2D test;
    public ModelAsset signRecognition;
    public float[] results;
    Worker worker;
    

    void Start()
    {

        Model model = ModelLoader.Load(signRecognition);

        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(model);
        FunctionalTensor[] outputs = Functional.Forward(model, inputs);
        FunctionalTensor softmax = Functional.Softmax(outputs[0]);

        Model runtimeModel = graph.Compile(softmax);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        Debug.Log(RunCNN(test));
    }

    public int RunCNN(Texture2D picture)
    {
        using Tensor<float> inputTensor = TextureConverter.ToTensor(picture, 640, 640, 3);
        worker.Schedule(inputTensor);
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;

        results = outputTensor.DownloadToArray();
        return GetMaxIndex(results);
    }


    void OnDisable()
    {
        worker.Dispose();
    }

    int GetMaxIndex(float[] array)
    {
        int maxIndex = 0;

        for (int i = 0; i < array.Length; i++)
        {
            if(array[i] > array[maxIndex])
            {
                maxIndex = i;
            }
        }

        if (array[maxIndex] > threshold)
        {
            return maxIndex;
        }
        else
        {
            return -1;
        }
    }
}
