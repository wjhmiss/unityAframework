using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Game.Player
{
    /// <summary>
    /// 玩家特效管理脚本
    /// </summary>
    public class PlayerVFXManager : MonoBehaviour
    {
        [Header("特效引用")]
        public VisualEffect footStep; //移动特效
        public VisualEffect heal; //回血特效
        public ParticleSystem blade01; //攻击特效1
        public ParticleSystem blade02; //攻击特效2
        public ParticleSystem blade03; //攻击特效3

        // 组件引用
        private PlayerStats m_playerStats;
        private bool m_lastIsMoving = false; // 上一帧的移动状态

        private void Awake()
        {
            // 获取 PlayerStats 组件
            m_playerStats = GetComponent<PlayerStats>();
        }

        private void Start()
        {
            if (m_playerStats == null)
            {
                Debug.LogError("PlayerVFXManager: PlayerStats component not found!");
            }
        }

        private void Update()
        {
            // 检查人物是否移动，控制脚步特效
            if (m_playerStats != null && footStep != null)
            {
                bool isMoving = m_playerStats.IsMoving;

                // 只在状态变化时触发，避免每帧重启特效
                if (isMoving != m_lastIsMoving)
                {
                    if (isMoving)
                    {
                        footStep.Play();
                    }
                    else
                    {
                        footStep.Stop();
                    }
                    m_lastIsMoving = isMoving;
                }
            }
        }

        #region 特效播放方法
        /// <summary>
        /// 播放回血特效
        /// </summary>
        public void PlayHealVFX()
        {
            if (heal != null)
            {
                heal.Play();
            }
        }

        /// <summary>
        /// 播放攻击特效（根据连招段数）
        /// </summary>
        /// <param name="comboCount">连招段数（1-3）</param>
        public void PlayAttackVFX(int comboCount)
        {
            switch (comboCount)
            {
                case 1:
                    if (blade01 != null) blade01.Play();
                    break;
                case 2:
                    if (blade02 != null) blade02.Play();
                    break;
                case 3:
                    if (blade03 != null) blade03.Play();
                    break;
            }
        }
        #endregion
    }
}
