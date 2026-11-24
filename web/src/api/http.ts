import axios from 'axios'
import type { ApiErrorBody } from '@/types/api'
import { getDeviceId } from '@/utils/device'
import { clearStoredSession, getStoredSessionId } from './session'

const DEFAULT_API_BASE = 'https://cosmos-static.sua17.cn:20000'

function stripTrailingSlash(value: string): string {
  return value.replace(/\/+$/, '')
}

// 开发模式下 VITE_API_BASE 留空，改由 Vite proxy 转发到 127.0.0.1:8787。
export const API_BASE =
  stripTrailingSlash((import.meta.env.VITE_API_BASE ?? '').trim()) ||
  (import.meta.env.PROD ? DEFAULT_API_BASE : '')

// 认证同源：留空时使用相对路径 /api/auth。
export const AUTH_BASE = stripTrailingSlash((import.meta.env.VITE_AUTH_BASE ?? '').trim())

export const API_PREFIX = `${API_BASE}/api`
export const AUTH_PREFIX = `${AUTH_BASE}/api/auth`

const STATUS_MESSAGES: Record<number, string> = {
  400: '请求参数有误，请检查后重试',
  401: '登录状态已失效，请重新登录',
  403: '没有权限执行该操作',
  404: '请求的资源不存在',
  409: '数据状态冲突，请刷新后重试',
  422: '数据校验未通过',
  429: '操作过于频繁，请稍后再试',
  500: '服务器内部错误，请稍后重试',
  502: '服务暂时不可用，请稍后重试',
  503: '服务暂时不可用，请稍后重试',
  504: '服务响应超时，请稍后重试',
}

export interface ApiErrorOptions {
  code: string
  status: number
  traceId?: string
}

export class ApiError extends Error {
  readonly code: string
  readonly status: number
  readonly traceId?: string

  constructor(message: string, options: ApiErrorOptions) {
    super(message)
    this.name = 'ApiError'
    this.code = options.code
    this.status = options.status
    this.traceId = options.traceId
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }
}

function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error

  if (axios.isAxiosError(error)) {
    const status = error.response?.status ?? 0
    const body = error.response?.data as ApiErrorBody | string | undefined

    let code = status ? `HTTP_${status}` : 'NETWORK_ERROR'
    let message = ''
    let traceId: string | undefined

    if (body && typeof body === 'object') {
      if (typeof body.code === 'string' && body.code) code = body.code
      if (typeof body.message === 'string' && body.message) message = body.message
      if (typeof body.traceId === 'string' && body.traceId) traceId = body.traceId
    } else if (typeof body === 'string' && body.trim()) {
      message = body.trim()
    }

    if (!message) {
      if (!error.response) {
        message =
          error.code === 'ECONNABORTED'
            ? '请求超时，请稍后重试'
            : '网络连接失败，请检查网络后重试'
      } else {
        message = STATUS_MESSAGES[status] ?? `请求失败（HTTP ${status}）`
      }
    }

    return new ApiError(message, { code, status, traceId })
  }

  if (error instanceof Error && error.message) {
    return new ApiError(error.message, { code: 'UNKNOWN', status: 0 })
  }

  return new ApiError('操作失败，请稍后重试', { code: 'UNKNOWN', status: 0 })
}

export function getErrorMessage(error: unknown, fallback = '操作失败，请稍后重试'): string {
  if (error instanceof ApiError && error.message) return error.message
  if (error instanceof Error && error.message) return error.message
  return fallback
}

let unauthorizedHandler: (() => void) | null = null
let unauthorizedLock = false

/** 由 main.ts 注入：清空 Pinia 会话并跳转登录页。 */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler
}

function handleUnauthorized(): void {
  if (unauthorizedLock) return
  unauthorizedLock = true
  clearStoredSession()
  try {
    unauthorizedHandler?.()
  } finally {
    // 多个并发请求同时 401 时只处理一次
    window.setTimeout(() => {
      unauthorizedLock = false
    }, 1000)
  }
}

function createClient(baseURL: string) {
  const instance = axios.create({
    baseURL,
    timeout: 20_000,
    headers: { Accept: 'application/json' },
  })

  instance.interceptors.request.use((config) => {
    const sessionId = getStoredSessionId()
    if (sessionId) {
      config.headers.set('Authorization', `Bearer ${sessionId}`)
    }
    config.headers.set('X-NC-Device', getDeviceId())
    return config
  })

  instance.interceptors.response.use(
    (response) => response,
    (error: unknown) => {
      const apiError = toApiError(error)
      const config = axios.isAxiosError(error) ? error.config : undefined
      if (apiError.status === 401 && !config?.skipAuthRedirect) {
        handleUnauthorized()
      }
      return Promise.reject(apiError)
    },
  )

  return instance
}

/** 认证请求：VITE_AUTH_BASE + /api/auth（留空即同源）。 */
export const authHttp = createClient(AUTH_PREFIX)

/** 业务请求：VITE_API_BASE + /api。 */
export const apiHttp = createClient(API_PREFIX)
