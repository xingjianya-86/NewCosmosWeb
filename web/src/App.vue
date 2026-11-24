<template>
  <RouterView v-if="ready" />
  <SplashOverlay v-if="showSplash" @done="onSplashDone" />
</template>

<script setup lang="ts">
import { ref } from 'vue'
import SplashOverlay from '@/components/SplashOverlay.vue'
import { useAuthStore } from '@/stores/auth'
import { isSplashForced, isSplashSuppressed } from '@/utils/splash'

const auth = useAuthStore()

/* 启动闸门：
   已登录 = 直接进应用，不播动画（?splash=1 可强制重播）；
   未登录 = 每次启动都播动画，播完再挂载 RouterView（登录页），避免页面先闪一下。 */
const shouldPlaySplash =
  (!auth.isSessionValid() || isSplashForced()) && !isSplashSuppressed()
const showSplash = ref(shouldPlaySplash)
const ready = ref(!shouldPlaySplash)

function onSplashDone(): void {
  showSplash.value = false
  ready.value = true
}
</script>
