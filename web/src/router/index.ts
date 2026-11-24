import { createRouter, createWebHistory } from 'vue-router'
import { APP_NAME } from '@/config/app'
import { useAuthStore } from '@/stores/auth'
import { pinia } from '@/stores'

// 页面入口固定在 .1 根路径；VITE_ASSET_BASE 只用于打包资源，不能当 Router base。
const routerBase = (import.meta.env.VITE_ROUTER_BASE || '/').trim() || '/'

const router = createRouter({
  history: createWebHistory(routerBase),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { title: '登录', public: true },
    },
    {
      path: '/',
      component: () => import('@/layouts/AppLayout.vue'),
      children: [
        {
          path: '',
          name: 'home',
          component: () => import('@/views/HomeView.vue'),
          meta: { title: '首页' },
        },
        {
          path: 'applications',
          name: 'applications',
          component: () => import('@/views/applications/ApplicationListView.vue'),
          meta: { title: '申请管理' },
        },
        {
          path: 'applications/:id',
          name: 'application-detail',
          component: () => import('@/views/applications/ApplicationDetailView.vue'),
          meta: { title: '申请详情' },
        },
        {
          path: 'printers',
          name: 'printers',
          component: () => import('@/views/PrintersView.vue'),
          meta: { title: '打印机绑定' },
        },
        {
          path: 'coming-soon/:module',
          name: 'coming-soon',
          component: () => import('@/views/ComingSoonView.vue'),
          meta: { title: '模块建设中' },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
      meta: { title: '页面不存在', public: true },
    },
  ],
  scrollBehavior: (_to, _from, savedPosition) => savedPosition ?? { top: 0 },
})

router.beforeEach((to) => {
  const auth = useAuthStore(pinia)

  if (to.meta.public) {
    // 已登录用户访问登录页时回到首页
    if (to.name === 'login' && auth.isSessionValid()) {
      return { name: 'home' }
    }
    return true
  }

  if (!auth.isSessionValid()) {
    if (auth.isAuthenticated) auth.clear()
    return {
      name: 'login',
      query: to.fullPath !== '/' ? { redirect: to.fullPath } : {},
    }
  }

  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · ${APP_NAME}` : APP_NAME
})

export default router
