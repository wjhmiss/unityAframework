using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// 玩家状态管理脚本
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [Header("移动参数")]
        [Tooltip("移动速度（米/秒）")]
        [SerializeField] private float m_moveSpeed = 5f;

        [Header("翻滚参数")]
        [Tooltip("翻滚持续时间（秒）")]
        [SerializeField] private float m_rollDuration = 0.4f;
        [Tooltip("翻滚冷却时间（秒）")]
        [SerializeField] private float m_rollCooldown = 1.5f;
        [Tooltip("翻滚移动速度倍率")]
        [SerializeField] private float m_rollSpeedMultiplier = 2.5f;

        [Header("受伤参数")]
        [Tooltip("受伤无敌时间（秒）")]
        [SerializeField] private float m_hurtInvincibleTime = 1f;

        [Header("攻击参数")]
        [Tooltip("攻击连招窗口时间（秒）")]
        [SerializeField] private float m_attackComboWindow = 10f;
        [Tooltip("每段攻击间隔（秒）")]
        [SerializeField] private float m_attackInterval = 0.5f;

        [Header("玩家属性")]
        [SerializeField] private float m_maxHealth = 100f;
        [SerializeField] private float m_currentHealth = 100f;
        [SerializeField] private int m_coinCount = 0;

        [Header("状态标志")]
        [SerializeField] private bool m_isDead = false;
        [SerializeField] private bool m_isHurting = false;

        // 组件引用
        private Rigidbody m_rigidbody;
        private Animator m_animator;
        private PlayerVFXManager m_vfxManager; // 特效管理器

        // 输入方向
        private Vector3 m_inputDirection;
        private float m_horizontalInput;
        private float m_verticalInput;

        // 翻滚状态
        private bool m_canRoll = true;           // 是否可以翻滚
        private bool m_isRolling = false;        // 是否正在翻滚
        private float m_rollTimer = 0f;          // 翻滚计时器
        private float m_rollCooldownTimer = 0f;  // 冷却计时器

        // 受伤状态
        private float m_hurtTimer = 0f;          // 受伤计时器

        // 攻击状态
        private int m_attackComboCount = 0;       // 当前连招段数（0=未攻击, 1=ATTACK1, 2=ATTACK2, 3=ATTACK3）
        private float m_attackComboTimer = 0f;    // 连招窗口计时器
        private float m_attackIntervalTimer = 999f; // 攻击间隔计时器，初始大值确保首次可攻击
        private bool m_isAttacking = false;       // 是否正在攻击

        //下落
        public bool isGrounded = true; //是否在地面上
        public float gravity = -3.5f; //重力

        private void Awake()
        {
            // 获取组件
            m_rigidbody = GetComponent<Rigidbody>();
            m_animator = GetComponent<Animator>();
            m_vfxManager = GetComponent<PlayerVFXManager>();

            m_rigidbody.freezeRotation = true; //不能通过物理引擎旋转 
            m_rigidbody.useGravity = false; //不使用重力

            // 初始化玩家状态
            m_currentHealth = m_maxHealth;
        }

        private void Start()
        {
            Debug.Log("PlayerStats initialized");
        }

        private void Update()
        {
            // 死亡时不处理输入
            if (m_isDead)
            {
                return;
            }

            // 受伤无敌时间计时
            if (m_isHurting)
            {
                m_hurtTimer += Time.deltaTime;
                if (m_hurtTimer >= m_hurtInvincibleTime)
                {
                    m_hurtTimer = 0f;
                    m_isHurting = false;
                }
                return; // 受伤期间不处理其他输入
            }

            // 攻击间隔计时，间隔过后可以移动
            if (m_isAttacking)
            {
                m_attackIntervalTimer += Time.deltaTime;
                if (m_attackIntervalTimer >= m_attackInterval)
                {
                    m_isAttacking = false; // 间隔过后允许移动

                    // 第三段攻击结束后立即重置连招，让玩家可以快速开始新一轮攻击
                    if (m_attackComboCount >= 3)
                    {
                        ResetAttackCombo();
                    }
                }
            }

            // 连招窗口计时，超时则重置连招
            if (m_attackComboCount > 0)
            {
                m_attackComboTimer += Time.deltaTime;
                if (m_attackComboTimer >= m_attackComboWindow)
                {
                    Debug.Log($"Combo Timer: {m_attackComboTimer:F2}, Window: {m_attackComboWindow:F2}");
                    ResetAttackCombo();
                }
            }

            // 翻滚冷却计时
            if (!m_canRoll)
            {
                m_rollCooldownTimer += Time.deltaTime;
                if (m_rollCooldownTimer >= m_rollCooldown)
                {
                    m_rollCooldownTimer = 0f;
                    m_canRoll = true;
                }
            }

            // 翻滚持续时间计时
            if (m_isRolling)
            {
                m_rollTimer += Time.deltaTime;
                if (m_rollTimer >= m_rollDuration)
                {
                    m_rollTimer = 0f;
                    m_isRolling = false;

                }
                return; // 翻滚期间不处理其他输入
            }

            // 读取键盘输入
            HandleInput();

            // 检测翻滚输入
            HandleRollInput();

            // 检测攻击输入
            HandleAttackInput();
        }

        private void FixedUpdate()
        {
            //下落
            if (!isGrounded)
            {
                transform.Translate(0, gravity * Time.fixedDeltaTime, 0);
            }

            // 死亡时不移动
            if (m_isDead)
            {
                return;
            }

            // 受伤期间停止移动
            if (m_isHurting)
            {
                m_rigidbody.velocity = Vector3.zero;
                return;
            }

            // 攻击期间停止移动
            if (m_isAttacking)
            {
                m_rigidbody.velocity = Vector3.zero;
                return;
            }

            // 翻滚期间处理翻滚移动
            if (m_isRolling)
            {
                HandleRollMovement();
                return;
            }

            // 处理物理移动
            HandleMovement();
        }

        /// <summary>
        /// 处理键盘输入
        /// </summary>
        private void HandleInput()
        {
            // 获取水平输入（A/D 或 左/右箭头）
            m_horizontalInput = Input.GetAxisRaw("Horizontal");

            // 获取垂直输入（W/S 或 上/下箭头）
            m_verticalInput = Input.GetAxisRaw("Vertical");

            // 计算输入方向
            m_inputDirection.Set(m_horizontalInput, 0f, m_verticalInput);
            m_inputDirection.Normalize();
            //方向转45度, 跟游戏方向保持一致
            m_inputDirection = Quaternion.Euler(0, -45f, 0) * m_inputDirection;
        }

        /// <summary>
        /// 处理物理移动
        /// </summary>
        private void HandleMovement()
        {
            // 攻击间隔内不能移动，让攻击动画完整播放
            if (m_isAttacking)
            {
                return;
            }

            bool isMoving = m_inputDirection.magnitude > 0.01f;
            var state = m_animator.GetCurrentAnimatorStateInfo(0);
            //var currentState = m_animator.GetCurrentAnimatorStateInfo(0);
            //var nextState = m_animator.GetNextAnimatorStateInfo(0);

            // 仅在状态变化时切换动画，避免每帧重启
            if (isMoving && !state.IsName("Run"))
            {
                m_animator.Play("Run");
                //m_animator.CrossFade("Run", 0.2f);
            }
            else if (!isMoving && !state.IsName("Idel"))
            {
                m_animator.Play("Idel");
                //m_animator.CrossFade("Idel", 0.2f);
            }

            if (isMoving)
            {
                // 设置 Rigidbody 速度
                m_rigidbody.velocity = m_inputDirection * m_moveSpeed;

                // 让角色朝向移动方向
                transform.rotation = Quaternion.LookRotation(m_inputDirection);
            }
            else
            {
                // 没有输入时停止移动
                m_rigidbody.velocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 处理翻滚输入
        /// </summary>
        private void HandleRollInput()
        {
            // 按空格键且可以翻滚时触发翻滚
            if (Input.GetKeyDown(KeyCode.Space) && m_canRoll)
            {
                // 检查当前是否在 Run 或 Idle 状态
                var state = m_animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Run") || state.IsName("Idel"))
                {
                    StartRoll();
                }
            }
        }

        /// <summary>
        /// 处理攻击输入
        /// </summary>
        private void HandleAttackInput()
        {
            // 按下鼠标左键且攻击间隔已过
            if (Input.GetMouseButtonDown(0) && m_attackIntervalTimer >= m_attackInterval)
            {
                // 检查当前是否在 Run、Idle 或攻击状态（允许连招）
                var state = m_animator.GetCurrentAnimatorStateInfo(0);
                bool canAttack = state.IsName("Run") || state.IsName("Idel") ||
                                  state.IsName("ATTACK1") || state.IsName("ATTACK2") ||
                                  state.IsName("ATTACK3");

                if (canAttack)
                {
                    StartAttack();
                }
            }
        }

        /// <summary>
        /// 开始攻击
        /// </summary>
        private void StartAttack()
        {
            m_isAttacking = true;
            m_attackIntervalTimer = 0f;
            m_attackComboTimer = 0f;
            m_attackComboCount++;

            // 根据连招段数播放对应攻击动画
            switch (m_attackComboCount)
            {
                case 1:
                    m_animator.Play("ATTACK1");
                    break;
                case 2:
                    m_animator.Play("ATTACK2");
                    break;
                case 3:
                    m_animator.Play("ATTACK3");
                    // 第三段攻击后不立即重置，让连招窗口超时后自然重置
                    // 这样 ATTACK3 动画可以完整播放
                    break;
            }

            // 播放攻击特效
            if (m_vfxManager != null)
            {
                m_vfxManager.PlayAttackVFX(m_attackComboCount);
            }
        }

        /// <summary>
        /// 重置攻击连招
        /// </summary>
        private void ResetAttackCombo()
        {
            m_attackComboCount = 0;
            m_attackComboTimer = 0f;
            m_isAttacking = false;
            m_attackIntervalTimer = 999f; // 重置为初始值，确保可以立即开始新一轮攻击
        }

        /// <summary>
        /// 开始翻滚
        /// </summary>
        private void StartRoll()
        {
            m_canRoll = false;
            m_isRolling = true;
            m_rollTimer = 0f;

            // 播放翻滚动画
            m_animator.Play("Roll");

            // 如果没有移动方向，使用当前朝向
            if (m_inputDirection.magnitude < 0.01f)
            {
                m_inputDirection = transform.forward;
            }
        }

        /// <summary>
        /// 处理翻滚期间的移动
        /// </summary>
        private void HandleRollMovement()
        {
            // 翻滚期间向前快速移动
            m_rigidbody.velocity = transform.forward * m_moveSpeed * m_rollSpeedMultiplier;
        }

        /// <summary>
        /// 受伤处理
        /// </summary>
        /// <param name="damage">伤害值</param>
        public void TakeDamage(float damage)
        {
            // 死亡或受伤期间不处理
            if (m_isDead || m_isHurting)
            {
                return;
            }

            // 减少生命值
            m_currentHealth = Math.Max(0, m_currentHealth - damage);

            // 设置受伤状态
            m_isHurting = true;
            m_hurtTimer = 0f;

            // 播放受伤动画
            m_animator.Play("Hurt");

            // 检查是否死亡
            if (m_currentHealth <= 0)
            {
                Die();
            }
        }


        //碰撞
        private void OnCollisionEnter(Collision collision)
        {
            //如果是地板
            if (collision.collider.CompareTag("floor"))
            {
                isGrounded = true;
            }

        }

        private void OnCollisionExit(Collision other)
        {
            if (other.collider.CompareTag("floor"))
            {
                isGrounded = false;
            }
        }

        private void OnCollisionStay(Collision collisionInfo)
        {
            if (collisionInfo.collider.CompareTag("floor"))
            {
                isGrounded = true;
            }
        }


        /// <summary>
        /// 恢复生命值
        /// </summary>
        public void Heal(float amount)
        {
            m_currentHealth = Math.Min(m_currentHealth + amount, m_maxHealth);

            // 播放回血特效
            if (m_vfxManager != null)
            {
                m_vfxManager.PlayHealVFX();
            }
        }

        /// <summary>
        /// 增加金币
        /// </summary>
        public void AddCoin(int amount)
        {
            m_coinCount += amount;
        }

        /// <summary>
        /// 死亡处理
        /// </summary>
        private void Die()
        {
            m_isDead = true;

            // 停止所有移动
            m_rigidbody.velocity = Vector3.zero;

            // 播放死亡动画
            m_animator.Play("Dead");

            Debug.Log("Player died");
        }

        #region Animation Event Methods
        /// <summary>
        /// 打开攻击触发器（动画事件调用）
        /// </summary>
        public void OpenAttackTrigger()
        {
            // 开启攻击判定，可以在这里启用攻击碰撞检测
            Debug.Log("Attack trigger opened");
        }

        /// <summary>
        /// 关闭攻击触发器（动画事件调用）
        /// </summary>
        public void CloseAttackTrigger()
        {
            // 关闭攻击判定，可以在这里禁用攻击碰撞检测
            Debug.Log("Attack trigger closed");
        }
        
        #endregion

        // 公开属性
        public float CurrentHealth => m_currentHealth;
        public float MaxHealth => m_maxHealth;
        public int CoinCount => m_coinCount;
        public bool IsDead => m_isDead;
        public bool IsHurting => m_isHurting;
        public bool IsMoving => m_inputDirection.magnitude > 0.01f; // 是否正在移动
    }
}