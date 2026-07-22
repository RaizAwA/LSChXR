using UnityEngine;
using System.Collections;
using Meta.XR;
using TMPro;

public class CameraInfo : MonoBehaviour
{

    PassthroughCameraAccess cameraAccess;
    Texture texture;

    [SerializeField]
    Transform camTransform;
    [SerializeField]
    OVRHand ovrhandR;
    [SerializeField]
    OVRHand ovrhandL;
    [SerializeField]
    InferenceEngine inferenceEngine;
    [SerializeField]
    GameObject avatar3D;
    AnimatorManager animManager;
    [SerializeField]
    TextMeshPro tmp;

    [SerializeField]
    float proccesingInterval = 0.15f;

    [SerializeField]
    string lastPrediction = "";
    bool isPinching = false;
    bool runCNN = true;
    

    void Start()
    {
        animManager = avatar3D.GetComponent<AnimatorManager>();
        cameraAccess = GetComponent<PassthroughCameraAccess>();
    }

    // Update is called once per frame
    void Update()
    {
        
        if (cameraAccess.enabled)
        {
            texture = cameraAccess.GetTexture(); //1280 x 960 default
        }

        if (cameraAccess.IsPlaying)
        {

            if ((ovrhandL != null && ovrhandR != null) && (ovrhandL.IsTracked || ovrhandR.IsTracked))
            {
                isPinching = (ovrhandL.GetFingerIsPinching(OVRHand.HandFinger.Index) && ovrhandL.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High) || 
                             (ovrhandR.GetFingerIsPinching(OVRHand.HandFinger.Index) && ovrhandR.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High);
            }
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.GetActiveController()) || isPinching)
            {
                if (texture != null && !animManager.GetTriggerAnim())
                {
                    lastPrediction = inferenceEngine.ProcesarImagen(texture);
                    if (lastPrediction != "")
                    {
                        tmp.text = lastPrediction;
                        animManager.MoveTo(this.transform);
                        //inferenceEngine.DisposeWorker(); //<- we get rid of the worker to see if that frees some GPU memory
                        //avatar3D.SetActive(true);
                        animManager.Interpret(lastPrediction);
                    }
                    else
                    {
                        tmp.text = "Ninguna señal detectada...";
                    }
                    
                }
                
            }

        }
    }

    
}
