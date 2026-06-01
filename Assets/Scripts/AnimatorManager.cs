using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AnimatorManager : MonoBehaviour
{
    
    //Alfabeto de las animaciones y sus respectivos strings/chars
    Dictionary<string,string> alphabet = new Dictionary<string, string>();

    //Añadir letras o strings detectados, leer en FIFO
    List<string> wordFifo = new List<string>(); 

    //Bosquejo de State machine para el modelo 3d animado
    enum AnimStates
    {
        IDLE,
        IS_ANIMATING,
        FINISH
    }
        void Start()
    {
        
    }

   
    void Update()
    {
        
    }
}
