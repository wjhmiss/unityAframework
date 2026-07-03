using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Enemy1 : EnemyBase
{

    public Transform targetPlayer; //目标玩家
    public NavMeshAgent _nav; //导航代理
  

    public BoxCollider _attackTriggerBox; //攻击触发器
    
    
    
    
    private void Awake()
    {
       
        _nav = GetComponent<NavMeshAgent>();
        _anim = GetComponent<Animator>();
        
        
    }

    // Start is called before the first frame update
    void Start()
    {
        targetPlayer = Player.Instance.transform;
        attackPower = 40f; //攻击力40

        _attackTriggerBox = transform.Find("DamageCaster")
            .GetComponent<BoxCollider>();
    }

    // Update is called once per frame
    void Update()
    {
        if (isDead)
        {
            return;
        }
        
        
        //如果速度大于0
        if (_nav.speed > 0)
        {
            isMoving = true; 
        }
        else
        {
            isMoving = false; 
        }
        
        
        //计算攻击间隔
        if (isAttacking)
        {
            attackTimer -= Time.deltaTime;
            
            //经过了2.3秒, 一次攻击完成
            if (attackTimer <= (attackInterval - attackConsumerTime))
            {
                isAttacking = false; 
                //此时 attackTimer = 2.7秒
            }
        }


        //攻击后的冷却 冷却2.7秒
        if (!canAttack && !isAttacking)
        {
            attackTimer -= Time.deltaTime;
            //5秒全部结束
            if (attackTimer <= 0)
            {
                attackTimer = 0;
                canAttack = true; //标记为可以攻击
            }
        }
            
        
        
        CalculateEnemyMovement();
    }
    
    //计算敌人的移动
    private void CalculateEnemyMovement()
    {
        //如果正在攻击中, 希望在攻击过程中, 确定攻击位置, 不再转身和移动
        if (isAttacking)
        {
            return;
        }
        
        
        //求出玩家和敌人的距离
        float distance = Vector3.Distance(
            targetPlayer.position, transform.position);
        
        //求出敌人到玩家的向量
        Vector3 direction = targetPlayer.position - transform.position;
        //让敌人看向该向量
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        
        
        // Debug.Log("距离" + distance);
        
        //超出视觉范围
        if (distance > 6)
        {
            //停止追逐
            _nav.speed = 0f; //设置速度为0
            _anim.SetFloat("speed", _nav.speed); //设置速度
            
            return;
        }
        
        //发现玩家
        if (distance >= 2)
        {
            // 一定在2-6之间
            _nav.speed = 3.5f; //设置速度
            _nav.SetDestination(targetPlayer.position); //设置目标
            _anim.SetFloat("speed", _nav.speed); //设置速度
        }
        else 
        {
            // 距离小于2 进入攻击范围 先停下
            // _nav.speed = 0;
            
            _nav.SetDestination(targetPlayer.position); //设置目标
            _nav.speed = 0f; //设置速度为0
            _anim.SetFloat("speed", _nav.speed); //设置速度

            //判断是否可以攻击
            if (canAttack)
            {
                Attack();
            }
            
            //平滑转身
            transform.rotation = Quaternion.Slerp(
                transform.rotation, 
                targetRotation, 
                Time.deltaTime * rotationSpeed
                );
            
            

        }
        
    }

    //发动一次攻击
    private void Attack()
    {
        Debug.Log("触发攻击");
        _anim.SetTrigger("Attack");  //攻击事件
        
        canAttack = false; //不能够攻击
        isAttacking = true; //正在攻击中
        attackTimer = attackInterval; //开始计时
        

    }
    
    
    //打开攻击检测触发器
    public void OpenAttackTrigger()
    {
        _attackTriggerBox.enabled = true;
        Debug.Log("打开碰撞器");
    }
    
    
    //打开攻击检测触发器
    public void CloseAttackTrigger()
    {
        _attackTriggerBox.enabled = false; 
        Debug.Log("关闭碰撞器");
    }

}
