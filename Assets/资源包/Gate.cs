using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Gate : MonoBehaviour
{

    public static Gate Instance; 
    
    
    public float targetY = -2.5f; //打开门后 降落的高度
    public float duration = 2f; //开门2秒

    private void Awake()
    {
        Instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    //打开大门
    public void OpenGate()
    {
        StartCoroutine(OpenGateAnim());
    }

    IEnumerator OpenGateAnim()
    {
        float currentDuration = 0;
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + Vector3.up * targetY;

        while (currentDuration < duration)
        {
            currentDuration += Time.deltaTime;

            transform.position = Vector3.Lerp(
                startPos,
                targetPos,
                currentDuration / duration
            );

            yield return null;

        }

    }
}
