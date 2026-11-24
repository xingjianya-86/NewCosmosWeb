<template>
  <RouterView v-if="ready" />
  <SplashOverlay v-if="showSplash" @done="onSplashDone" />
</template>

<script setup lang="ts">
import { ref } from 'vue'
import SplashOverlay from '@/components/SplashOverlay.vue'
import { useAuthStore } from '@/stores/auth'
import { isSplashSuppressed } from '@/utils/splash'

const auth = useAuthStore()

/* 启动闸门：
   已登录 = 直接进应用，不播动画；
   未登录 = 动画播完再挂载 RouterView（登录页），避免页面先闪一下。 */
const shouldPlaySplash = !auth.isSessionValid() && !isSplashSuppressed()
const showSplash = ref(shouldPlaySplash)
const ready = ref(!shouldPlaySplash)

function onSplashDone(): void {
  showSplash.value = false
  ready.value = true
}
</script>
