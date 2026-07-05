using UnityEngine;
using UnityEngine.UIElements;
using AFrameWork.Core;

namespace AFrameWork.GameUI
{
    /// <summary>
    /// 人物属性面板控制器 - 在屏幕左上角显示角色的各种属性
    /// 遵循 HealthBarController 的设计模式：单例、UIDocument、UXML/USS 序列化引用
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UserPanelController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════════════
        // 常量定义（BEM 命名规范，与 UXML 元素 name 对应）
        // ══════════════════════════════════════════════════════════════════════════

        private const string k_PanelName = "user-panel";
        private const string k_Title = "user-panel__title";
        private const string k_Level = "user-panel__level";
        private const string k_Hp = "user-panel__hp";
        private const string k_Mp = "user-panel__mp";
        private const string k_HpRegen = "user-panel__hp-regen";
        private const string k_MpRegen = "user-panel__mp-regen";
        private const string k_PhysAtk = "user-panel__phys-atk";
        private const string k_MagicAtk = "user-panel__magic-atk";
        private const string k_TrueDmg = "user-panel__true-dmg";
        private const string k_AtkSpeed = "user-panel__atk-speed";
        private const string k_PhysDef = "user-panel__phys-def";
        private const string k_MagicDef = "user-panel__magic-def";
        private const string k_Evasion = "user-panel__evasion";
        private const string k_HitRate = "user-panel__hit-rate";
        private const string k_CritRate = "user-panel__crit-rate";
        private const string k_CritDmg = "user-panel__crit-dmg";
        private const string k_ArmorPen = "user-panel__armor-pen";
        private const string k_MagicPen = "user-panel__magic-pen";
        private const string k_MoveSpeed = "user-panel__move-speed";
        private const string k_CastSpeed = "user-panel__cast-speed";
        private const string k_Cdr = "user-panel__cdr";
        private const string k_AtkRange = "user-panel__atk-range";
        private const string k_VisionRange = "user-panel__vision-range";
        private const string k_Gold = "user-panel__gold";
        private const string k_Exp = "user-panel__exp";

        /// <summary>
        /// 属性更新间隔（秒），避免每帧字符串分配
        /// </summary>
        private const float k_updateInterval = 0.1f;

        /// <summary>
        /// 玩家阵营 ID 范围（1-10）
        /// </summary>
        private const int k_playerFactionMinID = 1;
        private const int k_playerFactionMaxID = 10;

        // ══════════════════════════════════════════════════════════════════════════
        // 字段定义
        // ══════════════════════════════════════════════════════════════════════════

        [Tooltip("UserPanel UXML 资源")]
        [SerializeField]
        private VisualTreeAsset m_userPanelUxml;

        [Tooltip("UserPanel USS 样式资源")]
        [SerializeField]
        private StyleSheet m_userPanelUss;

        [Tooltip("目标角色（未设置时自动查找场景中的玩家角色）")]
        [SerializeField]
        private ObjectBase m_target;

        // 缓存的 Label 引用
        private Label m_titleLabel;
        private Label m_levelLabel;
        private Label m_hpLabel;
        private Label m_mpLabel;
        private Label m_hpRegenLabel;
        private Label m_mpRegenLabel;
        private Label m_physAtkLabel;
        private Label m_magicAtkLabel;
        private Label m_trueDmgLabel;
        private Label m_atkSpeedLabel;
        private Label m_physDefLabel;
        private Label m_magicDefLabel;
        private Label m_evasionLabel;
        private Label m_hitRateLabel;
        private Label m_critRateLabel;
        private Label m_critDmgLabel;
        private Label m_armorPenLabel;
        private Label m_magicPenLabel;
        private Label m_moveSpeedLabel;
        private Label m_castSpeedLabel;
        private Label m_cdrLabel;
        private Label m_atkRangeLabel;
        private Label m_visionRangeLabel;
        private Label m_goldLabel;
        private Label m_expLabel;

        private float m_lastUpdateTime;
        private bool m_targetResolved;

        // ══════════════════════════════════════════════════════════════════════════
        // 属性
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 场景中的单例引用（Awake 时缓存，OnDestroy 时清除）
        /// </summary>
        public static UserPanelController Instance { get; private set; }

        // ══════════════════════════════════════════════════════════════════════════
        // MonoBehaviour 方法
        // ══════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            Instance = this;

            var uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] UIDocument component is missing!", this);
#endif
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[{GetType().Name}] UIDocument.rootVisualElement is null!", this);
#endif
                return;
            }

            // 添加 USS 样式到 root
            if (m_userPanelUss != null)
            {
                root.styleSheets.Add(m_userPanelUss);
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] User Panel Uss resource is null! Please configure it in Inspector.", this);
#endif
            }

            // 清除 UIDocument.visualTreeAsset 可能已生成的 user-panel 元素，避免重复实例化导致重影
            root.Query<VisualElement>(name: k_PanelName).ForEach(e => e.RemoveFromHierarchy());

            // 实例化 UXML 并添加到 root
            if (m_userPanelUxml != null)
            {
                var tree = m_userPanelUxml.Instantiate();
                var panel = tree.Q<VisualElement>(k_PanelName);
                if (panel != null)
                {
                    panel.RemoveFromHierarchy();
                    root.Add(panel);
                }
                else
                {
                    root.Add(tree);
                }
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] User Panel Uxml resource is null! Please configure it in Inspector.", this);
#endif
                return;
            }

            // 缓存元素引用
            CacheElements(root);
        }

        private void Start()
        {
            ResolveTarget();
            UpdateAllAttributes();
        }

        private void Update()
        {
            // 目标未解析时持续尝试
            if (!m_targetResolved)
            {
                ResolveTarget();
            }

            if (m_target == null || !m_target.HasObjectStats())
            {
                return;
            }

            // 节流更新，避免每帧字符串分配
            if (Time.time - m_lastUpdateTime < k_updateInterval)
            {
                return;
            }

            m_lastUpdateTime = Time.time;
            UpdateAllAttributes();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 公共方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 设置目标角色
        /// </summary>
        /// <param name="target">目标 ObjectBase</param>
        public void SetTarget(ObjectBase target)
        {
            m_target = target;
            m_targetResolved = target != null;
            UpdateAllAttributes();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 缓存所有 Label 元素引用
        /// </summary>
        private void CacheElements(VisualElement root)
        {
            m_titleLabel = root.Q<Label>(k_Title);
            m_levelLabel = root.Q<Label>(k_Level);
            m_hpLabel = root.Q<Label>(k_Hp);
            m_mpLabel = root.Q<Label>(k_Mp);
            m_hpRegenLabel = root.Q<Label>(k_HpRegen);
            m_mpRegenLabel = root.Q<Label>(k_MpRegen);
            m_physAtkLabel = root.Q<Label>(k_PhysAtk);
            m_magicAtkLabel = root.Q<Label>(k_MagicAtk);
            m_trueDmgLabel = root.Q<Label>(k_TrueDmg);
            m_atkSpeedLabel = root.Q<Label>(k_AtkSpeed);
            m_physDefLabel = root.Q<Label>(k_PhysDef);
            m_magicDefLabel = root.Q<Label>(k_MagicDef);
            m_evasionLabel = root.Q<Label>(k_Evasion);
            m_hitRateLabel = root.Q<Label>(k_HitRate);
            m_critRateLabel = root.Q<Label>(k_CritRate);
            m_critDmgLabel = root.Q<Label>(k_CritDmg);
            m_armorPenLabel = root.Q<Label>(k_ArmorPen);
            m_magicPenLabel = root.Q<Label>(k_MagicPen);
            m_moveSpeedLabel = root.Q<Label>(k_MoveSpeed);
            m_castSpeedLabel = root.Q<Label>(k_CastSpeed);
            m_cdrLabel = root.Q<Label>(k_Cdr);
            m_atkRangeLabel = root.Q<Label>(k_AtkRange);
            m_visionRangeLabel = root.Q<Label>(k_VisionRange);
            m_goldLabel = root.Q<Label>(k_Gold);
            m_expLabel = root.Q<Label>(k_Exp);
        }

        /// <summary>
        /// 查找目标角色：优先使用 Inspector 指定的引用，
        /// 否则在场景中查找玩家阵营的 ObjectBase
        /// </summary>
        private void ResolveTarget()
        {
            if (m_target != null)
            {
                m_targetResolved = true;
                return;
            }

            ObjectBase[] candidates = FindObjectsOfType<ObjectBase>();
            if (candidates == null || candidates.Length == 0)
            {
                return;
            }

            // 优先查找玩家阵营（FactionID 1-10）的角色
            foreach (ObjectBase obj in candidates)
            {
                if (obj == null || !obj.HasObjectStats())
                {
                    continue;
                }

                ObjectStatsConfig stats = obj.GetObjectStats();
                if (stats.FactionID >= k_playerFactionMinID && stats.FactionID <= k_playerFactionMaxID)
                {
                    m_target = obj;
                    m_targetResolved = true;
                    return;
                }
            }

            // 回退：使用第一个找到的 ObjectBase
            m_target = candidates[0];
            m_targetResolved = true;
        }

        /// <summary>
        /// 更新面板所有属性显示
        /// </summary>
        private void UpdateAllAttributes()
        {
            if (m_target == null || !m_target.HasObjectStats())
            {
                return;
            }

            ObjectStatsConfig stats = m_target.GetObjectStats();
            if (stats == null)
            {
                return;
            }

            // 标题与等级
            if (m_titleLabel != null)
            {
                m_titleLabel.text = GetTypeName(stats.Type);
            }

            if (m_levelLabel != null)
            {
                m_levelLabel.text = $"Lv.{stats.Level}";
            }

            // 资源
            if (m_hpLabel != null)
            {
                m_hpLabel.text = $"{stats.CurrentHealth:F0}/{stats.MaxHealth:F0}";
            }

            if (m_mpLabel != null)
            {
                m_mpLabel.text = $"{stats.CurrentMana:F0}/{stats.MaxMana:F0}";
            }

            if (m_hpRegenLabel != null)
            {
                m_hpRegenLabel.text = $"{stats.HealthRegeneration:F1}";
            }

            if (m_mpRegenLabel != null)
            {
                m_mpRegenLabel.text = $"{stats.ManaRegeneration:F1}";
            }

            // 攻击
            if (m_physAtkLabel != null)
            {
                m_physAtkLabel.text = $"{stats.PhysicalAttack:F0}";
            }

            if (m_magicAtkLabel != null)
            {
                m_magicAtkLabel.text = $"{stats.MagicAttack:F0}";
            }

            if (m_trueDmgLabel != null)
            {
                m_trueDmgLabel.text = $"{stats.TrueDamage:F0}";
            }

            if (m_atkSpeedLabel != null)
            {
                m_atkSpeedLabel.text = $"{stats.AttackSpeed:F1}";
            }

            // 防御
            if (m_physDefLabel != null)
            {
                m_physDefLabel.text = $"{stats.PhysicalDefense:F0}";
            }

            if (m_magicDefLabel != null)
            {
                m_magicDefLabel.text = $"{stats.MagicDefense:F0}";
            }

            if (m_evasionLabel != null)
            {
                m_evasionLabel.text = $"{stats.EvasionRate * 100f:F0}%";
            }

            if (m_hitRateLabel != null)
            {
                m_hitRateLabel.text = $"{stats.HitRate * 100f:F0}%";
            }

            // 暴击
            if (m_critRateLabel != null)
            {
                m_critRateLabel.text = $"{stats.CriticalRate * 100f:F0}%";
            }

            if (m_critDmgLabel != null)
            {
                m_critDmgLabel.text = $"{stats.CriticalDamageMultiplier * 100f:F0}%";
            }

            // 穿透
            if (m_armorPenLabel != null)
            {
                m_armorPenLabel.text = $"{stats.ArmorPenetration * 100f:F0}%";
            }

            if (m_magicPenLabel != null)
            {
                m_magicPenLabel.text = $"{stats.MagicPenetration * 100f:F0}%";
            }

            // 其他
            if (m_moveSpeedLabel != null)
            {
                m_moveSpeedLabel.text = $"{stats.MoveSpeed:F1}";
            }

            if (m_castSpeedLabel != null)
            {
                m_castSpeedLabel.text = $"{stats.CastSpeed:F1}";
            }

            if (m_cdrLabel != null)
            {
                m_cdrLabel.text = $"{stats.CooldownReduction * 100f:F0}%";
            }

            if (m_atkRangeLabel != null)
            {
                m_atkRangeLabel.text = $"{stats.AttackRange:F0}";
            }

            if (m_visionRangeLabel != null)
            {
                m_visionRangeLabel.text = $"{stats.VisionRange:F0}";
            }

            // 进度
            if (m_goldLabel != null)
            {
                m_goldLabel.text = $"{stats.Gold}";
            }

            if (m_expLabel != null)
            {
                m_expLabel.text = $"{stats.Experience:F0}";
            }
        }

        /// <summary>
        /// 将 ObjectType 转换为中文名称
        /// </summary>
        private static string GetTypeName(ObjectType type)
        {
            switch (type)
            {
                case ObjectType.Warrior: return "战士";
                case ObjectType.Mage: return "法师";
                case ObjectType.Assassin: return "刺客";
                case ObjectType.Tank: return "坦克";
                case ObjectType.Marksman: return "射手";
                case ObjectType.Support: return "辅助";
                case ObjectType.Projectile: return "投射物";
                case ObjectType.Trap: return "陷阱";
                case ObjectType.Building: return "建筑";
                case ObjectType.Neutral: return "中立";
                case ObjectType.Weapon: return "武器";
                default: return "未知";
            }
        }
    }
}
