using UnityEngine;
using System.Collections;
using Meta.XR;
using TMPro;

public class CameraInfo : MonoBehaviour
{

    PassthroughCameraAccess cameraAccess;
    Texture texture;

    [SerializeField]
    GameObject uiManager;
    Transform uiTransform;
    [SerializeField]
    Transform camTransform;
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
    bool preventPinch = false;
    int indexAlgoritmo = 0;
    

    void Start()
    {
        animManager = avatar3D.GetComponent<AnimatorManager>();
        cameraAccess = GetComponent<PassthroughCameraAccess>();
        uiTransform = GetComponent<Transform>();
        //animManager.MoveTo(camTransform);
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
            
            if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger, OVRInput.GetActiveController()) || isPinchingR && !preventPinch)
            {
                Debug.Log(OVRInput.GetActiveController().ToString());
                if (texture != null && !animManager.GetTriggerAnim())
                {
                    switch (indexAlgoritmo)
                    {
                        case 0:
                            lastPrediction = inferenceEngine.ProcesarImagen(texture);
                            break;
                        case 1:
                            lastPrediction = ocrEngine.ProcesarFrame(texture);
                            break;
                        default:
                            lastPrediction = inferenceEngine.ProcesarImagen(texture);
                            break;
                    }
                    
                    //lastPrediction = inferenceEngine.ProcesarImagen(texture);
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
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.GetActiveController()) || isPinchingL && !preventPinch)
            {
                
                if (animManager.GetTriggerAnim())
                {
                    animManager.MoveTo(camTransform);
                    animManager.CancelInterpretation();
                }
                
            }

            if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.GetActiveController()))
            {
                ToggleUI();
            }

        }
    }

    public void MoveUIToCam()
    {
        Vector3 pos = camTransform.position + (camTransform.forward * 0.5f);
        Quaternion rotation = new Quaternion(camTransform.rotation.x,camTransform.rotation.y,camTransform.rotation.z, camTransform.rotation.w);
        uiManager.transform.position = pos;
        uiManager.transform.rotation = rotation;

        //uiTransform.SetPositionAndRotation(pos, rotation);
    }

    public void MoveAvatar3D()
    {
        animManager.MoveTo(camTransform);
    }

    public void ToggleUI()
    {
        uiManager.SetActive(!uiManager.activeSelf);
        bool isUIActive = uiManager.activeSelf;
        preventPinch = uiManager.activeSelf;
        MoveUIToCam();
    }

    public void CloseApplication()
    {
        Application.Quit();
    }

    public void ChangeAlgorithm(int index)
    {
        indexAlgoritmo = index;
    }

    
}
