<template>
  <Teleport to="body">
    <div v-if="visible" class="nc-splash" role="dialog" aria-label="启动动画" aria-live="polite">
      <iframe
        v-if="src"
        :src="src"
        class="nc-splash__frame"
        :class="{ 'is-ready': frameReady }"
        title="帝皇权杖δ-me13 启动动画"
        allow="autoplay"
        @load="frameReady = true"
      ></iframe>

      <div v-if="!frameReady" class="nc-splash__loading">
        <span class="nc-splash__mark" aria-hidden="true">帝皇权杖δ-me13</span>
        <span class="nc-splash__hint">正在进入…</span>
      </div>

      <button class="nc-splash__skip" type="button" @click="finish('skip')">跳过</button>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { buildSplashUrl, markSplashSeen } from '@/utils/splash'

/* 启动动画外壳：由 App.vue 根据登录态决定是否渲染。
   动画自己派发 dsh-startup:done（由 splash/bridge.js 转成 postMessage），
   本组件收到后 emit done；失败 / 跳过 / 60 秒兜底都会结束。 */

const emit = defineEmits<{ done: [reason: string] }>()

const SOURCE = 'newcosmos-splash'
const FALLBACK_MS = 60_000
const END_TYPES = new Set(['done', 'error', 'skip'])

const src = buildSplashUrl()
const visible = ref(true)
const frameReady = ref(false)

let expectedOrigin = ''
let fallbackTimer: number | undefined
let previousOverflow = ''

function clearFallback(): void {
  if (fallbackTimer !== undefined) {
    window.clearTimeout(fallbackTimer)
    fallbackTimer = undefined
  }
}

function finish(reason: string): void {
  if (!visible.value) return
  if (reason === 'error' || reason === 'timeout') {
    console.warn(`[splash] 启动动画提前结束：${reason}`)
  }
  visible.value = false
  clearFallback()
  window.removeEventListener('message', handleMessage)
  document.documentElement.style.overflow = previousOverflow
  markSplashSeen()
  emit('done', reason)
}

function isSplashMessage(data: unknown): data is { source: string; type: string; message?: string } {
  return !!data && typeof data === 'object' && (data as { source?: unknown }).source === SOURCE
}

function handleMessage(event: MessageEvent): void {
  /* 只认当前 iframe 这一路：生产 iframe 在静态资源域，开发同源 */
  if (!visible.value || event.origin !== expectedOrigin) return
  if (!isSplashMessage(event.data)) return

  const type = String(event.data.type || '')
  if (!END_TYPES.has(type)) return

  if (type === 'error') {
    console.warn('[splash] 动画上报异常：', event.data.message || 'script-error')
  }
  finish(type)
}

onMounted(() => {
  if (!visible.value || !src) return

  /* 本次会话只播一次：一进来就落标记，刷新页面不会再从头播 */
  markSplashSeen()

  try {
    expectedOrigin = new URL(src, window.location.href).origin
  } catch {
    expectedOrigin = window.location.origin
  }

  previousOverflow = document.documentElement.style.overflow
  document.documentElement.style.overflow = 'hidden'

  window.addEventListener('message', handleMessage)
  fallbackTimer = window.setTimeout(() => finish('timeout'), FALLBACK_MS)
})

onBeforeUnmount(() => {
  clearFallback()
  window.removeEventListener('message', handleMessage)
  if (visible.value) document.documentElement.style.overflow = previousOverflow
})
</script>

<style scoped>
.nc-splash {
  position: fixed;
  inset: 0;
  z-index: 99990;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  /* 与动画 / 登录页同一块暖白「技术档案」底板，首帧不闪白 */
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

.nc-splash__frame {
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

.nc-splash__frame.is-ready {
  opacity: 1;
}

.nc-splash__loading {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  pointer-events: none;
  animation: nc-splash-fade 900ms ease both;
}

.nc-splash__mark {
  font-family: 'Noto Serif SC', 'Source Han Serif SC', 'Songti SC', serif;
  font-size: 15px;
  letter-spacing: 0.34em;
  color: #6c5f52;
}

.nc-splash__hint {
  font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', monospace;
  font-size: 12px;
  letter-spacing: 0.28em;
  color: rgba(108, 95, 82, 0.62);
  animation: nc-splash-pulse 1.6s ease-in-out infinite;
}

.nc-splash__skip {
  position: absolute;
  top: 18px;
  right: 20px;
  z-index: 2;
  padding: 7px 18px;
  border: 1px solid rgba(171, 50, 39, 0.5);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.72);
  color: #ab3227;
  font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', monospace;
  font-size: 12px;
  letter-spacing: 0.22em;
  line-height: 1.4;
  cursor: pointer;
  backdrop-filter: blur(6px);
  transition:
    background-color 160ms ease,
    color 160ms ease,
    border-color 160ms ease;
}

.nc-splash__skip:hover,
.nc-splash__skip:focus-visible {
  background: #ab3227;
  border-color: #ab3227;
  color: #fff;
  outline: none;
}

@keyframes nc-splash-pulse {
  0%,
  100% {
    opacity: 0.45;
  }
  50% {
    opacity: 0.95;
  }
}

@keyframes nc-splash-fade {
  from {
    opacity: 0;
    transform: translateY(4px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

@media (max-width: 640px) {
  .nc-splash__skip {
    top: 12px;
    right: 12px;
    padding: 6px 14px;
    font-size: 11px;
    letter-spacing: 0.14em;
  }
}

@media (prefers-reduced-motion: reduce) {
  .nc-splash__loading,
  .nc-splash__hint {
    animation: none;
  }
}
</style>
