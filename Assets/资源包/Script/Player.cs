using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{

    public static Player Instance;
    
    public Rigidbody _rb;
    public Animator _anim; 
    
    public float moveSpeed = 5f; //移动速度
    public float horizontalInput; //水平方向输入
    public float verticalInput; //垂直方向输入
    public Vector3 inputDirection; //输入方向
    
    //移动
    public bool isMove = false; //是否移动

    //翻滚
    public bool canRoll = true; //是否可以翻滚
    public bool isRolling = false; //是否正在翻滚
    public float rollingTimer = 0; //翻滚定时器
    public float rollingTime = 0.4f; //一次翻滚动画的持续时间
    public float rollCollingTimer = 0; //冷却时间定时器
    public float rollCollingTime = 1f; //冷却时间
    
    //受伤
    public bool isHurting = false; //正在受伤中
    public float dontHurtTimer = 0 ; //不受伤定时器
    public float dontHurtTime = 1f ; //不受伤时间
    public float hp = 100f; //当前生命值
    public float maxHp = 100f; //最大生命值
    
    //金币值
    public int coin = 200; //初始200金币
    
    //攻击
    public float attackPower = 30; 
    public BoxCollider _attackTriggerBox; //攻击触发器

    
    //死亡
    public bool isDead = false; //是否死亡
    
    //下落
    public bool isGrounded = true; //是否在地面上
    public float gravity = -3.5f; //重力
    

    private void Awake()
    {
        Instance = this;
        
        _rb = GetComponent<Rigidbody>();
        _anim = GetComponent<Animator>();

        _rb.freezeRotation = true; //不能通过物理引擎旋转 
        _rb.useGravity = false; //不使用重力
    }

    // Start is called before the first frame update
    void Start()
    {
        _attackTriggerBox = transform.Find("DamageCaster")
            .GetComponent<BoxCollider>();
    }

    // Update is called once per frame
    void Update()
    {
        //如果死亡
        if (isDead)
        {
            return;
        }

        
        //监听鼠标左键
        if (Input.GetMouseButtonDown(0))
        {
            _anim.SetTrigger("Attack");
        }
        
        
        //如果正在受击
        if (isHurting)
        {
            dontHurtTimer += Time.deltaTime;
            if (dontHurtTimer >= dontHurtTime)
            {
                isHurting = false;
                dontHurtTimer = 0;
            }
        }
        
        
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical"); 
        inputDirection.Set(horizontalInput, 0, verticalInput);
        inputDirection.Normalize();
        
        //判断模长
        if (inputDirection.magnitude != 0)
        {
            isMove = true;
        }
        else
        {
            isMove = false;
        }
        _anim.SetBool("isMove", isMove);
        
        
        
        //方向转45度, 跟游戏方向保持一致
        inputDirection = Quaternion.Euler(0, -45f, 0) * inputDirection; 
        
        
        //角色转向
        if (inputDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(inputDirection);
        }


        //按下空格, 开始翻滚
        if (Input.GetKeyDown(KeyCode.Space) && canRoll)
        {
            canRoll = false; //禁止再翻滚
            isRolling = true; //标记开始翻滚中
            _anim.SetTrigger("Roll"); //动画器触发事件
        }
        
        
        //判断正在翻滚时间
        if (isRolling)
        {
            rollingTimer += Time.deltaTime;
            if (rollingTimer >= rollingTime) //到达0.4秒 翻滚完成
            {
                isRolling = false; //结束翻滚
                rollingTimer = 0; 
            }
        }
        
        //判断冷却时间
        if (!canRoll)
        {
            rollCollingTimer += Time.deltaTime;
            if (rollCollingTimer >= rollCollingTime) //冷却定时器大于1秒
            {
                rollCollingTimer = 0; //定时器重置
                canRoll = true; //可以正常翻滚
            }
            
        }
        
        
        
    }


    private void FixedUpdate()
    {
        //下落
        if (!isGrounded)
        {
            transform.Translate(0, gravity * Time.fixedDeltaTime, 0);
        }
        
        
        //翻滚中
        if (isRolling)
        {
            _rb.velocity = transform.forward * moveSpeed * 2.5f; 

            return;
        }
        
        
        //移动函数
        _rb.velocity = inputDirection * moveSpeed; 
        
        
        
        
    }
    
    
    //受伤函数 由敌人攻击调用
    public void Hurt(float damage)
    {
        //如果玩家正在受击
        if (isHurting)
        {
            return;
        }
        
        
        isHurting = true; //正在受击
        hp = MathF.Max(0, hp - damage); //剩余血量
        _anim.SetTrigger("Hurt");
        GameManager.Instance.UpdateHealth();  //修改血条

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
        
        _anim.SetTrigger("Dead");
        
        
        //游戏失败
        GameManager.Instance.GameOver();


    }
    
    //碰撞
    private void OnCollisionEnter(Collision collision)
    {
        //如果是地板
        if (collision.collider.CompareTag("Floor"))
        {
            isGrounded = true;
        }
        
    }

    private void OnCollisionExit(Collision other)
    {
        if (other.collider.CompareTag("Floor"))
        {
            isGrounded = false;
        }
    }

    private void OnCollisionStay(Collision collisionInfo)
    {
        if (collisionInfo.collider.CompareTag("Floor"))
        {
            isGrounded = true;
        }
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
    
    
    //增加金币
    public void AddCoin(int c)
    {
        coin += c; 
        GameManager.Instance.UpdateCoin();
        
    }
    
    
    //增加血量
    public void AddHealth(float h)
    {
        hp = MathF.Min(hp + h, maxHp) ;
        GameManager.Instance.UpdateHealth();
        
        
    }

}
