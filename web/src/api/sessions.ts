import type { OnlineSessionList } from '@/types/sessions'
import { apiHttp } from './http'

const BASE = '/system/sessions'

export function listOnlineSessions(): Promise<OnlineSessionList> {
  return apiHttp.get<OnlineSessionList>(BASE).then((response) => response.data)
}

export function kickSession(sessionId: string): Promise<{ message: string }> {
  return apiHttp
    .post<{ message: string }>(`${BASE}/${sessionId}/kick`)
    .then((response) => response.data)
}

export function kickAllSessions(): Promise<{ message: string; count: number }> {
  return apiHttp.post<{ message: string; count: number }>(`${BASE}/kick-all`).then((response) => response.data)
}
