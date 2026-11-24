import type { HeartbeatResponse, LoginRequest, LoginResponse, User } from '@/types/api'
import { authHttp } from './http'

export function loginApi(payload: LoginRequest): Promise<LoginResponse> {
  return authHttp
    .post<LoginResponse>('/login', payload, { skipAuthRedirect: true })
    .then((response) => response.data)
}

export function heartbeatApi(): Promise<HeartbeatResponse> {
  return authHttp.post<HeartbeatResponse>('/heartbeat').then((response) => response.data)
}

export async function logoutApi(): Promise<void> {
  await authHttp.post('/logout')
}

export function meApi(): Promise<User> {
  return authHttp.get<User>('/me').then((response) => response.data)
}
