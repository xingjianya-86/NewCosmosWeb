<template>
  <el-config-provider :locale="zhCn">
    <RouterView v-if="ready" />
    <LoginSequenceOverlay
      v-if="showLoginSequence"
      @done="onSequenceDone"
      @fallback="onSequenceFallback"
    />
    <SplashOverlay v-if="showSplash" @done="onSplashDone" @error="onSplashError" />
  </el-config-provider>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import LoginSequenceOverlay from '@/components/LoginSequenceOverlay.vue'
import SplashOverlay from '@/components/SplashOverlay.vue'
import { useAuthStore } from '@/stores/auth'
import { isSplashForced, isSplashSuppressed } from '@/utils/splash'

const auth = useAuthStore()
const router = useRouter()

/* 启动闸门：
   - 未登录（且未 ?nosplash=1）：先播序列登录 login.html，第 2 步输入账号密码，
     认证成功继续 3–5 步/终幕，然后进首页；失败/回落才显示经典 Vue 登录页。
   - 已登录：不播动画直接进首页；?splash=1 强制重播 splash.html 完整动画。
   - ?nosplash=1：什么都不播，直接挂载 RouterView（未登录即经典登录页）。 */
const suppressed = isSplashSuppressed()
const forced = isSplashForced()
const authenticated = auth.isSessionValid()

const showLoginSequence = ref(!suppressed && !authenticated)
const showSplash = ref(!suppressed && authenticated && forced)
const ready = ref(suppressed || (authenticated && !forced))

async function onSequenceDone(): Promise<void> {
  showLoginSequence.value = false
  try {
    /* 序列登录成功：替换到首页，避免浏览器后退回到登录态页面 */
    if (auth.isSessionValid()) await router.replace('/')
  } catch {
    // 路由失败不阻断进入应用：仍挂载 RouterView，交给路由守卫兜底
  } finally {
    ready.value = true
  }
}

/* 序列登录回落（切换经典登录 / 脚本异常 / 超时）：卸下 iframe，露出 Vue 登录页 */
function onSequenceFallback(): void {
  showLoginSequence.value = false
  ready.value = true
}

function onSplashDone(): void {
  showSplash.value = false
  ready.value = true
}

function onSplashError(): void {
  showSplash.value = false
  ready.value = true
}
</script>
