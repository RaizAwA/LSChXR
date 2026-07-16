using UnityEngine;
using Meta.XR;

public class CameraInfo : MonoBehaviour
{

    PassthroughCameraAccess cameraAccess;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cameraAccess = GetComponent<PassthroughCameraAccess>();
    }

    // Update is called once per frame
    void Update()
    {
        if (cameraAccess.enabled)
        {
            Texture texture = cameraAccess.GetTexture();
        }

        if (cameraAccess.IsPlaying)
        {
            
        }
    }
}
