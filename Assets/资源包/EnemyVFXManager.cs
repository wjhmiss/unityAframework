using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public class EnemyVFXManager : MonoBehaviour
{
    public VisualEffect footStep; //脚步特效
    public VisualEffect attackVFX; //攻击特效

    public EnemyBase enemyBase;  //敌人基类

    private void Awake()
    {
        enemyBase = GetComponent<EnemyBase>();
    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
       
    }
    
    //播放脚步特效 由动画器事件调用
    public void FootStepVFX()
    {
        footStep.Play();
    }
    
    //攻击特效
    public void AttackVFX()
    {
        attackVFX.Play();
    }
    
}
