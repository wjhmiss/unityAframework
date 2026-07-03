using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum CampType //阵营
{
    Player,
    Enemy
}

public class DamageCaster : MonoBehaviour
{
    public BoxCollider _box;
    public EnemyBase enemyBase; //敌人基类
    public CampType currentCamp; //当前的阵容类型
    
   

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        _box.enabled = false;

        enemyBase = transform.parent.GetComponent<EnemyBase>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    //进入触发器
    private void OnTriggerEnter(Collider other)
    {
        //当前角色是玩家
        if (currentCamp == CampType.Player)
        {
            //如果检测到敌人
            if (other.CompareTag("Enemy"))
            {
                Debug.Log("检测到敌人");
                //敌人受伤
                other.gameObject.GetComponent<EnemyBase>()
                    .Hurt(Player.Instance.attackPower);
              
            }
            
        }else if (currentCamp == CampType.Enemy)
        {
            //如果检测到玩家
            if (other.CompareTag("Player"))
            {
                Debug.Log("检测到玩家");
                //玩家受伤
                Player.Instance.Hurt(enemyBase.attackPower);
            }
        }
        
        
    }
}
