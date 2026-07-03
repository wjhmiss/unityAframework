using System;
using UnityEngine;

//敌人父类
public class EnemyBase: MonoBehaviour
{
    public Animator _anim; //动画器
    
    //攻击相关
    public float attackTimer = 0; //攻击计时器
    public float attackInterval = 5f; //攻击间隔, 两次攻击的间隔
    public float attackConsumerTime = 2.3f; //单次攻击的耗时
    public bool isAttacking = false; //是否正在攻击
    public bool canAttack = true; //是否能够攻击
    public float attackPower; //攻击力
    
    
    //移动相关
    public float rotationSpeed = 3f; //转身速度
    public bool isMoving = false; //是否正在移动


    public float hp = 100f; //敌人血量
    public float maxHp = 100f; //敌人最大血量
    public bool isDead = false; //是否死亡
    
    //受伤函数 由敌人攻击调用
    public void Hurt(float damage)
    {
        if (isDead)
        {
            return; 
        }
        
        hp = MathF.Max(0, hp - damage); //剩余血量
        _anim.SetTrigger("Hurt"); 

        //判断死亡
        if (hp <= 0)
        {
            Dead();
        }
        
    }
    
    
    //死亡
    public void Dead()
    {
        isDead = true; 
        
        _anim.SetBool("isDead", isDead);
        
        //记录死亡
        Spawner.Instance.remainEnemies -= 1;
        
        //杀死敌人获得50金币
        Player.Instance.AddCoin(50);


    }
}