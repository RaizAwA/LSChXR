using UnityEngine;

public class UIManager : MonoBehaviour
{

    [SerializeField]
    Transform camsPosition;
    [SerializeField]
    float followSpeed = 1.5f;
    [SerializeField]
    float distance = 1.5f;

    void Start()
    {
        
    }

    void Update()
    {
        Vector3 targetPosition = camsPosition.position + (camsPosition.forward * distance);
        this.transform.position = Vector3.Lerp(this.transform.position, targetPosition,Time.deltaTime * followSpeed);
        this.transform.LookAt(new Vector3(camsPosition.position.x, this.transform.position.y, camsPosition.position.z));
        this.transform.Rotate(0, 180, 0); 
    }

    public void HelloWorld(){
        Debug.Log("HelloWorld");
    }
}
