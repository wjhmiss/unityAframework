using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using AFrameWork.Core;

namespace AFrameWork.GameUI
{
    /// <summary>
    /// 伤害计算面板控制器 - 在屏幕右上角展示最后一次 CalculateAttack 调用的完整信息。
    ///
    /// 数据来源：ObjectStatsConfig.LastAttackRecord（由 CalculateAttack 在每次调用时自动记录）。
    /// 这样能真实反映多攻击方场景（如 Fighter + Sword → Monster，或 Bullet 共享属性 + Owner → Target），
    /// 而非面板自行猜测攻击者/目标。
    ///
    /// 展示内容：
    ///   1. 水平排列所有攻击方卡片（蓝边）→ 目标卡片（红边），用 "+" 和 "→" 分隔
    ///   2. 每张卡片列出该对象的伤害相关参数（从快照读取，非活引用）
    ///   3. 底部公式区用真实数字展示各步骤推导（与 CalculateAttack 内部计算完全一致）
    ///
    /// 交互：
    ///   - Alt+2 切换显隐（避免与 Unity Editor 的 Ctrl+1/Ctrl+2 冲突）
    ///   - 关闭按钮隐藏面板
    ///   - 卡片悬停显示完整属性 Tooltip
    ///   - 仅在 LastAttackRecord 时间戳变化时重建 UI（避免每帧重建）
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AttackCalcPanelController : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════════════
        // 常量定义（BEM 命名规范，与 UXML/USS 元素对应）
        // ══════════════════════════════════════════════════════════════════════════

        private const string k_PanelName = "attack-calc-panel";
        private const string k_Title = "attack-calc-panel__title";
        private const string k_CloseBtn = "attack-calc-panel__close-btn";
        private const string k_Body = "attack-calc-panel__body";
        private const string k_CardsRow = "attack-calc-panel__cards-row";
        private const string k_EmptyHint = "attack-calc-panel__empty-hint";
        private const string k_Formula = "attack-calc-panel__formula";
        private const string k_FormulaSteps = "attack-calc-panel__formula-steps";

        // BEM 类名常量（用于动态创建元素时附加 class）
        private const string k_ClassArrow = "attack-calc-panel__arrow";
        private const string k_ClassObjectCard = "attack-calc-panel__object-card";
        private const string k_ClassObjectCardAttacker = "attack-calc-panel__object-card--attacker";
        private const string k_ClassObjectCardTarget = "attack-calc-panel__object-card--target";
        private const string k_ClassObjectCardShared = "attack-calc-panel__object-card--shared";
        private const string k_ClassObjectHeader = "attack-calc-panel__object-header";
        private const string k_ClassObjectName = "attack-calc-panel__object-name";
        private const string k_ClassObjectRole = "attack-calc-panel__object-role";
        private const string k_ClassParamList = "attack-calc-panel__param-list";
        private const string k_ClassParamRow = "attack-calc-panel__param-row";
        private const string k_ClassParamLabel = "attack-calc-panel__param-label";
        private const string k_ClassParamValue = "attack-calc-panel__param-value";
        private const string k_ClassParamValueMuted = "attack-calc-panel__param-value--muted";
        private const string k_ClassFormulaStep = "attack-calc-panel__formula-step";
        private const string k_ClassFormulaStepResult = "attack-calc-panel__formula-step--result";
        private const string k_ClassFormulaStepEvaded = "attack-calc-panel__formula-step--evaded";
        private const string k_ClassHidden = "attack-calc-panel--hidden";

        /// <summary>面板检查新攻击记录的间隔（秒），避免每帧轮询</summary>
        private const float k_updateInterval = 0.1f;

        // ══════════════════════════════════════════════════════════════════════════
        // 字段定义
        // ══════════════════════════════════════════════════════════════════════════

        [Tooltip("AttackCalcPanel UXML 资源")]
        [SerializeField]
        private VisualTreeAsset m_attackCalcPanelUxml;

        [Tooltip("AttackCalcPanel USS 样式资源")]
        [SerializeField]
        private StyleSheet m_attackCalcPanelUss;

        // 缓存的 UI 元素引用
        private VisualElement m_panelRoot;
        private Label m_titleLabel;
        private Button m_closeButton;
        private ScrollView m_bodyScrollView;
        private VisualElement m_cardsRow;
        private Label m_emptyHintLabel;
        private VisualElement m_formulaContainer;
        private VisualElement m_formulaStepsContainer;

        // 运行时状态
        private float m_lastUpdateTime;
        private bool m_isVisible = true;
        // 记录上次展示的 AttackRecord 时间戳，避免相同记录重复重建 UI
        private float m_lastDisplayedTimestamp = -1f;

        // 复用的 StringBuilder（避免每次更新分配新字符串）
        private static readonly StringBuilder s_textBuilder = new StringBuilder(512);

        // 卡片实时数据缓存：重建卡片时填充，Update 节流时从中读取活引用刷新生命值/魔法值
        private List<CardLiveData> m_attackerCardData;
        private CardLiveData m_targetCardData;

        /// <summary>
        /// 卡片实时数据：保存 ObjectBase 活引用 + HP/MP Label，用于在不重建卡片时更新生命值/魔法值。
        ///
        /// 实时刷新策略：
        ///   - ObjectBase（如 Fighter/Monster）：生命周期长，生命值/魔法值实时变化，需要刷新（ShouldRefreshLive=true）
        ///   - SimpleObjectBase（如 Bullet/Fire）：生命周期短，命中后立即销毁，生命值/魔法值意义不大，跳过刷新（ShouldRefreshLive=false）
        ///   - 无活引用（null）：回退到快照值（如子弹共享属性无 ObjectBase 实例）
        ///
        /// 活引用不可用（null 或已销毁）时回退到快照值。
        /// </summary>
        private struct CardLiveData
        {
            /// <summary>是否需要实时刷新（ObjectBase 为 true，SimpleObjectBase 或 null 为 false）</summary>
            public bool ShouldRefreshLive;
            /// <summary>ObjectBase 活引用（可能为 null 或已销毁）</summary>
            public ObjectBase LiveRef;
            /// <summary>生命值 Label 引用（用于实时更新）</summary>
            public Label HpLabel;
            /// <summary>魔法值 Label 引用（用于实时更新）</summary>
            public Label MpLabel;
            /// <summary>攻击时的属性快照（当 LiveRef 不可用时作为回退数据源）</summary>
            public ObjectStatsConfig Snapshot;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 属性
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>场景中的单例引用</summary>
        public static AttackCalcPanelController Instance { get; private set; }

        /// <summary>面板当前是否可见</summary>
        public bool IsVisible => m_isVisible;

        // ══════════════════════════════════════════════════════════════════════════
        // MonoBehaviour 方法（按 Unity Script Execution Order 排列）
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
            if (m_attackCalcPanelUss != null)
            {
                root.styleSheets.Add(m_attackCalcPanelUss);
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[{GetType().Name}] Attack Calc Panel Uss resource is null! Please configure it in Inspector.", this);
#endif
            }

            // 清除可能已存在的同名面板（避免重复实例化导致重影）
            root.Query<VisualElement>(name: k_PanelName).ForEach(e => e.RemoveFromHierarchy());

            // 实例化 UXML 并添加到 root
            if (m_attackCalcPanelUxml != null)
            {
                var tree = m_attackCalcPanelUxml.Instantiate();
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
                Debug.LogWarning($"[{GetType().Name}] Attack Calc Panel Uxml resource is null! Please configure it in Inspector.", this);
#endif
                return;
            }

            CacheElements(root);

            // 首次显示空状态
            ShowEmptyState();
        }

        private void OnEnable()
        {
            if (m_closeButton != null)
            {
                m_closeButton.clicked += HandleCloseButtonClicked;
            }
        }

        private void Start()
        {
            // 尝试展示当前 LastAttackRecord（若有）
            TryRefreshFromLastRecord();
        }

        private void Update()
        {
            // Alt+2 切换显隐（避免与 Unity Editor 的 Ctrl+1/Ctrl+2 窗口切换冲突）
            HandleToggleShortcut();

            // 面板不可见时跳过更新
            if (!m_isVisible) return;

            // 节流检查，避免每帧轮询 LastAttackRecord
            if (Time.time - m_lastUpdateTime < k_updateInterval)
            {
                return;
            }
            m_lastUpdateTime = Time.time;

            // 先检查是否有新攻击记录（可能重建卡片，重建内部会调用 RefreshLiveData）
            TryRefreshFromLastRecord();
            // 再刷新实时数据（即使无新攻击，生命值/魔法值也可能因其他伤害/恢复而变化）
            RefreshLiveData();
        }

        private void OnDisable()
        {
            if (m_closeButton != null)
            {
                m_closeButton.clicked -= HandleCloseButtonClicked;
            }
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

        /// <summary>显示面板</summary>
        public void Show()
        {
            if (m_panelRoot == null) return;
            m_isVisible = true;
            m_panelRoot.RemoveFromClassList(k_ClassHidden);
            m_lastUpdateTime = 0f; // 强制下一帧立即检查
        }

        /// <summary>隐藏面板</summary>
        public void Hide()
        {
            if (m_panelRoot == null) return;
            m_isVisible = false;
            m_panelRoot.AddToClassList(k_ClassHidden);
        }

        /// <summary>切换面板显隐</summary>
        public void Toggle()
        {
            if (m_isVisible) Hide();
            else Show();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - UI 初始化与缓存
        // ══════════════════════════════════════════════════════════════════════════

        private void CacheElements(VisualElement root)
        {
            m_panelRoot = root.Q<VisualElement>(k_PanelName);
            m_titleLabel = root.Q<Label>(k_Title);
            m_closeButton = root.Q<Button>(k_CloseBtn);
            m_bodyScrollView = root.Q<ScrollView>(k_Body);
            m_cardsRow = root.Q<VisualElement>(k_CardsRow);
            m_emptyHintLabel = root.Q<Label>(k_EmptyHint);
            m_formulaContainer = root.Q<VisualElement>(k_Formula);
            m_formulaStepsContainer = root.Q<VisualElement>(k_FormulaSteps);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - 交互处理
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>Alt+2 快捷键切换显隐</summary>
        private void HandleToggleShortcut()
        {
            bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (altPressed && Input.GetKeyDown(KeyCode.Alpha2))
            {
                Toggle();
            }
        }

        /// <summary>关闭按钮点击回调</summary>
        private void HandleCloseButtonClicked()
        {
            Hide();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - 从 LastAttackRecord 刷新面板
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 检查 ObjectStatsConfig.LastAttackRecord 是否有新记录，若有则刷新面板。
        /// 通过时间戳去重，避免相同记录重复重建 UI。
        /// </summary>
        private void TryRefreshFromLastRecord()
        {
            ObjectStatsConfig.AttackRecord record = ObjectStatsConfig.LastAttackRecord;
            if (!record.IsValid)
            {
                ShowEmptyState();
                return;
            }

            // 时间戳未变化，不重建
            if (record.Timestamp == m_lastDisplayedTimestamp)
            {
                return;
            }

            m_lastDisplayedTimestamp = record.Timestamp;
            RefreshPanel(record);
        }

        /// <summary>展示空状态（无攻击记录）</summary>
        private void ShowEmptyState()
        {
            if (m_emptyHintLabel != null)
            {
                m_emptyHintLabel.style.display = DisplayStyle.Flex;
            }
            if (m_bodyScrollView != null)
            {
                m_bodyScrollView.style.display = DisplayStyle.None;
            }
            if (m_formulaContainer != null)
            {
                m_formulaContainer.style.display = DisplayStyle.None;
            }
            // 清空实时数据缓存，避免 RefreshLiveData 更新已失效的 Label 引用
            m_attackerCardData?.Clear();
            m_targetCardData = default;
            if (m_titleLabel != null)
            {
                m_titleLabel.text = "伤害计算面板（等待攻击...）";
            }
        }

        /// <summary>刷新整个面板：更新标题、卡片列表、公式说明</summary>
        private void RefreshPanel(ObjectStatsConfig.AttackRecord record)
        {
            if (m_panelRoot == null) return;

            // 切换显示状态
            if (m_emptyHintLabel != null) m_emptyHintLabel.style.display = DisplayStyle.None;
            if (m_bodyScrollView != null) m_bodyScrollView.style.display = DisplayStyle.Flex;
            if (m_formulaContainer != null) m_formulaContainer.style.display = DisplayStyle.Flex;

            // 标题：攻击方们 → 目标
            string targetName = record.TargetSnapshot != null
                ? GetTypeName(record.TargetSnapshot.Type)
                : "目标";
            if (m_titleLabel != null)
            {
                m_titleLabel.text = $"最后一次攻击 → {targetName}";
            }

            RebuildCards(record);
            RebuildFormulaSteps(record);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - 卡片构建
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 重建卡片列表：[攻击方1] + [攻击方2] + ... → [目标]
        /// 攻击方之间用 "+" 表示属性累加，最后用 "→" 指向目标。
        /// 同时收集每张卡片的 CardLiveData，供 Update 节流时实时刷新生命值/魔法值。
        /// </summary>
        private void RebuildCards(ObjectStatsConfig.AttackRecord record)
        {
            if (m_cardsRow == null) return;
            m_cardsRow.Clear();

            // 初始化/清空攻击方实时数据缓存
            if (m_attackerCardData == null)
            {
                m_attackerCardData = new List<CardLiveData>(4);
            }
            else
            {
                m_attackerCardData.Clear();
            }

            // 攻击方卡片
            if (record.AttackerSnapshots != null && record.AttackerSnapshots.Length > 0)
            {
                for (int i = 0; i < record.AttackerSnapshots.Length; i++)
                {
                    ObjectStatsConfig attacker = record.AttackerSnapshots[i];
                    if (attacker == null) continue;

                    // 攻击方之间用 "+" 分隔（非首张卡片前加 "+"）
                    if (i > 0)
                    {
                        m_cardsRow.Add(BuildArrowElement("+"));
                    }

                    string role = record.AttackerSnapshots.Length > 1
                        ? $"攻击方 {i + 1}"
                        : "攻击方";

                    // 从 AttackerRefs 取活引用（可能为 null，如子弹共享属性无 ObjectBase）
                    ObjectBase liveRef = (record.AttackerRefs != null && i < record.AttackerRefs.Length)
                        ? record.AttackerRefs[i]
                        : null;

                    m_cardsRow.Add(BuildAttackerCard(attacker, role, liveRef, out CardLiveData data));
                    m_attackerCardData.Add(data);
                }
            }

            // 箭头指向目标
            m_cardsRow.Add(BuildArrowElement("→"));

            // 目标卡片
            if (record.TargetSnapshot != null)
            {
                m_cardsRow.Add(BuildTargetCard(record.TargetSnapshot, record.TargetRef, out m_targetCardData));
            }
            else
            {
                m_targetCardData = default;
            }

            // 重建后立即刷新一次实时数据
            RefreshLiveData();
        }

        /// <summary>构建攻击方卡片（蓝边），通过 out 返回实时数据引用</summary>
        private VisualElement BuildAttackerCard(ObjectStatsConfig stats, string role, ObjectBase liveRef, out CardLiveData data)
        {
            VisualElement card = BuildObjectCardBase(k_ClassObjectCardAttacker);
            FillCardHeader(card, GetTypeName(stats.Type), role);
            FillAttackerCardParams(card, stats, out Label hpLabel, out Label mpLabel);
            card.tooltip = BuildCardTooltip(stats, isAttacker: true);

            // ObjectBase（如 Fighter/Monster）需要实时刷新；SimpleObjectBase（如 Bullet）生命周期短，跳过刷新
            // SimpleObjectBase 不继承 ObjectBase，所以 liveRef 为 null 时即为 SimpleObjectBase 或共享属性
            bool shouldRefreshLive = (liveRef != null) && (liveRef is ObjectBase);

            data = new CardLiveData
            {
                ShouldRefreshLive = shouldRefreshLive,
                LiveRef = liveRef,
                HpLabel = hpLabel,
                MpLabel = mpLabel,
                Snapshot = stats,
            };
            return card;
        }

        /// <summary>构建目标卡片（红边），通过 out 返回实时数据引用</summary>
        private VisualElement BuildTargetCard(ObjectStatsConfig stats, ObjectBase liveRef, out CardLiveData data)
        {
            VisualElement card = BuildObjectCardBase(k_ClassObjectCardTarget);
            FillCardHeader(card, GetTypeName(stats.Type), "目标");
            FillTargetCardParams(card, stats, out Label hpLabel, out Label mpLabel);
            card.tooltip = BuildCardTooltip(stats, isAttacker: false);

            // 目标通常是 ObjectBase（如 Monster），需要实时刷新生命值/魔法值
            bool shouldRefreshLive = (liveRef != null) && (liveRef is ObjectBase);

            data = new CardLiveData
            {
                ShouldRefreshLive = shouldRefreshLive,
                LiveRef = liveRef,
                HpLabel = hpLabel,
                MpLabel = mpLabel,
                Snapshot = stats,
            };
            return card;
        }

        /// <summary>构建卡片基础容器</summary>
        private VisualElement BuildObjectCardBase(string modifierClass)
        {
            var card = new VisualElement();
            card.AddToClassList(k_ClassObjectCard);
            card.AddToClassList(modifierClass);
            return card;
        }

        /// <summary>填充卡片头部（名称 + 角色）</summary>
        private void FillCardHeader(VisualElement card, string objectName, string role)
        {
            var header = new VisualElement();
            header.AddToClassList(k_ClassObjectHeader);

            var nameLabel = new Label(objectName);
            nameLabel.AddToClassList(k_ClassObjectName);

            var roleLabel = new Label(role);
            roleLabel.AddToClassList(k_ClassObjectRole);

            header.Add(nameLabel);
            header.Add(roleLabel);
            card.Add(header);
        }

        /// <summary>填充攻击方卡片参数（攻击相关属性 + 实时生命值/魔法值）</summary>
        private void FillAttackerCardParams(VisualElement card, ObjectStatsConfig stats, out Label hpLabel, out Label mpLabel)
        {
            var paramList = new VisualElement();
            paramList.AddToClassList(k_ClassParamList);

            AddParamRow(paramList, "物理攻击", $"{stats.PhysicalAttack:F0}");
            AddParamRow(paramList, "魔法攻击", $"{stats.MagicAttack:F0}");
            AddParamRow(paramList, "真实伤害", $"{stats.TrueDamage:F0}");
            AddParamRow(paramList, "命中率", $"{stats.HitRate * 100f:F0}%");
            AddParamRow(paramList, "暴击率", $"{stats.CriticalRate * 100f:F0}%");
            AddParamRow(paramList, "暴击伤害", $"{stats.CriticalDamageMultiplier * 100f:F0}%");
            AddParamRow(paramList, "护甲穿透", $"{stats.ArmorPenetration * 100f:F0}%");
            AddParamRow(paramList, "魔法穿透", $"{stats.MagicPenetration * 100f:F0}%");

            // 实时生命值/魔法值（初始值用快照，后续由 RefreshLiveData 从活引用更新）
            hpLabel = AddParamRow(paramList, "当前生命", $"{stats.CurrentHealth:F0}/{stats.MaxHealth:F0}");
            mpLabel = AddParamRow(paramList, "当前魔法", $"{stats.CurrentMana:F0}/{stats.MaxMana:F0}");

            card.Add(paramList);
        }

        /// <summary>填充目标卡片参数（防御相关属性 + 实时生命值/魔法值）</summary>
        private void FillTargetCardParams(VisualElement card, ObjectStatsConfig stats, out Label hpLabel, out Label mpLabel)
        {
            var paramList = new VisualElement();
            paramList.AddToClassList(k_ClassParamList);

            AddParamRow(paramList, "物理防御", $"{stats.PhysicalDefense:F0}");
            AddParamRow(paramList, "魔法防御", $"{stats.MagicDefense:F0}");
            AddParamRow(paramList, "闪避率", $"{stats.EvasionRate * 100f:F0}%");

            // 实时生命值/魔法值（初始值用快照，后续由 RefreshLiveData 从活引用更新）
            hpLabel = AddParamRow(paramList, "当前生命", $"{stats.CurrentHealth:F0}/{stats.MaxHealth:F0}");
            mpLabel = AddParamRow(paramList, "当前魔法", $"{stats.CurrentMana:F0}/{stats.MaxMana:F0}");

            card.Add(paramList);
        }

        /// <summary>添加一行参数（label + value），返回 value Label 供后续动态更新</summary>
        private Label AddParamRow(VisualElement parent, string label, string value, bool isMuted = false)
        {
            var row = new VisualElement();
            row.AddToClassList(k_ClassParamRow);

            var labelElement = new Label(label);
            labelElement.AddToClassList(k_ClassParamLabel);

            var valueElement = new Label(value);
            valueElement.AddToClassList(k_ClassParamValue);
            if (isMuted)
            {
                valueElement.AddToClassList(k_ClassParamValueMuted);
            }

            row.Add(labelElement);
            row.Add(valueElement);
            parent.Add(row);
            return valueElement;
        }

        /// <summary>构建箭头分隔符元素</summary>
        private VisualElement BuildArrowElement(string arrowText)
        {
            var arrow = new Label(arrowText);
            arrow.AddToClassList(k_ClassArrow);
            return arrow;
        }

        /// <summary>构建卡片悬停 Tooltip 文本</summary>
        private string BuildCardTooltip(ObjectStatsConfig stats, bool isAttacker)
        {
            s_textBuilder.Clear();
            s_textBuilder.Append(GetTypeName(stats.Type));
            s_textBuilder.Append(" (Lv.").Append(stats.Level).Append(")\n");
            s_textBuilder.Append("阵营 ID: ").Append(stats.FactionID).Append("\n");
            s_textBuilder.Append("生命: ").Append(stats.CurrentHealth).Append("/").Append(stats.MaxHealth).Append("\n");
            s_textBuilder.Append("物理攻击: ").Append(stats.PhysicalAttack).Append("\n");
            s_textBuilder.Append("魔法攻击: ").Append(stats.MagicAttack).Append("\n");
            s_textBuilder.Append("真实伤害: ").Append(stats.TrueDamage).Append("\n");
            s_textBuilder.Append("物理防御: ").Append(stats.PhysicalDefense).Append("\n");
            s_textBuilder.Append("魔法防御: ").Append(stats.MagicDefense).Append("\n");
            s_textBuilder.Append("命中率: ").Append(stats.HitRate * 100f).Append("%\n");
            s_textBuilder.Append("闪避率: ").Append(stats.EvasionRate * 100f).Append("%\n");
            s_textBuilder.Append("暴击率: ").Append(stats.CriticalRate * 100f).Append("%\n");
            s_textBuilder.Append("暴击伤害: ").Append(stats.CriticalDamageMultiplier * 100f).Append("%\n");
            s_textBuilder.Append("护甲穿透: ").Append(stats.ArmorPenetration * 100f).Append("%\n");
            s_textBuilder.Append("魔法穿透: ").Append(stats.MagicPenetration * 100f).Append("%");

            return s_textBuilder.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - 实时数据刷新（生命值/魔法值）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 从 ObjectBase 活引用实时刷新所有卡片的当前生命值/魔法值。
        /// 活引用不可用（null 或已销毁）时回退到攻击时快照值。
        /// 由 Update 节流调用（每 k_updateInterval 秒一次），RebuildCards 后也会立即调用一次。
        /// </summary>
        private void RefreshLiveData()
        {
            // 刷新目标卡片
            RefreshCardLiveData(ref m_targetCardData);

            // 刷新攻击方卡片
            if (m_attackerCardData != null)
            {
                for (int i = 0; i < m_attackerCardData.Count; i++)
                {
                    CardLiveData data = m_attackerCardData[i];
                    RefreshCardLiveData(ref data);
                    m_attackerCardData[i] = data; // struct 值类型，写回
                }
            }
        }

        /// <summary>
        /// 刷新单张卡片的生命值/魔法值。
        /// 根据 ShouldRefreshLive 标志位决定是否尝试实时刷新：
        ///   - ShouldRefreshLive=true：从 ObjectBase 活引用读取实时数据（如 Fighter/Monster）
        ///   - ShouldRefreshLive=false：直接使用快照值，跳过实时刷新（如 SimpleObjectBase 的子弹）
        /// </summary>
        private void RefreshCardLiveData(ref CardLiveData card)
        {
            if (card.HpLabel == null || card.MpLabel == null) return;

            // 跳过不需要实时刷新的卡片（SimpleObjectBase 或无活引用）
            if (!card.ShouldRefreshLive)
            {
                // 直接使用快照值（已由 RebuildCards 填充，无需再刷新）
                return;
            }

            // 从 ObjectBase 活引用读取实时数据
            // 注意：Unity 销毁的对象与 null 比较返回 true（fake-null），可安全检测
            ObjectStatsConfig stats = null;
            if (card.LiveRef != null && card.LiveRef.HasObjectStats())
            {
                stats = card.LiveRef.GetObjectStats();
            }
            else if (card.Snapshot != null)
            {
                // 活引用不可用（null 或已销毁），回退到攻击时快照
                stats = card.Snapshot;
            }

            if (stats == null) return;

            // 使用 StringBuilder 复用，避免每次分配新字符串
            s_textBuilder.Clear();
            s_textBuilder.Append(stats.CurrentHealth.ToString("F0"));
            s_textBuilder.Append('/').Append(stats.MaxHealth.ToString("F0"));
            card.HpLabel.text = s_textBuilder.ToString();

            s_textBuilder.Clear();
            s_textBuilder.Append(stats.CurrentMana.ToString("F0"));
            s_textBuilder.Append('/').Append(stats.MaxMana.ToString("F0"));
            card.MpLabel.text = s_textBuilder.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - 公式步骤构建（使用 AttackRecord 中的真实数字）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 重建公式步骤区域。使用 AttackRecord 中的真实数字展示各步骤，
        /// 与 ObjectStatsConfig.CalculateAttack 内部计算完全一致。
        /// 步骤编号用圆圈数字 ①②③④⑤⑥，动态编号（有倍率时多一步）。
        /// </summary>
        private void RebuildFormulaSteps(ObjectStatsConfig.AttackRecord record)
        {
            if (m_formulaStepsContainer == null) return;
            m_formulaStepsContainer.Clear();

            // 圆圈数字（按步骤序号 1-based 索引）
            // 步骤序号动态计算：①累加 → [②倍率] → ③闪避 → ④防御 → ⑤暴击
            int stepNum = 1;

            // ─── 步骤 ①：累加攻击方属性（展示每个攻击方的贡献） ───
            s_textBuilder.Clear();
            s_textBuilder.Append(GetStepCircle(stepNum++)).Append(" 累加攻击方属性：\n");
            if (record.AttackerSnapshots != null)
            {
                for (int i = 0; i < record.AttackerSnapshots.Length; i++)
                {
                    ObjectStatsConfig a = record.AttackerSnapshots[i];
                    if (a == null) continue;
                    s_textBuilder.Append("   [").Append(GetTypeName(a.Type)).Append("] ");
                    s_textBuilder.Append("物攻 ").Append(a.PhysicalAttack);
                    s_textBuilder.Append(" + 魔攻 ").Append(a.MagicAttack);
                    s_textBuilder.Append(" + 真伤 ").Append(a.TrueDamage);
                    s_textBuilder.Append(" (命中 ").Append(a.HitRate * 100f).Append("%");
                    s_textBuilder.Append(", 暴击 ").Append(a.CriticalRate * 100f).Append("%");
                    s_textBuilder.Append(", 暴伤 ").Append(a.CriticalDamageMultiplier * 100f).Append("%");
                    s_textBuilder.Append(", 穿甲 ").Append(a.ArmorPenetration * 100f).Append("%");
                    s_textBuilder.Append(", 穿魔 ").Append(a.MagicPenetration * 100f).Append("%)\n");
                }
            }
            s_textBuilder.Append("   合计: 物攻 ").Append(record.SumPhysicalAttack);
            s_textBuilder.Append(" + 魔攻 ").Append(record.SumMagicAttack);
            s_textBuilder.Append(" + 真伤 ").Append(record.SumTrueDamage);
            s_textBuilder.Append("\n   命中 ").Append(record.SumHitRate * 100f).Append("%");
            s_textBuilder.Append(", 暴击 ").Append(record.SumCriticalRate * 100f).Append("%");
            s_textBuilder.Append(", 暴伤 ").Append(record.SumCriticalDamageMultiplier * 100f).Append("%");
            s_textBuilder.Append(", 穿甲 ").Append(record.SumArmorPenetration * 100f).Append("%");
            s_textBuilder.Append(", 穿魔 ").Append(record.SumMagicPenetration * 100f).Append("%");
            AddFormulaStep(s_textBuilder.ToString());

            // ─── 步骤 ②：应用倍率（仅当倍率非 identity 时展示） ───
            if (HasNonIdentityMultiplier(record.Multiplier))
            {
                s_textBuilder.Clear();
                s_textBuilder.Append(GetStepCircle(stepNum++)).Append(" 应用攻击参数倍率：\n");
                s_textBuilder.Append("   物攻 ").Append(record.SumPhysicalAttack).Append(" → ").Append(record.EffectivePhysAtk).Append("\n");
                s_textBuilder.Append("   魔攻 ").Append(record.SumMagicAttack).Append(" → ").Append(record.EffectiveMagicAtk).Append("\n");
                s_textBuilder.Append("   真伤 ").Append(record.SumTrueDamage).Append(" → ").Append(record.EffectiveTrueDmg).Append("\n");
                s_textBuilder.Append("   暴击 ").Append(record.SumCriticalRate * 100f).Append("% → ").Append(record.EffectiveCritRate * 100f).Append("%\n");
                s_textBuilder.Append("   暴伤 ").Append(record.SumCriticalDamageMultiplier * 100f).Append("% → ").Append(record.EffectiveCritDmg * 100f).Append("%");
                AddFormulaStep(s_textBuilder.ToString());
            }

            // ─── 步骤 ③：闪避判定 ───
            s_textBuilder.Clear();
            s_textBuilder.Append(GetStepCircle(stepNum++)).Append(" 闪避判定：\n");
            s_textBuilder.Append("   有效闪避 = 闪避 ").Append(record.TargetSnapshot.EvasionRate * 100f).Append("%");
            s_textBuilder.Append(" - (命中 ").Append(record.SumHitRate * 100f).Append("% - 100%)");
            s_textBuilder.Append(" = ").Append(Mathf.Max(0f, record.EffectiveEvasion) * 100f).Append("%\n");
            if (record.IsEvaded)
            {
                s_textBuilder.Append("   ⚠ 本次攻击被闪避！伤害 = 0");
                AddFormulaStep(s_textBuilder.ToString(), k_ClassFormulaStepEvaded);
                AddFormulaStep("最终伤害 = 0（被闪避）", k_ClassFormulaStepResult);
                return;
            }
            if (record.EffectiveEvasion <= 0f)
            {
                s_textBuilder.Append("   ✓ 有效闪避 ≤ 0，必命中");
            }
            else
            {
                s_textBuilder.Append("   本次：命中（闪避概率 ").Append(record.EffectiveEvasion * 100f).Append("% 未触发）");
            }
            AddFormulaStep(s_textBuilder.ToString());

            // ─── 步骤 ④：防御减免 ───
            s_textBuilder.Clear();
            s_textBuilder.Append(GetStepCircle(stepNum++)).Append(" 防御减免：\n");
            if (record.PhysicalDamage > 0f)
            {
                s_textBuilder.Append("   物理: ").Append(record.EffectivePhysAtk);
                s_textBuilder.Append(" × 100/(100+").Append(record.TargetSnapshot.PhysicalDefense);
                s_textBuilder.Append("×(1-").Append(record.EffectiveArmorPen * 100f).Append("%))");
                s_textBuilder.Append(" = ").Append(record.PhysicalDamage).Append("\n");
            }
            if (record.MagicDamage > 0f)
            {
                s_textBuilder.Append("   魔法: ").Append(record.EffectiveMagicAtk);
                s_textBuilder.Append(" × 100/(100+").Append(record.TargetSnapshot.MagicDefense);
                s_textBuilder.Append("×(1-").Append(record.EffectiveMagicPen * 100f).Append("%))");
                s_textBuilder.Append(" = ").Append(record.MagicDamage).Append("\n");
            }
            if (record.TrueDamageApplied > 0f)
            {
                s_textBuilder.Append("   真伤: ").Append(record.TrueDamageApplied).Append("（无视防御）\n");
            }
            s_textBuilder.Append("   基础伤害 = ").Append(record.BaseDamage);
            AddFormulaStep(s_textBuilder.ToString());

            // ─── 步骤 ⑤：暴击判定 ───
            s_textBuilder.Clear();
            s_textBuilder.Append(GetStepCircle(stepNum++)).Append(" 暴击判定：\n");
            s_textBuilder.Append("   暴击率 ").Append(record.EffectiveCritRate * 100f).Append("%");
            s_textBuilder.Append(", 暴击伤害 ×").Append(record.EffectiveCritDmg).Append("\n");
            if (record.IsCritical)
            {
                s_textBuilder.Append("   ✓ 触发暴击: ").Append(record.BaseDamage).Append(" × ").Append(record.EffectiveCritDmg);
                s_textBuilder.Append(" = ").Append(record.FinalDamage);
            }
            else
            {
                s_textBuilder.Append("   ✗ 未暴击: ").Append(record.FinalDamage).Append("（= 基础伤害）");
            }
            AddFormulaStep(s_textBuilder.ToString());

            // ─── 最终结果 ───
            s_textBuilder.Clear();
            s_textBuilder.Append("最终伤害 = ").Append(record.FinalDamage);
            if (record.IsCritical)
            {
                s_textBuilder.Append("（暴击）");
            }
            AddFormulaStep(s_textBuilder.ToString(), k_ClassFormulaStepResult);
        }

        /// <summary>获取步骤圆圈数字（1→①, 2→②, ..., 9→⑨）</summary>
        private static string GetStepCircle(int stepNum)
        {
            // 圆圈数字 Unicode: ①=U+2460, ②=U+2461, ...
            if (stepNum < 1 || stepNum > 9) return stepNum + ".";
            return ((char)(0x2460 + stepNum - 1)).ToString();
        }

        /// <summary>检查倍率是否非 identity（任一字段非 k_useBase）</summary>
        private static bool HasNonIdentityMultiplier(ObjectStatsConfigMultiplier m)
        {
            const float k_useBase = -1f;
            return m.PhysicalAttackMultiplier != k_useBase ||
                   m.MagicAttackMultiplier != k_useBase ||
                   m.TrueDamageMultiplier != k_useBase ||
                   m.CriticalRateMultiplier != k_useBase ||
                   m.CriticalDamageMultiplier != k_useBase ||
                   m.ArmorPenetrationMultiplier != k_useBase ||
                   m.MagicPenetrationMultiplier != k_useBase ||
                   m.HitRateMultiplier != k_useBase;
        }

        /// <summary>添加一个公式步骤 Label</summary>
        private void AddFormulaStep(string text, string modifierClass = null)
        {
            var step = new Label(text);
            step.AddToClassList(k_ClassFormulaStep);
            if (!string.IsNullOrEmpty(modifierClass))
            {
                step.AddToClassList(modifierClass);
            }
            m_formulaStepsContainer?.Add(step);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 私有方法 - 工具
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>获取 ObjectType 的中文名称</summary>
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
