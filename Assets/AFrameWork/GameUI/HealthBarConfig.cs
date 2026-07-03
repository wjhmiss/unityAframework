using System;
using UnityEngine;

namespace AFrameWork.GameUI
{
    /// <summary>
    /// 血条配置数据结构
    /// 包含血条的显示参数和行为设置
    /// 完全兼容 C# 9.0（Unity 团结引擎使用）
    /// </summary>
    [Serializable]
    public struct HealthBarConfig
    {
        // ══════════════════════════════════════════════════════════════════════════
        // 字段定义（C# 9.0 结构体字段不能在声明时初始化）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 血条宽度（像素）
        /// </summary>
        [Tooltip("血条宽度（像素）")]
        public float Width;

        /// <summary>
        /// 血条高度（像素）
        /// </summary>
        [Tooltip("血条高度（像素）")]
        public float Height;

        /// <summary>
        /// 血条距离目标对象头部的垂直偏移（像素）
        /// </summary>
        [Tooltip("血条距离目标对象头部的垂直偏移（像素）")]
        public float OffsetY;

        /// <summary>
        /// 是否显示数值文本
        /// </summary>
        [Tooltip("是否显示数值文本")]
        public bool ShowText;

        /// <summary>
        /// 低血量阈值百分比（0.0-1.0）
        /// </summary>
        [Tooltip("低血量阈值百分比（0.0-1.0）")]
        [Range(0.0f, 1.0f)]
        public float LowHealthThreshold;

        /// <summary>
        /// 危急血量阈值百分比（0.0-1.0）
        /// </summary>
        [Tooltip("危急血量阈值百分比（0.0-1.0）")]
        [Range(0.0f, 1.0f)]
        public float CriticalHealthThreshold;

        /// <summary>
        /// 血条平滑过渡动画时间（秒）
        /// </summary>
        [Tooltip("血条平滑过渡动画时间（秒）")]
        public float SmoothTransitionDuration;

        /// <summary>
        /// 淡入淡出动画时间（秒）
        /// </summary>
        [Tooltip("淡入淡出动画时间（秒）")]
        public float FadeDuration;

        /// <summary>
        /// 血条更新频率限制（秒），用于性能优化
        /// </summary>
        [Tooltip("血条更新频率限制（秒），用于性能优化")]
        public float UpdateInterval;

        /// <summary>
        /// 是否启用屏幕边缘裁剪
        /// </summary>
        [Tooltip("是否启用屏幕边缘裁剪")]
        public bool EnableScreenClipping;

        /// <summary>
        /// 屏幕边缘裁剪边距（像素）
        /// </summary>
        [Tooltip("屏幕边缘裁剪边距（像素）")]
        public float ScreenClipMargin;

        /// <summary>
        /// 是否启用遮挡检测（被建筑物等遮挡时自动淡出血条）
        /// </summary>
        [Tooltip("是否启用遮挡检测")]
        public bool EnableOcclusionCheck;

        /// <summary>
        /// 遮挡检测的 LayerMask（哪些层算遮挡物，0 = 所有层）
        /// </summary>
        [Tooltip("遮挡检测的 LayerMask（0 = 所有层）")]
        public LayerMask OcclusionLayerMask;

        /// <summary>
        /// 被遮挡时的透明度（0=完全透明，1=不透明）
        /// </summary>
        [Tooltip("被遮挡时的透明度")]
        [Range(0.0f, 1.0f)]
        public float OccludedAlpha;

        // ══════════════════════════════════════════════════════════════════════════
        // 常量定义
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 默认血条宽度
        /// </summary>
        private const float k_defaultWidth = 100f;

        /// <summary>
        /// 默认血条高度
        /// </summary>
        private const float k_defaultHeight = 12f;

        /// <summary>
        /// 默认垂直偏移
        /// </summary>
        private const float k_defaultOffsetY = -20f;

        /// <summary>
        /// 默认低血量阈值
        /// </summary>
        private const float k_defaultLowHealthThreshold = 0.3f;

        /// <summary>
        /// 默认危急血量阈值
        /// </summary>
        private const float k_defaultCriticalHealthThreshold = 0.1f;

        /// <summary>
        /// 默认平滑过渡时间
        /// </summary>
        private const float k_defaultSmoothTransitionDuration = 0.2f;

        /// <summary>
        /// 默认淡入淡出时间
        /// </summary>
        private const float k_defaultFadeDuration = 0.3f;

        /// <summary>
        /// 默认更新间隔
        /// </summary>
        private const float k_defaultUpdateInterval = 0.05f;

        /// <summary>
        /// 默认屏幕裁剪边距
        /// </summary>
        private const float k_defaultScreenClipMargin = 10f;

        /// <summary>
        /// 默认是否启用遮挡检测
        /// </summary>
        private const bool k_defaultEnableOcclusionCheck = true;

        /// <summary>
        /// 默认遮挡检测 LayerMask（0 = 所有层）
        /// </summary>
        private const int k_defaultOcclusionLayerMask = 0;

        /// <summary>
        /// 默认被遮挡时的透明度
        /// </summary>
        private const float k_defaultOccludedAlpha = 0.3f;

        // ══════════════════════════════════════════════════════════════════════════
        // 构造方法（C# 9.0 允许带默认参数的构造函数）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 创建血条配置（所有参数都有默认值）
        /// </summary>
        /// <param name="width">血条宽度</param>
        /// <param name="height">血条高度</param>
        /// <param name="offsetY">垂直偏移</param>
        /// <param name="showText">是否显示文本</param>
        /// <param name="lowHealthThreshold">低血量阈值</param>
        /// <param name="criticalHealthThreshold">危急血量阈值</param>
        /// <param name="smoothTransitionDuration">平滑过渡时间</param>
        /// <param name="fadeDuration">淡入淡出时间</param>
        /// <param name="updateInterval">更新间隔</param>
        /// <param name="enableScreenClipping">是否启用屏幕裁剪</param>
        /// <param name="screenClipMargin">屏幕裁剪边距</param>
        public HealthBarConfig(
            float width = k_defaultWidth,
            float height = k_defaultHeight,
            float offsetY = k_defaultOffsetY,
            bool showText = true,
            float lowHealthThreshold = k_defaultLowHealthThreshold,
            float criticalHealthThreshold = k_defaultCriticalHealthThreshold,
            float smoothTransitionDuration = k_defaultSmoothTransitionDuration,
            float fadeDuration = k_defaultFadeDuration,
            float updateInterval = k_defaultUpdateInterval,
            bool enableScreenClipping = true,
            float screenClipMargin = k_defaultScreenClipMargin,
            bool enableOcclusionCheck = k_defaultEnableOcclusionCheck,
            LayerMask occlusionLayerMask = default,
            float occludedAlpha = k_defaultOccludedAlpha)
        {
            Width = width;
            Height = height;
            OffsetY = offsetY;
            ShowText = showText;
            LowHealthThreshold = lowHealthThreshold;
            CriticalHealthThreshold = criticalHealthThreshold;
            SmoothTransitionDuration = smoothTransitionDuration;
            FadeDuration = fadeDuration;
            UpdateInterval = updateInterval;
            EnableScreenClipping = enableScreenClipping;
            ScreenClipMargin = screenClipMargin;
            EnableOcclusionCheck = enableOcclusionCheck;
            OcclusionLayerMask = occlusionLayerMask;
            OccludedAlpha = occludedAlpha;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 静态工厂方法
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 创建默认配置实例
        /// </summary>
        public static HealthBarConfig CreateDefault()
        {
            // 调用带默认参数的构造函数（所有参数都使用默认值）
            return new HealthBarConfig();
        }

        /// <summary>
        /// 创建紧凑型血条配置（适合小目标）
        /// </summary>
        public static HealthBarConfig CreateCompact()
        {
            return new HealthBarConfig(
                width: 60f,
                height: 8f,
                offsetY: -15f,
                showText: false);
        }

        /// <summary>
        /// 创建大型血条配置（适合Boss等大型目标）
        /// </summary>
        public static HealthBarConfig CreateLarge()
        {
            return new HealthBarConfig(
                width: 200f,
                height: 25f,
                offsetY: -30f,
                showText: true,
                lowHealthThreshold: 0.4f,
                criticalHealthThreshold: 0.15f);
        }
    }

    /* ══════════════════════════════════════════════════════════════════════════
       【使用说明】

       1. 基本配置创建：
          // 使用默认配置
          HealthBarConfig config = HealthBarConfig.CreateDefault();

          // 使用带默认参数的构造函数
          HealthBarConfig config = new HealthBarConfig();  // 所有参数都使用默认值

          // 使用自定义参数创建
          HealthBarConfig config = new HealthBarConfig(100f, 12f, -20f, true);

       2. 预设配置使用：
          // 紧凑型配置（适合小目标）
          HealthBarConfig compactConfig = HealthBarConfig.CreateCompact();

          // 大型配置（适合Boss）
          HealthBarConfig largeConfig = HealthBarConfig.CreateLarge();

       3. 配置参数调整：
          // 调整血条大小
          config.Width = 120f;
          config.Height = 15f;

          // 调整血量阈值
          config.LowHealthThreshold = 0.3f;  // 30%血量时变为黄色
          config.CriticalHealthThreshold = 0.1f;  // 10%血量时变为红色

          // 禁用文本显示
          config.ShowText = false;

          // 调整动画时间
          config.SmoothTransitionDuration = 0.3f;  // 血量变化过渡时间
          config.FadeDuration = 0.5f;  // 淡入淡出时间

          // 性能优化设置
          config.UpdateInterval = 0.1f;  // 降低更新频率以提高性能

          // 屏幕裁剪设置
          config.EnableScreenClipping = true;  // 启用屏幕边缘裁剪
          config.ScreenClipMargin = 15f;  // 设置裁剪边距

       4. 配置特性说明：
          - Width/Height: 血条尺寸，建议值范围：宽度60-150像素，高度8-20像素
          - OffsetY: 血条距离目标头顶的垂直偏移，负值表示向上偏移
          - ShowText: 是否显示数值文本（如"100/100"）
          - LowHealthThreshold: 触发黄色警告的血量百分比（0.3表示30%）
          - CriticalHealthThreshold: 触发红色警告的血量百分比（0.1表示10%）
          - SmoothTransitionDuration: 血量数值变化时的平滑过渡动画时间
          - FadeDuration: 血条显示/隐藏时的淡入淡出动画时间
          - UpdateInterval: 血条位置更新频率限制，用于性能优化
          - EnableScreenClipping: 是否启用屏幕边缘裁剪，防止血条超出屏幕
          - ScreenClipMargin: 屏幕边缘裁剪的安全边距

       5. 与HealthBarController配合使用：
          // 在HealthBarController中应用配置
          healthBarController.SetConfig(config);

          // 或在创建血条时指定配置
          healthBarController.CreateHealthBar(target, config);

       6. C# 9.0 兼容性说明：
          - 使用带默认参数的构造函数代替字段初始化
          - 所有参数都有默认值，可以灵活调用
          - new HealthBarConfig() 等同于 HealthBarConfig.CreateDefault()
          - 支持部分参数自定义：new HealthBarConfig(width: 120f, height: 15f)

       ══════════════════════════════════════════════════════════════════════════ */
}