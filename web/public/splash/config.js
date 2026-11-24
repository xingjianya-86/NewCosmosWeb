/* ============================================================================
 * 帝皇权杖δ-me13 · 启动动画静态配置（Web 版）
 * ----------------------------------------------------------------------------
 * 桌面端由 C# 每次启动生成 config.js（注入上次登录用户名）；Web 版没有本地
 * 用户上下文，改为固定文案，绝不写入真实用户名 / 密码 / 任何凭据。
 *
 * 加载顺序：config.js → bridge.js → core/* → scenes/* → splash.js
 * 未列出的字段一律走 core/anim-config.js 的 A.DEFAULTS 默认值。
 * ========================================================================== */
window.__DSH_STARTUP__ = {
  enabled: true,
  mode: 'session',
  theme: 'light',
  speed: 1,
  allowSkip: true,
  sound: true,
  voice: true,
  identity: '操作员',
  brand1: 'NEWCOSMOS',
  brand2: 'SOCIAL ASSISTANCE MANAGEMENT',
  brand3a: '帝皇权杖',
  brand3b: 'δ-me13',
  orgZh: '民政社会救助',
  orgEn: 'NEWCOSMOS',
  accent: '#ab3227',
  glitchA: '#ff3b30',
  glitchB: '#7f231b'
}
