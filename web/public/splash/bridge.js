/* ============================================================================
 * 帝皇权杖δ-me13 · 动画 ↔ 浏览器宿主 桥接脚本（Web 版）
 * ----------------------------------------------------------------------------
 * 动画运行时（splash.js）不感知宿主，本脚本只负责把状态汇报给父页面：
 *
 *   web → parent（window.parent.postMessage，结构化消息）：
 *     { source: 'newcosmos-splash', type: 'done'  }  动画演完（含动画内 SKIP）
 *     { source: 'newcosmos-splash', type: 'error', message }  脚本异常
 *     { source: 'newcosmos-splash', type: 'login-ready' }  序列登录页就绪
 *     { source: 'newcosmos-splash', type: 'login-submit', payload }  表单提交
 *     { source: 'newcosmos-splash', type: 'login-skip' | 'login-fallback' }  回经典登录
 *     { source: 'newcosmos-splash', type: 'boot' | 'audio' }  诊断（可选）
 *
 *   parent → web（window.message，结构化消息）：
 *     { source: 'newcosmos-parent', type: 'dispatch', kind, data } → dispatch(kind, data)
 *     兼容直接 { type: 'auth-result' | 'prefill', payload | data }
 *
 *   不再使用 newcosmos-splash:// 自定义协议、不操作 window.location、
 *   不依赖 chrome.webview —— 这套桥在任何普通浏览器 / iframe 里都能用。
 *
 * 目标 origin 从 URL 参数 origin 读取（父页面拼 iframe src 时注入），
 * 缺省 '*'（仅用于本机 file:// 等拿不到 origin 的场景）。
 *
 * 对外契约保持不变：window.__NC_BRIDGE__ 的 report / send / on / dispatch。
 * ========================================================================== */
(function () {
  'use strict'

  var SOURCE = 'newcosmos-splash'
  var hasOwn = Object.prototype.hasOwnProperty

  /* ── 目标 origin ──────────────────────────────────────────────────────── */
  function readTargetOrigin() {
    try {
      var origin = new URLSearchParams(window.location.search).get('origin')
      return origin || '*'
    } catch (e) {
      return '*'
    }
  }

  var targetOrigin = readTargetOrigin()

  function post(type, extra) {
    try {
      var parent = window.parent
      if (!parent || parent === window) return
      var msg = { source: SOURCE, type: type }
      if (extra) {
        for (var k in extra) {
          if (hasOwn.call(extra, k) && extra[k] !== undefined) msg[k] = extra[k]
        }
      }
      parent.postMessage(msg, targetOrigin)
    } catch (e) { /* 父页面已关闭 / 序列化失败：忽略，动画本身不受影响 */ }
  }

  /* ── host → web 订阅表（保留原接口，浏览器侧一般用不到）─────────────── */
  var handlers = {}

  function on(type, fn) {
    if (!fn) return
    (handlers[type] = handlers[type] || []).push(fn)
  }

  function dispatch(type, data) {
    var list = handlers[type] || []
    for (var i = 0; i < list.length; i++) {
      try { list[i](data) } catch (e) { /* 单个处理器异常不影响其余 */ }
    }
  }

  /* ── web → host 上报 ─────────────────────────────────────────────────── */
  function report(kind, msg) {
    /* 保留上报类型：done / error / login-skip / login-fallback …
       只有 error 与 login-fallback 附带 message，其余类型父页面按 type 分流。 */
    var type = String(kind || 'error')
    var extra = {}
    if (type === 'error' || type === 'login-fallback') {
      extra.message = String(msg || '').slice(0, 500)
    }
    post(type, extra)
  }

  /** 结构化消息：Web 版没有宿主业务通道，这里只透传给父页面（父页面按 type 过滤）。 */
  function send(kind, obj) {
    var payload = obj == null ? {} : obj

    /* 启动动画会问宿主「这次走哪条路」：full = 完整播五幕，skip = 片头即收尾。
       Web 端没有 C# 会话恢复逻辑，也暂时不迁移序列登录，统一答复 full，
       让动画完整播放；异步答复以留出 splash.js 注册监听的时间。 */
    if (kind === 'splash-intent') {
      setTimeout(function () { dispatch('splash-intent-result', 'full') }, 0)
    }

    post(String(kind || 'message'), { payload: payload })
  }

  try {
    window.__NC_BRIDGE__ = {
      report: report,
      send: send,
      on: on,
      dispatch: dispatch
    }
  } catch (e) { /* noop */ }

  /* ── parent → web：宿主用 postMessage 派发 dispatch(kind, data) ───────── */
  window.addEventListener('message', function (event) {
    /* 只收父页面这一路：origin 必须等于 URL 参数 origin（父页面 origin） */
    if (targetOrigin !== '*' && event.origin !== targetOrigin) return
    if (event.source && event.source !== window.parent) return

    var data = event.data
    if (!data || typeof data !== 'object') return

    if (data.source === 'newcosmos-parent' && data.type === 'dispatch') {
      if (typeof data.kind === 'string' && data.kind) dispatch(data.kind, data.data)
      return
    }

    /* 兼容直接 { type: 'auth-result' | 'prefill', payload | data } */
    if (data.type === 'auth-result' || data.type === 'prefill') {
      dispatch(data.type, data.payload !== undefined ? data.payload : data.data)
    }
  })

  /* ── 生命周期信号 ────────────────────────────────────────────────────── */
  var doneSent = false

  window.addEventListener('dsh-startup:done', function () {
    /* 等 finish() 把 root 摘掉、页面淡出后再通知父页面，切换更顺 */
    setTimeout(function () {
      if (doneSent) return
      doneSent = true
      report('done')
    }, 120)
  })

  window.addEventListener('error', function (e) {
    var msg = (e && (e.message || (e.error && e.error.message))) || 'script-error'
    report('error', msg)
  }, true)

  window.addEventListener('unhandledrejection', function (e) {
    var reason = e && e.reason
    report('error', (reason && (reason.message || reason)) || 'unhandledrejection')
  })

  /* 脚本已加载回执：父页面可据此判断 iframe 是否真的跑起来了（看门狗兜底用）。 */
  post('boot')

  /* 音频诊断（可选）：4 秒后回报 AudioContext 状态与已解码素材数，
     父页面日志据此判断自动播放是否被策略拦截、mp3 是否加载成功。 */
  setTimeout(function () {
    try {
      var api = window.__DSH_STARTUP_API__
      var d = api && api.audioDiag ? api.audioDiag() : { state: 'n/a', assets: -1 }
      post('audio', { state: String(d.state || 'unknown'), assets: Number(d.assets) })
    } catch (e) {
      post('audio', { state: 'diag-error', assets: -1 })
    }
  }, 4000)
})()
