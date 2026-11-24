import type { User } from '@/types/api'

const SESSION_KEY = 'nc.web.session'

export interface StoredSession {
  sessionId: string
  expiresAt: string
  user: User | null
}

export function readStoredSession(): StoredSession | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<StoredSession> | null
    if (!parsed || typeof parsed.sessionId !== 'string' || !parsed.sessionId) return null
    if (typeof parsed.expiresAt !== 'string' || !parsed.expiresAt) return null
    return {
      sessionId: parsed.sessionId,
      expiresAt: parsed.expiresAt,
      user: parsed.user ?? null,
    }
  } catch {
    return null
  }
}

export function writeStoredSession(session: StoredSession): void {
  try {
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
  } catch {
    // 存储不可用时保持内存状态，不阻断业务
  }
}

export function clearStoredSession(): void {
  try {
    sessionStorage.removeItem(SESSION_KEY)
  } catch {
    // ignore
  }
}

export function getStoredSessionId(): string | null {
  return readStoredSession()?.sessionId ?? null
}

/** expiresAt 无效时按有效处理，避免服务端时间格式差异导致误登出。 */
export function isSessionExpired(session: Pick<StoredSession, 'expiresAt'> | null, now = Date.now()): boolean {
  if (!session) return true
  const expires = Date.parse(session.expiresAt)
  if (!Number.isFinite(expires)) return false
  return expires <= now
}
