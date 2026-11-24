import 'axios'

declare module 'axios' {
  export interface AxiosRequestConfig {
    /** 为 true 时，401 不触发全局登出/跳转（用于登录请求本身）。 */
    skipAuthRedirect?: boolean
  }
}
