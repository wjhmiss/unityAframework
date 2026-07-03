using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float damage;  //伤害值
    private float speed = 4f; //飞行速度
    public Vector3 direction; //方向 

    public float timeout = 5f; //超时销毁
    public float timer = 0 ; //定时器
    
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime, Space.World);


        timer += Time.deltaTime;
        
        //5秒后销毁
        if (timer >= timeout)
        {
            Destroy(gameObject);
        }
        
    }

    //设置伤害
    public void SetDamage(float d)
    {
        damage = d;
    }

    //进入触发器
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Player.Instance.Hurt(damage);
            Destroy(gameObject);
        }
    }

    public void Init(Vector3 d, float attackPower)
    {
        SetDamage(attackPower) ;

        direction = d;

        //子弹跟着方向旋转
        transform.rotation = Quaternion.LookRotation(direction);
        //再给x轴旋转90
        transform.Rotate(90f, 0 ,0 , Space.Self);
        
        
    }
}
