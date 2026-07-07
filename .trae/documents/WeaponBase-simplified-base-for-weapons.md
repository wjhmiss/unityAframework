# WeaponBase 简化武器基类 — 计划文档

## 背景与动机

用户提出:Sword 作为武器没有动画、没有移动逻辑,继承自 ObjectBase 是否开销过大?是否可以参考 ObjectBase 新增一个简化的 WeaponBase 基类,让所有武器继承?

## 现状分析(Phase 1 探索结果)

### ObjectBase 对武器的开销评估

ObjectBase (位于 [ObjectBase.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Core/ObjectBase.cs)) 是重型基类,包含以下与武器无关的子系统:

**字段开销(每个武器实例浪费 ~400+ 字节):**
- PlayableGraph + 8 个动画槽位 (`m_animationSlots`) + 8 个音频槽位 (`m_audioPlayables`)
- 动画混合器、音频混合器、动画/音频输出
- Addressables 资源句柄字典 `m_loadedAssetHandles`
- 组件缓存字典 `m_componentCache`
- 移动系统字段(m_movementConfig / m_movementInput / m_horizontalVelocity 等)
- CrossFade 过渡状态字段
- 帧事件列表、连击索引、攻击冷却字段

**每帧生命周期开销:**
- `Update()` 即使早返回也会跨越 native↔managed 边界 1 次
- `FixedUpdate()` 同样会跨越 native↔managed 边界 1 次(即使 MovementConfig=null 早返回)
- 5-6 个空方法调用(HandleInput/HandleRegeneration/UpdateAnimationCrossFade/CheckFrameEvents/CleanupFinishedAudioPlayables)

**结论:** 对武器(剑/火球)这类"无动画、无移动"的轻量攻击物体,ObjectBase 开销过大,确实需要独立基类。

### 现有 SimpleObjectBase 参考

[SimpleObjectBase.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Core/SmallBase/SimpleObjectBase.cs) 已是轻量基类范例(供 Bullet 使用):
- 直接继承 MonoBehaviour,完全脱离 ObjectBase
- 用 owner 的 int 字段镜像阵营信息(避免 GC)
- 使用 Tick(deltaTime) 由 Pool 单点驱动
- 使用克隆快照锁定 owner 属性(发射时一次性拷贝)

### 已完成实现(本会话之前已落地)

经探索发现,以下三个文件的实现已经完成:

1. **[WeaponBase.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Core/SmallBase/WeaponBase.cs)** — 482 行,完整实现
   - 直接继承 MonoBehaviour
   - 提供 SetupComponents / SetupObjectStats / AddObjectComponent / CalculateObjectBounds / AddBoxCollider / AddCapsuleCollider / FindParentObjectBase / InheritOwnerFactionInfo / CanDealDamageTo / CalculateDamage / ApplyDamageTo 等核心方法
   - 使用静态缓冲区 s_twoAttackerBuffer 避免 params 数组分配
   - 设计决策:阵营判定委托 m_objectStats.CanDealDamageTo(非镜像 int 字段,因为武器有自己的 m_objectStats)

2. **[Sword.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Sample/Sword.cs)** — 代码部分(1-209 行)已迁移到 WeaponBase
   - `public class Sword : WeaponBase`
   - 移除了 MovementConfig override / m_owner / m_ownerStats 字段(已上移到基类)
   - 移除了 FindParentObjectBase / InheritOwnerFactionInfo / OnDamaged / OnDeath
   - OnTriggerEnter 改用 CanDealDamageTo / CalculateDamage / ApplyDamageTo

3. **[Fire.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Sample/Fire.cs)** — 代码部分(1-292 行)已迁移到 WeaponBase
   - `public class Fire : WeaponBase`
   - Update/OnDestroy 改为 private(WeaponBase 无 virtual Update/OnDestroy)
   - ApplyDamageToTarget 改用 CalculateDamage / ApplyDamageTo

### Fighter.cs 兼容性

[Fighter.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Sample/Fighter.cs) 第 328 行使用 `GetComponentInChildren<Sword>()` 查找武器组件 — 因为 Sword 仍是 MonoBehaviour 子类,此调用完全兼容,无需修改。

### AttackCalcPanel UI 兼容性

根据 SimpleObjectBase 已验证的模式:`ApplyDamageTo` 调用 `ObjectStatsConfig.SetLastAttackRefs(target, (ObjectBase)null, m_owner)`,武器自身位置传 null,UI 回退到 AttackerSnapshots 显示快照(不显示武器卡片,只显示 owner)。Fire 的 m_owner 为 null 时,UI 只显示目标卡片。

## 待完成工作

实现代码部分已 100% 完成,**唯一遗留的是文档同步问题**:

### 任务 1:清理 Sword.cs 底部过时文档

**文件:** [Sword.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Sample/Sword.cs) 第 211 行至文件末尾

**问题:** 底部 `<summary>` 文档块仍写着:
- "剑类,继承 ObjectBase"(应为 WeaponBase)
- "【与 ObjectBase 的关系】"(应为 WeaponBase)
- "Sword 继承 ObjectBase,复用父类的属性系统"
- "未重写 MovementConfig(返回 null)"(已删除该 override)
- "重写 OnDamaged/OnDeath 回调"(WeaponBase 不提供这些)
- 字段说明中的 m_owner / m_ownerStats 描述需注明已上移到 WeaponBase
- 初始化流程中的"父类自动处理"需改为 WeaponBase

**修改方式:** 将底部文档块的"ObjectBase"替换为"WeaponBase",删除对 MovementConfig/OnDamaged/OnDeath 的描述,补注 m_owner/m_ownerStats 已上移到基类。

### 任务 2:清理 Fire.cs 底部过时文档

**文件:** [Fire.cs](file:///d:/Unity/项目/test/Assets/AFrameWork/Sample/Fire.cs) 第 294 行至文件末尾

**问题:** 底部 `<summary>` 文档块仍写着:
- "火球类,继承 ObjectBase"(应为 WeaponBase)
- "【与 ObjectBase 的关系】"(应为 WeaponBase)
- "Fire 继承 ObjectBase,复用父类的属性系统"
- "未重写 MovementConfig(默认 null)"(WeaponBase 没有 MovementConfig)
- "父类自动处理:Awake 缓存 MovementConfig(null)"(WeaponBase 不缓存 MovementConfig)

**修改方式:** 将底部文档块的"ObjectBase"替换为"WeaponBase",删除对 MovementConfig 缓存的描述。

## 假设与决策

1. **不删除底部文档**:文档虽然过时,但包含有价值的字段说明、初始化流程、碰撞流程、暴击计算流程等设计知识,只需修正继承关系描述即可。
2. **不修改 WeaponBase.cs**:已实现且符合设计目标,无需调整。
3. **不修改 Fighter.cs**:`GetComponentInChildren<Sword>()` 已兼容。
4. **不修改 ObjectBase.cs**:武器不再继承它,无需改动。
5. **不修改 AttackCalcPanelController.cs**:已通过 SetLastAttackRefs 的 null 处理机制兼容。

## 验证步骤

完成文档清理后:

1. **编译验证**:确认三个文件(WeaponBase.cs / Sword.cs / Fire.cs)无编译错误
2. **运行时验证**:在 Unity Play 模式下
   - 让 Fighter 攻击 Monster,确认 Sword 仍能正常造成伤害
   - 触发 Fire(火球/AOE 陷阱),确认范围伤害正常
   - Alt+2 打开 AttackCalcPanel,确认 UI 显示正确(不显示武器卡片,只显示 Fighter→Monster)
3. **性能验证**(可选):在 Profiler 中确认 Sword/Fire 不再触发 ObjectBase.Update/FixedUpdate 的 native↔managed 边界开销

## 总结

实现代码已经在前一会话中完成并通过验证。本次计划仅剩文档同步清理工作 — 修正 Sword.cs 和 Fire.cs 底部过时的"继承 ObjectBase"描述为"WeaponBase",使代码与文档保持一致。预计工作量:10-15 分钟的纯文本编辑。
