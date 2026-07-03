using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Game.Player;

namespace Game.Enemy
{
    /// <summary>
    /// 敌人控制脚本
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    public class Enemy : MonoBehaviour
    {
        [Header("追踪参数")]
        [Tooltip("开始追踪的距离")]
        [SerializeField] private float m_chaseStartDistance = 6f;
        [Tooltip("停止追踪的距离")]
        [SerializeField] private float m_chaseStopDistance = 2f;

        [Header("攻击参数")]
        [Tooltip("攻击冷却时间（秒）")]
        [SerializeField] private float m_attackCooldown = 1.5f;
        [Tooltip("攻击伤害值")]
        [SerializeField] private float m_attackDamage = 10f;

        [Header("属性")]
        [Tooltip("最大生命值")]
        [SerializeField] private float m_maxHealth = 50f;
        [SerializeField] private float m_currentHealth = 50f;

        [Header("受伤参数")]
        [Tooltip("受伤无敌时间（秒）")]
        [SerializeField] private float m_hurtInvincibleTime = 0.5f;

        // 组件引用
        private NavMeshAgent m_navMeshAgent;
        private Animator m_animator;
        private Transform m_playerTransform;
        private PlayerStats m_playerStats;

        // 状态
        private bool m_isChasing = false;
        private bool m_isDead = false;
        private bool m_isHurting = false;
        private bool m_isAttacking = false;

        // 计时器
        private float m_hurtTimer = 0f;
        private float m_attackCooldownTimer = 999f; // 初始大值确保首次可攻击

        // 动画状态名称常量
        private const string k_animIdle = "IDEL";
        private const string k_animWalk = "WALK";
        private const string k_animAttack = "ATTACK";
        private const string k_animHurt = "HURT";
        private const string k_animDead = "DEAD";

        private void Awake()
        {
            // 获取组件
            m_navMeshAgent = GetComponent<NavMeshAgent>();
            m_animator = GetComponent<Animator>();
        }

        private void Start()
        {
            // 初始化生命值
            m_currentHealth = m_maxHealth;

            // 查找玩家
            FindPlayer();

            // 初始播放待机动画
            PlayAnimation(k_animIdle);
        }

        private void Update()
        {
            // 死亡时不处理任何逻辑
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
                return; // 受伤期间不处理其他逻辑
            }

            // 攻击冷却计时
            if (m_isAttacking)
            {
                m_attackCooldownTimer += Time.deltaTime;
                if (m_attackCooldownTimer >= m_attackCooldown)
                {
                    m_isAttacking = false;
                }
                return; // 攻击期间不处理其他逻辑
            }

            // 如果没有找到玩家，尝试重新查找
            if (m_playerTransform == null)
            {
                FindPlayer();
                return;
            }

            // 计算与玩家的距离
            float distanceToPlayer = Vector3.Distance(transform.position, m_playerTransform.position);

            // 根据距离决定是否追踪或攻击
            HandleBehavior(distanceToPlayer);
        }

        /// <summary>
        /// 查找玩家
        /// </summary>
        private void FindPlayer()
        {
            // 通过 PlayerStats 组件查找玩家
            PlayerStats playerStats = FindObjectOfType<PlayerStats>();
            if (playerStats != null)
            {
                m_playerStats = playerStats;
                m_playerTransform = playerStats.transform;
            }
        }

        /// <summary>
        /// 处理敌人行为逻辑
        /// </summary>
        /// <param name="distance">与玩家的距离</param>
        private void HandleBehavior(float distance)
        {
            // 距离小于停止追踪距离时，尝试攻击
            if (distance <= m_chaseStopDistance)
            {
                StopChase();
                TryAttack();
            }
            // 距离在追踪范围内时追踪玩家
            else if (distance > m_chaseStopDistance && distance < m_chaseStartDistance)
            {
                StartChase();
            }
            // 距离大于开始追踪距离时停止追踪
            else if (distance >= m_chaseStartDistance)
            {
                StopChase();
            }
        }

        /// <summary>
        /// 开始追踪
        /// </summary>
        private void StartChase()
        {
            if (!m_isChasing)
            {
                m_isChasing = true;
                m_navMeshAgent.isStopped = false;
                PlayAnimation(k_animWalk);
            }

            // 设置目标位置为玩家位置
            m_navMeshAgent.SetDestination(m_playerTransform.position);

            // 让敌人朝向玩家
            LookAtPlayer();
        }

        /// <summary>
        /// 停止追踪
        /// </summary>
        private void StopChase()
        {
            if (m_isChasing)
            {
                m_isChasing = false;
                m_navMeshAgent.isStopped = true;
                PlayAnimation(k_animIdle);
            }
        }

        /// <summary>
        /// 尝试攻击
        /// </summary>
        private void TryAttack()
        {
            // 攻击冷却已过且不在受伤状态
            if (m_attackCooldownTimer >= m_attackCooldown && !m_isHurting)
            {
                StartAttack();
            }
        }

        /// <summary>
        /// 开始攻击
        /// </summary>
        private void StartAttack()
        {
            m_isAttacking = true;
            m_attackCooldownTimer = 0f;

            // 播放攻击动画
            PlayAnimation(k_animAttack);

            // 让敌人朝向玩家
            LookAtPlayer();

            // 对玩家造成伤害
            if (m_playerStats != null)
            {
                m_playerStats.TakeDamage(m_attackDamage);
            }
        }

        /// <summary>
        /// 让敌人朝向玩家
        /// </summary>
        private void LookAtPlayer()
        {
            if (m_playerTransform != null)
            {
                Vector3 direction = (m_playerTransform.position - transform.position).normalized;
                direction.y = 0f; // 保持水平方向
                if (direction.magnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }
        }

        /// <summary>
        /// 播放动画
        /// </summary>
        /// <param name="animName">动画状态名称</param>
        private void PlayAnimation(string animName)
        {
            if (m_animator != null)
            {
                m_animator.Play(animName);
            }
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

            // 停止追踪和移动
            m_navMeshAgent.isStopped = true;
            m_isChasing = false;

            // 播放受伤动画
            PlayAnimation(k_animHurt);

            // 检查是否死亡
            if (m_currentHealth <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// 死亡处理
        /// </summary>
        private void Die()
        {
            m_isDead = true;

            // 停止所有移动
            m_navMeshAgent.isStopped = true;
            m_isChasing = false;

            // 播放死亡动画
            PlayAnimation(k_animDead);

            Debug.Log("Enemy died");
        }

        // 公开属性
        public bool IsChasing => m_isChasing;
        public bool IsDead => m_isDead;
        public bool IsHurting => m_isHurting;
        public bool IsAttacking => m_isAttacking;
        public float CurrentHealth => m_currentHealth;
        public float MaxHealth => m_maxHealth;
        public float DistanceToPlayer => m_playerTransform != null
            ? Vector3.Distance(transform.position, m_playerTransform.position)
            : float.MaxValue;
    }
}