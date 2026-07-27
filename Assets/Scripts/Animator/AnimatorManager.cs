using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;



public class AnimatorManager : MonoBehaviour
{
    /*
    [SerializeField]
    CameraInfo camInfo;
    */
    [SerializeField]
    TextMeshPro tmp;

    [System.Serializable]
    public struct Dict
    {
        public string key;
        public string value;

    }


    Animator anim;
    //Alfabeto de las animaciones y sus respectivos strings/chars
    public List<Dict> alphabet = new List<Dict>();

    //Añadir letras o strings detectados, leer en FIFO
    public List<string> wordFifo = new List<string>(); 
    public List<string> animFifo = new List<string>(); 


    //Bosquejo de State machine para el modelo 3d animado
    enum AnimStates
    {
        IDLE,
        IS_ANIMATING,
    }

    AnimStates currentState = AnimStates.IDLE;
    bool triggerAnim= false;
    
    void Start()
    {
        anim = GetComponentInChildren<Animator>();
       
    }

   
    void Update()
    {
        if (triggerAnim && animFifo.Count != 0)
        {
            if(currentState != AnimStates.IS_ANIMATING)
            {
                currentState = AnimStates.IS_ANIMATING;
                anim.CrossFadeInFixedTime(animFifo[0],1,0,-1);
                
            }else if(anim.GetCurrentAnimatorStateInfo(0).normalizedTime>1 && !anim.IsInTransition(0)){
                currentState = AnimStates.IDLE;
                wordFifo.RemoveAt(0);
                animFifo.RemoveAt(0);
                //Finished interpreting, now we stop the animation loop, return to IDLE pose
                if(animFifo.Count == 0)
                {
                    triggerAnim = false;
                    anim.CrossFadeInFixedTime("Male_Basemesh_Rig_01|Male_Basemesh_Rig_01_Idle",1);
                    ChangeText("");

                }
            }
        }
    }

    void PlayAnim()
    {
        if (animFifo.Count != 0)
        {
            triggerAnim = true;
        }
        else
        {
            ChangeText("");
        }
        
    }

    char[] Sanitize(string phrase)
    {
        char[] letters = phrase.ToCharArray();
        for (int i = 0; i< letters.Length;i++)
        {
            char letter = char.ToUpper(letters[i]);
            if (char.ToUpper(letter) == 'Á')
            {
                letter = 'A';
            }else if (char.ToUpper(letter)  == 'É')
            {
                letter = 'E';
            }else if (char.ToUpper(letter)  == 'Í')
            {
                letter = 'I';
            }else if (char.ToUpper(letter)  == 'Ó')
            {
                letter = 'O';
            }else if (char.ToUpper(letter)  == 'Ú' || char.ToUpper(letter) == 'Ü')
            {
                letter = 'U';
            }
            letters[i] = letter;
            
        }
        return letters;
    }
    //Busca en el array de structs "dict" en donde se encuentra la key especificada y retorna su value
    string FindByKey(string key)
    {
        foreach(Dict dict in alphabet)
        {
            if(dict.key == key.ToUpper())
            {
                return dict.value;
            }
        }
        Debug.Log("No encontré '"+key+"' en el alfabeto");
        return "";
    }
    public void Interpret(string phrase)
    {
        char[] letters = Sanitize(phrase);
        bool edgecasefound = false;
        for(int i = 0; i < letters.Length; i++)
        {
            //if an LL or an RR were processed beforehand, skip this iteration
            if (edgecasefound)
            {
                edgecasefound = false;
                continue;
            }

            char letter = char.ToUpper(letters[i]);
            string anim = "";

            
            //Handle edge cases first (LL and RR)
            if (letter == 'L')
            {
                //check if next iteration is not out of bounds
                if (i+1 < letters.Length && char.ToUpper(letters[i+1]) == 'L')
                {
                    //LL found, chain LL's animation and signal to skip next iteration
                    edgecasefound = true;
                    anim = FindByKey("LL");
                    wordFifo.Add("LL");
                }
                else
                {
                    //is just a singular L
                    anim = FindByKey(letter.ToString());
                }
            }
            else if (letter == 'R')
            {
                //check if next iteration is not out of bounds
                if (i+1 < letters.Length && char.ToUpper(letters[i+1]) == 'R')
                {
                    //LL found, chain RR's animation and signal to skip next iteration
                    edgecasefound = true;
                    anim = FindByKey("RR");
                    wordFifo.Add("RR");
                }
                else
                {
                    //is just a singular R
                    anim = FindByKey(letter.ToString());
                }
            }
            else
            {
                anim = FindByKey(letter.ToString());
                
            }

            //if an animation was found, add it to the chain and wordlist
            if (anim != "")
            {
                if (!edgecasefound) wordFifo.Add(letter.ToString());
                animFifo.Add(anim); 
            }
            //if not, just do nothing and skip it
            
        }
        PlayAnim();
        
    }

    public void MoveTo(Transform transform)
    {
        Vector3 pos = transform.position + (transform.forward * 0.5f);
        Quaternion rotation = new Quaternion(transform.rotation.x,transform.rotation.y,transform.rotation.z, transform.rotation.w);
        this.transform.SetPositionAndRotation(pos, rotation);
    }

    public bool GetTriggerAnim()
    {
        return this.triggerAnim;
    }

    public void CancelInterpretation()
    {
        if (triggerAnim && animFifo.Count != 0)
        {
            StartCoroutine(CancelAnimation());
        }
    }
    public void ChangeText(string text)
    {
        tmp.text = text;
    }

    IEnumerator CancelAnimation()
    {
        wordFifo.Clear();
        animFifo.Clear();
        ChangeText("Cancelando animación...");
        anim.CrossFadeInFixedTime("Male_Basemesh_Rig_01|Male_Basemesh_Rig_01_Idle",1);
        yield return new WaitForSeconds(1.5f); // <- we wait for the crossfade IDLE animation to end.
        ChangeText("");
        triggerAnim = false;
        currentState = AnimStates.IDLE;
    }
}
