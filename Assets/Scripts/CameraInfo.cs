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
    /*
    [SerializeField]
    OVRPlugin.Controller.LTouch ltouch;
    [SerializeField]
    OVRPlugin.Controller.RTouch rtouch;
    */
    [SerializeField]
    OVRHand ovrhandR;
    [SerializeField]
    OVRHand ovrhandL;
    [SerializeField]
    InferenceEngine inferenceEngine;
    [SerializeField]
    PipelineCartel ocrEngine;
    [SerializeField]
    GameObject avatar3D;
    AnimatorManager animManager;

    [SerializeField]
    float proccesingInterval = 0.15f;

    [SerializeField]
    string lastPrediction = "";
    bool isPinchingR = false;
    bool isPinchingL = false;
    bool runCNN = true;
    

    void Start()
    {
        animManager = avatar3D.GetComponent<AnimatorManager>();
        cameraAccess = GetComponent<PassthroughCameraAccess>();
        animManager.MoveTo(camTransform);
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
                isPinchingL = (ovrhandL.GetFingerIsPinching(OVRHand.HandFinger.Index) && ovrhandL.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High) && !ovrhandL.IsSystemGestureInProgress; 
                isPinchingR = (ovrhandR.GetFingerIsPinching(OVRHand.HandFinger.Index) && ovrhandR.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High) && !ovrhandR.IsSystemGestureInProgress;
            }
            
            if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger, OVRInput.GetActiveController()) || isPinchingR)
            {
                Debug.Log(OVRInput.GetActiveController().ToString());
                if (texture != null && !animManager.GetTriggerAnim())
                {
                    lastPrediction = inferenceEngine.ProcesarImagen(texture);
                    //lastPrediction = ocrEngine.ProcesarFrame(texture);
                    animManager.MoveTo(camTransform);
                    if (lastPrediction != "")
                    {
                        animManager.ChangeText(lastPrediction);
                        //inferenceEngine.DisposeWorker(); //<- we get rid of the worker to see if that frees some GPU memory
                        //avatar3D.SetActive(true);
                        animManager.Interpret(lastPrediction);
                    }
                    else
                    {
                       animManager.ChangeText("Ninguna señal detectada...");
                    }
                    
                }
                
            }
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.GetActiveController()) || isPinchingL)
            {
                animManager.MoveTo(camTransform);
                if (animManager.GetTriggerAnim())
                {
                    
                    animManager.CancelInterpretation();
                }
                
            }

        }
    }

    
}
