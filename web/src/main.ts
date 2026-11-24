import { createApp } from 'vue'
import App from './App.vue'
import { setUnauthorizedHandler } from './api/http'
import router from './router'
import { pinia } from './stores'
import { useAuthStore } from './stores/auth'
import './styles/index.css'

const app = createApp(App)

app.use(pinia)
app.use(router)

// 全局 401 处理：清会话 + 回登录页，并带上当前地址便于登录后返回。
setUnauthorizedHandler(() => {
  const auth = useAuthStore(pinia)
  auth.clear()

  const current = router.currentRoute.value
  if (current.name === 'login') return

  ElMessage.warning('登录状态已失效，请重新登录')
  router.replace({
    name: 'login',
    query: current.fullPath && current.fullPath !== '/' ? { redirect: current.fullPath } : {},
  })
})

app.mount('#app')
