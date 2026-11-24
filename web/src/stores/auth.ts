import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { heartbeatApi, loginApi, logoutApi, meApi } from '@/api/auth'
import { ApiError } from '@/api/http'
import {
  clearStoredSession,
  isSessionExpired,
  readStoredSession,
  writeStoredSession,
  type StoredSession,
} from '@/api/session'
import { APP_VERSION } from '@/config/app'
import type { User } from '@/types/api'
import { currentDeviceKind, describeDevice, getDeviceId } from '@/utils/device'

export const useAuthStore = defineStore('auth', () => {
  const session = ref<StoredSession | null>(readStoredSession())
  const deviceId = ref(getDeviceId())

  const sessionId = computed(() => session.value?.sessionId ?? '')
  const user = computed<User | null>(() => session.value?.user ?? null)
  const expiresAt = computed(() => session.value?.expiresAt ?? '')
  const isAuthenticated = computed(() => Boolean(session.value?.sessionId))
  const deviceKind = computed(() => currentDeviceKind())
  const displayName = computed(() => user.value?.fullName || user.value?.username || '未登录')

  function isSessionValid(now = Date.now()): boolean {
    return Boolean(session.value?.sessionId) && !isSessionExpired(session.value, now)
  }

  function setSession(next: StoredSession): void {
    session.value = next
    writeStoredSession(next)
  }

  function clear(): void {
    session.value = null
    clearStoredSession()
  }

  async function login(username: string, password: string): Promise<void> {
    const result = await loginApi({
      username,
      password,
      deviceId: deviceId.value,
      deviceKind: currentDeviceKind(),
      deviceName: describeDevice(),
      appVersion: APP_VERSION,
    })
    setSession({
      sessionId: result.sessionId,
      expiresAt: result.expiresAt,
      user: result.user,
    })
  }

  /** 心跳成功后顺带刷新过期时间；401 时清理本地会话。 */
  async function heartbeat(): Promise<boolean> {
    if (!session.value) return false
    try {
      const result = await heartbeatApi()
      if (result?.expiresAt && session.value) {
        setSession({ ...session.value, expiresAt: result.expiresAt })
      }
      return result?.active !== false
    } catch (error) {
      if (error instanceof ApiError && error.isUnauthorized) {
        clear()
      }
      throw error
    }
  }

  async function me(): Promise<User | null> {
    if (!session.value) return null
    try {
      const currentUser = await meApi()
      if (session.value) {
        setSession({ ...session.value, user: currentUser })
      }
      return currentUser
    } catch (error) {
      if (error instanceof ApiError && error.isUnauthorized) {
        clear()
      }
      throw error
    }
  }

  async function logout(): Promise<void> {
    try {
      if (session.value) await logoutApi()
    } catch {
      // 服务端不可达时也允许本地退出
    } finally {
      clear()
    }
  }

  return {
    session,
    sessionId,
    expiresAt,
    user,
    deviceId,
    deviceKind,
    displayName,
    isAuthenticated,
    isSessionValid,
    login,
    heartbeat,
    me,
    logout,
    clear,
  }
})
