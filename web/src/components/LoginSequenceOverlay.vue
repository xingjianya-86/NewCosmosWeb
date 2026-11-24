<template>
  <Teleport to="body">
    <div v-if="visible" class="nc-login-seq" role="dialog" aria-label="操作员登录" aria-live="polite">
      <iframe
        v-if="src"
        ref="frameRef"
        :src="src"
        class="nc-login-seq__frame"
        :class="{ 'is-ready': frameReady }"
        title="帝皇权杖δ-me13 操作员登录"
        allow="autoplay"
        @load="frameReady = true"
      ></iframe>

      <div v-if="!frameReady" class="nc-login-seq__loading">
        <span class="nc-login-seq__mark" aria-hidden="true">帝皇权杖δ-me13</span>
        <span class="nc-login-seq__hint">正在准备身份核验…</span>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { getErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { buildLoginUrl } from '@/utils/splash'

/* 序列登录外壳：未登录启动时由 App.vue 渲染。
   login.html 自己完成「档案底板 → 五步核验 → 终幕」，本组件只做三件事：
   1) 收到 login-ready 后推送 prefill（只带最近用户名，密码永不出前端存储）；
   2) 收到 login-submit 后调用真实登录接口，并用 auth-result 回执；
   3) 收到 done / login-skip / login-fallback / error 后交给 App.vue 决定去向。
   凭据只在一次 postMessage + 一次 HTTP 请求中存在：不写日志、不落 localStorage。 */

const emit = defineEmits<{ done: []; fallback: [reason: string] }>()

const SOURCE_IN = 'newcosmos-parent'
const SOURCE_OUT = 'newcosmos-splash'
const READY_TIMEOUT_MS = 20_000
const HARD_TIMEOUT_MS = 5 * 60_000
const LAST_USER_KEY = 'nc.auth.lastUser'

const auth = useAuthStore()
const frameRef = ref<HTMLIFrameElement | null>(null)
const src = buildLoginUrl()
const visible = ref(true)
const frameReady = ref(false)

let iframeOrigin = ''
let readyTimer: number | undefined
let hardTimer: number | undefined
let previousOverflow = ''
let gotReady = false
let closing = false
let authInFlight = false
let lastSubmitId = ''

function clearTimer(timer: number | undefined): undefined {
  if (timer !== undefined) window.clearTimeout(timer)
  return undefined
}

function clearTimers(): void {
  readyTimer = clearTimer(readyTimer)
  hardTimer = clearTimer(hardTimer)
}

function readLastUser(): string {
  try {
    return window.localStorage.getItem(LAST_USER_KEY) || ''
  } catch {
    return ''
  }
}

function writeLastUser(user: string): void {
  try {
    window.localStorage.setItem(LAST_USER_KEY, user)
  } catch {
    // 隐私模式 / 存储不可用：不影响登录
  }
}

function postDispatch(kind: string, data: unknown): void {
  const frame = frameRef.value
  if (!frame?.contentWindow || !iframeOrigin) return
  frame.contentWindow.postMessage(
    { source: SOURCE_IN, type: 'dispatch', kind, data },
    iframeOrigin,
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object'
}

function teardown(): void {
  visible.value = false
  clearTimers()
  window.removeEventListener('message', handleMessage)
  document.documentElement.style.overflow = previousOverflow
}

function finishFallback(reason: string): void {
  if (closing) return
  closing = true
  console.warn(`[login-sequence] 序列登录回落经典页：${reason}`)
  teardown()
  emit('fallback', reason)
}

function finishDone(): void {
  if (closing) return
  closing = true
  const ok = auth.isSessionValid()
  teardown()
  if (ok) emit('done')
  else emit('fallback', '终幕到达但认证未成功')
}

function onLoginReady(): void {
  if (closing || gotReady) return
  gotReady = true
  clearTimer(readyTimer)
  /* 已进入表单阶段：登录输入需要时间，只留 5 分钟硬兜底，不再用启动短超时 */
  hardTimer = window.setTimeout(() => finishFallback('登录停留超时'), HARD_TIMEOUT_MS)

  postDispatch('prefill', {
    user: readLastUser(),
    pass: '',
    remember: true,
    rememberPass: false,
  })
}

async function onLoginSubmit(payload: unknown): Promise<void> {
  if (closing || !isRecord(payload)) return

  const id = String(payload.id ?? '')
  const user = String(payload.user ?? '').trim()
  const pass = String(payload.pass ?? '')
  const remember = payload.remember !== false

  if (!id) return
  if (!user || !pass) {
    postDispatch('auth-result', { id, ok: false, msg: '请输入账号与密码' })
    return
  }
  /* 同一提交号去重；在途认证不接受新提交（与桌面端语义一致） */
  if (id === lastSubmitId || authInFlight) return
  lastSubmitId = id
  authInFlight = true

  try {
    await auth.login(user, pass)
    if (remember) writeLastUser(user)
    postDispatch('auth-result', { id, ok: true })
  } catch (error) {
    postDispatch('auth-result', {
      id,
      ok: false,
      msg: getErrorMessage(error, '登录失败，请稍后重试'),
    })
  } finally {
    authInFlight = false
  }
}

function handleMessage(event: MessageEvent): void {
  if (closing || !visible.value) return
  /* 只认当前 iframe、且 origin 与 login.html 一致的消息 */
  if (event.origin !== iframeOrigin) return
  if (event.source !== frameRef.value?.contentWindow) return
  if (!isRecord(event.data)) return

  const message = event.data
  if (message.source !== SOURCE_OUT) return

  const type = String(message.type ?? '')
  switch (type) {
    case 'login-ready':
      onLoginReady()
      break
    case 'login-submit':
      void onLoginSubmit(message.payload)
      break
    case 'done':
      finishDone()
      break
    case 'login-skip':
      finishFallback('用户切换到经典登录')
      break
    case 'login-fallback':
      finishFallback(`脚本回落：${String(message.message ?? '')}`)
      break
    case 'error':
      finishFallback(`脚本异常：${String(message.message ?? 'script-error')}`)
      break
    default:
      /* boot / audio 等诊断消息：忽略 */
      break
  }
}

onMounted(() => {
  if (!visible.value) return

  try {
    iframeOrigin = new URL(src, window.location.href).origin
  } catch {
    iframeOrigin = window.location.origin
  }

  previousOverflow = document.documentElement.style.overflow
  document.documentElement.style.overflow = 'hidden'

  window.addEventListener('message', handleMessage)
  /* 20 秒等不到 login-ready（脚本异常 / 资源 404）→ 直接回经典登录页 */
  readyTimer = window.setTimeout(() => finishFallback('login-ready 超时'), READY_TIMEOUT_MS)
})

onBeforeUnmount(() => {
  clearTimers()
  window.removeEventListener('message', handleMessage)
  if (visible.value) document.documentElement.style.overflow = previousOverflow
})
</script>

<style scoped>
.nc-login-seq {
  position: fixed;
  inset: 0;
  z-index: 99990;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  /* 与 login.html / 动画同一块暖白「技术档案」底板，首帧不闪白 */
  background-color: #f2ece1;
  background-image:
    linear-gradient(
      90deg,
      rgba(223, 213, 194, 0) 0%,
      rgba(223, 213, 194, 0) 30%,
      rgba(223, 213, 194, 0.55) 30%,
      rgba(223, 213, 194, 0.55) 40%,
      rgba(223, 213, 194, 0) 40%,
      rgba(223, 213, 194, 0) 44%,
      rgba(223, 213, 194, 0.4) 44%,
      rgba(223, 213, 194, 0.4) 54%,
      rgba(223, 213, 194, 0) 54%,
      rgba(223, 213, 194, 0) 58%,
      rgba(223, 213, 194, 0.26) 58%,
      rgba(223, 213, 194, 0.26) 68%,
      rgba(223, 213, 194, 0) 68%
    ),
    linear-gradient(135deg, #f2ece1 0%, #e9e1d1 100%);
  background-size: cover;
  background-position: center;
}

.nc-login-seq__frame {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  border: 0;
  display: block;
  background: transparent;
  opacity: 0;
  transition: opacity 240ms ease;
}

.nc-login-seq__frame.is-ready {
  opacity: 1;
}

.nc-login-seq__loading {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  pointer-events: none;
  animation: nc-login-fade 900ms ease both;
}

.nc-login-seq__mark {
  font-family: 'Noto Serif SC', 'Source Han Serif SC', 'Songti SC', serif;
  font-size: 15px;
  letter-spacing: 0.34em;
  color: #6c5f52;
}

.nc-login-seq__hint {
  font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', monospace;
  font-size: 12px;
  letter-spacing: 0.28em;
  color: rgba(108, 95, 82, 0.62);
  animation: nc-login-pulse 1.6s ease-in-out infinite;
}

@keyframes nc-login-pulse {
  0%,
  100% {
    opacity: 0.45;
  }
  50% {
    opacity: 0.95;
  }
}

@keyframes nc-login-fade {
  from {
    opacity: 0;
    transform: translateY(4px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .nc-login-seq__loading,
  .nc-login-seq__hint {
    animation: none;
  }
}
</style>
