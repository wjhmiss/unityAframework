using System.Collections.Generic;
using UnityEngine;

namespace AFrameWork.Core
{
    /// <summary>
    /// 阵营关系管理器，用于集中管理所有阵营之间的关系
    /// 纯静态类，无需挂载到场景对象，首次使用时自动初始化
    /// </summary>
    public static class FactionRelationManager
    {
        #region 阵营ID范围常量

        // 玩家阵营ID范围（1-10）
        private const int k_playerFactionMinID = 1;
        private const int k_playerFactionMaxID = 10;

        // 怪物阵营ID范围（11-50）
        private const int k_monsterFactionMinID = 11;
        private const int k_monsterFactionMaxID = 50;

        // NPC阵营ID范围（51-99）
        private const int k_npcFactionMinID = 51;
        private const int k_npcFactionMaxID = 99;

        #endregion

        // 阵营关系表（键：阵营ID对，值：关系类型）
        private static Dictionary<(int, int), FactionRelationType> s_factionRelations
            = new Dictionary<(int, int), FactionRelationType>();

        // 标记是否已初始化
        private static bool s_isInitialized = false;

        /// <summary>
        /// 静态构造函数，类首次使用时自动执行一次
        /// </summary>
        static FactionRelationManager()
        {
            InitializeDefaultFactionRelations();
        }

        #region 初始化方法

        /// <summary>
        /// 初始化默认阵营关系（示例配置）
        /// </summary>
        private static void InitializeDefaultFactionRelations()
        {
            if (s_isInitialized)
            {
                return;
            }
            s_isInitialized = true;

            // 玩家阵营之间：友好
            for (int i = k_playerFactionMinID; i <= k_playerFactionMaxID; i++)
            {
                for (int j = k_playerFactionMinID; j <= k_playerFactionMaxID; j++)
                {
                    SetFactionRelation(i, j, FactionRelationType.Friendly);
                }
            }

            // 玩家阵营对怪物阵营：敌对
            for (int i = k_playerFactionMinID; i <= k_playerFactionMaxID; i++)
            {
                for (int j = k_monsterFactionMinID; j <= k_monsterFactionMaxID; j++)
                {
                    SetFactionRelation(i, j, FactionRelationType.Hostile);
                    SetFactionRelation(j, i, FactionRelationType.Hostile);
                }
            }

            // NPC阵营对所有阵营：中立
            for (int i = k_npcFactionMinID; i <= k_npcFactionMaxID; i++)
            {
                for (int j = k_playerFactionMinID; j <= k_monsterFactionMaxID; j++)
                {
                    SetFactionRelation(i, j, FactionRelationType.Neutral);
                    SetFactionRelation(j, i, FactionRelationType.Neutral);
                }
            }

            // Debug.Log("阵营关系管理器已初始化，默认阵营关系已配置");
        }

        /// <summary>
        /// 重新初始化阵营关系（清空后重新加载默认配置）
        /// </summary>
        public static void Reinitialize()
        {
            s_isInitialized = false;
            s_factionRelations.Clear();
            InitializeDefaultFactionRelations();
        }

        #endregion

        #region 阵营关系设置方法

        /// <summary>
        /// 设置阵营关系（单向）
        /// </summary>
        /// <param name="factionA">阵营A的ID</param>
        /// <param name="factionB">阵营B的ID</param>
        /// <param name="relation">关系类型</param>
        public static void SetFactionRelation(int factionA, int factionB, FactionRelationType relation)
        {
            s_factionRelations[(factionA, factionB)] = relation;
        }

        /// <summary>
        /// 设置阵营关系（双向）
        /// </summary>
        /// <param name="factionA">阵营A的ID</param>
        /// <param name="factionB">阵营B的ID</param>
        /// <param name="relation">关系类型</param>
        public static void SetFactionRelationBidirectional(int factionA, int factionB, FactionRelationType relation)
        {
            s_factionRelations[(factionA, factionB)] = relation;
            s_factionRelations[(factionB, factionA)] = relation;
        }

        /// <summary>
        /// 获取阵营关系
        /// </summary>
        /// <param name="factionA">阵营A的ID</param>
        /// <param name="factionB">阵营B的ID</param>
        /// <returns>关系类型，如果未配置则返回中立</returns>
        public static FactionRelationType GetFactionRelation(int factionA, int factionB)
        {
            if (s_factionRelations.TryGetValue((factionA, factionB), out FactionRelationType relation))
            {
                return relation;
            }

            // 未配置的关系默认为中立
            return FactionRelationType.Neutral;
        }

        #endregion

        #region 批量配置方法

        /// <summary>
        /// 清空所有阵营关系
        /// </summary>
        public static void ClearAllRelations()
        {
            s_factionRelations.Clear();
        }

        /// <summary>
        /// 添加阵营同盟关系（多个阵营互为友好）
        /// </summary>
        /// <param name="factionIDs">阵营ID数组</param>
        public static void AddAlliance(List<int> factionIDs)
        {
            for (int i = 0; i < factionIDs.Count; i++)
            {
                for (int j = 0; j < factionIDs.Count; j++)
                {
                    if (i != j)
                    {
                        SetFactionRelation(factionIDs[i], factionIDs[j], FactionRelationType.Alliance);
                    }
                }
            }
        }

        /// <summary>
        /// 添加阵营敌对关系（多个阵营互为敌对）
        /// </summary>
        /// <param name="factionIDs">阵营ID数组</param>
        public static void AddHostile(List<int> factionIDs)
        {
            for (int i = 0; i < factionIDs.Count; i++)
            {
                for (int j = 0; j < factionIDs.Count; j++)
                {
                    if (i != j)
                    {
                        SetFactionRelation(factionIDs[i], factionIDs[j], FactionRelationType.Hostile);
                    }
                }
            }
        }

        /// <summary>
        /// 移除阵营关系
        /// </summary>
        /// <param name="factionA">阵营A的ID</param>
        /// <param name="factionB">阵营B的ID</param>
        public static void RemoveFactionRelation(int factionA, int factionB)
        {
            s_factionRelations.Remove((factionA, factionB));
        }

        #endregion

        #region 配置文件支持（可选）

        /// <summary>
        /// 从配置文件加载阵营关系（示例）
        /// 实际项目中可以使用 ScriptableObject 或 JSON 文件
        /// </summary>
        /// <param name="configFilePath">配置文件路径</param>
        public static void LoadFromConfig(string configFilePath)
        {
            // 示例：从配置文件加载阵营关系
            // 实际项目中需要实现具体的配置文件解析逻辑
            // Debug.Log($"从配置文件加载阵营关系：{configFilePath}");
        }

        /// <summary>
        /// 保存阵营关系到配置文件（示例）
        /// </summary>
        /// <param name="configFilePath">配置文件路径</param>
        public static void SaveToConfig(string configFilePath)
        {
            // 示例：保存阵营关系到配置文件
            // 实际项目中需要实现具体的配置文件保存逻辑
            // Debug.Log($"保存阵营关系到配置文件：{configFilePath}");
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取所有阵营关系
        /// </summary>
        /// <returns>阵营关系表</returns>
        public static Dictionary<(int, int), FactionRelationType> GetAllRelations()
        {
            return new Dictionary<(int, int), FactionRelationType>(s_factionRelations);
        }

        /// <summary>
        /// 打印所有阵营关系（用于调试）
        /// </summary>
        public static void PrintAllRelations()
        {
            // Debug.Log("阵营关系表：");
            foreach (var kvp in s_factionRelations)
            {
                // Debug.Log($"阵营 {kvp.Key.Item1} 对阵营 {kvp.Key.Item2}：{kvp.Value}");
            }
        }

        #endregion
    }

    /// <summary>
    /// FactionRelationManager 使用说明：
    /// ============================================================
    /// 纯静态类，无需挂载到场景对象。首次访问时通过静态构造函数自动初始化默认阵营关系。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【与 ObjectStatsConfig 的关系】
    /// ════════════════════════════════════════════════════════════
    ///   ObjectStatsConfig 内部有 protected virtual GetFactionRelation(targetFactionID) 方法，
    ///   默认实现使用硬编码规则（同阵营/玩家阵营/怪物阵营等常量判断）。
    ///   实际项目可通过以下两种方式接入 FactionRelationManager：
    ///     方式 1：重写 ObjectStatsConfig 子类的 GetFactionRelation 方法
    ///       protected override FactionRelationType GetFactionRelation(int targetFactionID)
    ///       {
    ///           return FactionRelationManager.GetFactionRelation(FactionID, targetFactionID);
    ///       }
    ///     方式 2：在 ObjectStatsConfig 子类外部直接调用
    ///       FactionRelationType rel = FactionRelationManager.GetFactionRelation(factionA, factionB);
    ///   CanDealDamageTo / IsFriendly / IsHostile 内部会调用 GetFactionRelation 进行阵营判定。
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【初始化机制】
    /// ════════════════════════════════════════════════════════════
    ///   - 静态构造函数：类首次被访问时自动调用 InitializeDefaultFactionRelations()
    ///   - 通过 s_isInitialized 标志防止重复初始化
    ///   - Reinitialize()：清空所有关系后重新加载默认配置（用于测试或运行时重置）
    ///   - 初始化日志：Debug.Log("阵营关系管理器已初始化...")
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【默认阵营配置】
    /// ════════════════════════════════════════════════════════════
    ///   玩家阵营(1-10)：互为友好
    ///   怪物阵营(11-50)：与玩家阵营敌对（双向）
    ///   NPC阵营(51-99)：对所有阵营中立（双向）
    ///   其他阵营(100+)：默认中立（未配置时 GetFactionRelation 返回 Neutral）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【关系类型说明】
    /// ════════════════════════════════════════════════════════════
    ///   Friendly  = 友好（不能互相伤害，可组队治疗）
    ///   Alliance  = 同盟（不能互相伤害，组队后的额外友好关系）
    ///   Neutral   = 中立（可以互相伤害，PVP 区域）
    ///   Hostile   = 敌对（可以互相伤害，主动攻击）
    ///   注意：Friendly 和 Alliance 在 CanDealDamageTo 中行为一致（默认不能伤害）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【单向 vs 双向关系】
    /// ════════════════════════════════════════════════════════════
    ///   SetFactionRelation(A, B, relation)            — 单向：仅设置 A→B 的关系
    ///     适用场景：A 对 B 敌对，但 B 对 A 友好（如怪物对玩家敌对，玩家对怪物友好）
    ///   SetFactionRelationBidirectional(A, B, relation) — 双向：同时设置 A→B 和 B→A
    ///     适用场景：双方互为友好/敌对（默认推荐，对称关系更直观）
    ///   注意：GetFactionRelation(A, B) 和 GetFactionRelation(B, A) 可能返回不同结果（单向设置时）
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【常用方法】
    /// ════════════════════════════════════════════════════════════
    ///   查询方法：
    ///     GetFactionRelation(factionA, factionB) — 查询两个阵营的关系（未配置返回 Neutral）
    ///     GetAllRelations()                     — 获取所有关系的副本（修改不影响原始数据）
    ///
    ///   设置方法：
    ///     SetFactionRelation(A, B, relation)            — 设置单向关系
    ///     SetFactionRelationBidirectional(A, B, relation) — 设置双向关系
    ///     AddAlliance(factionIDs)  — 批量设置多个阵营互为同盟
    ///     AddHostile(factionIDs)   — 批量设置多个阵营互为敌对
    ///
    ///   管理方法：
    ///     RemoveFactionRelation(A, B) — 移除指定关系
    ///     ClearAllRelations()         — 清空所有关系
    ///     Reinitialize()              — 清空并重新加载默认配置
    ///     PrintAllRelations()         — 打印所有关系到 Console（调试用）
    ///
    ///   配置文件支持（示例实现，需自行扩展）：
    ///     LoadFromConfig(configFilePath) — 从配置文件加载阵营关系
    ///     SaveToConfig(configFilePath)   — 保存阵营关系到配置文件
    ///     实际项目可使用 ScriptableObject 或 JSON 文件实现
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【性能考虑】
    /// ════════════════════════════════════════════════════════════
    ///   - 内部使用 Dictionary&lt;(int, int), FactionRelationType&gt; 存储关系，O(1) 查询
    ///   - 默认初始化约 4000 条关系（10×10 玩家 + 40×10×2 玩家-怪物 + 49×40×2 NPC）
    ///   - 静态字典在游戏期间不释放，内存占用约几十 KB（可接受）
    ///   - 查询热路径：CanDealDamageTo → GetFactionRelation → Dictionary.TryGetValue，无 GC
    ///
    /// ════════════════════════════════════════════════════════════
    /// 【实际项目扩展建议】
    /// ════════════════════════════════════════════════════════════
    ///   1. 自定义阵营 ID 范围：修改 k_playerFactionMinID 等常量或使用配置文件
    ///   2. 动态阵营关系：运行时通过 SetFactionRelation 修改（如任务触发阵营转换）
    ///   3. 持久化：实现 LoadFromConfig/SaveToConfig，使用 JSON/ScriptableObject 存储关系
    ///   4. 多服务器同步：MMO 场景下需要将阵营关系同步到所有服务器
    ///   5. 与仇恨系统结合：Hostile 关系自动触发 AI 仇恨，Friendly 关系可互助
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 1：查询阵营关系（自动初始化）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 首次调用时自动初始化默认配置
    /// FactionRelationType relation = FactionRelationManager.GetFactionRelation(1, 11);
    /// // relation == FactionRelationType.Hostile（玩家对怪物为敌对）
    ///
    /// relation = FactionRelationManager.GetFactionRelation(1, 2);
    /// // relation == FactionRelationType.Friendly（玩家之间为友好）
    ///
    /// relation = FactionRelationManager.GetFactionRelation(1, 51);
    /// // relation == FactionRelationType.Neutral（玩家对NPC为中立）
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 2：自定义阵营关系（双向）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 设置阵营 1 和 2 互为同盟
    /// FactionRelationManager.SetFactionRelationBidirectional(1, 2, FactionRelationType.Alliance);
    ///
    /// // 设置阵营 1 和 100 互为敌对
    /// FactionRelationManager.SetFactionRelationBidirectional(1, 100, FactionRelationType.Hostile);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 3：单向关系（A 对 B 敌对，但 B 对 A 友好）
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 单向设置：阵营1 对 阵营2 敌对，但 阵营2 对 阵营1 保持原关系
    /// FactionRelationManager.SetFactionRelation(1, 2, FactionRelationType.Hostile);
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 4：批量设置同盟
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// List&lt;int&gt; allianceFactions = new List&lt;int&gt; { 1, 2, 3 };
    /// FactionRelationManager.AddAlliance(allianceFactions);
    /// // 阵营 1、2、3 之间互为同盟
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 5：批量设置敌对
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// List&lt;int&gt; hostileFactions = new List&lt;int&gt; { 1, 11, 12 };
    /// FactionRelationManager.AddHostile(hostileFactions);
    /// // 阵营 1、11、12 之间互为敌对
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 6：移除关系与清空
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 移除指定关系
    /// FactionRelationManager.RemoveFactionRelation(1, 100);
    ///
    /// // 清空所有关系
    /// FactionRelationManager.ClearAllRelations();
    ///
    /// // 重新初始化（恢复默认配置）
    /// FactionRelationManager.Reinitialize();
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 7：结合 ObjectStatsConfig 判断能否造成伤害
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// ObjectStatsConfig attacker = fighter.GetObjectStats();
    /// ObjectStatsConfig target = monster.GetObjectStats();
    ///
    /// // CanDealDamageTo 内部会调用 FactionRelationManager 判断阵营关系
    /// bool canDamage = attacker.CanDealDamageTo(target);
    /// if (canDamage)
    /// {
    ///     float damage = attacker.CalculateDamage(target);
    ///     target.TakeDamage(damage);
    /// }
    /// </code>
    ///
    /// ────────────────────────────────────────────────────────────
    /// 示例 8：调试与查看所有关系
    /// ────────────────────────────────────────────────────────────
    /// <code>
    /// // 打印所有阵营关系到 Console
    /// FactionRelationManager.PrintAllRelations();
    ///
    /// // 获取所有关系的副本（修改副本不影响原始数据）
    /// Dictionary&lt;(int, int), FactionRelationType&gt; allRelations =
    ///     FactionRelationManager.GetAllRelations();
    /// foreach (var kvp in allRelations)
    /// {
    ///     Debug.Log($"阵营 {kvp.Key.Item1} -&gt; 阵营 {kvp.Key.Item2}：{kvp.Value}");
    /// }
    /// </code>
    /// </summary>
}
