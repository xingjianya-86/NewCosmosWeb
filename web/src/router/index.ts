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
          component: () => import('@/views/applications/ApplicationWorkflowView.vue'),
          meta: { title: '业务申请工作流' },
        },
        {
          path: 'applications/:id',
          name: 'application-detail',
          component: () => import('@/views/applications/ApplicationDetailView.vue'),
          meta: { title: '申请详情' },
        },
        {
          path: 'social-assistance',
          name: 'social-assistance-home',
          component: () => import('@/views/social-assistance/SocialAssistanceHomeView.vue'),
          meta: { title: '低收入人口救助帮扶' },
        },
        {
          path: 'social-assistance/grace-period',
          name: 'social-assistance-grace',
          component: () => import('@/views/social-assistance/GracePeriodListView.vue'),
          meta: { title: '渐退期管理' },
        },
        {
          path: 'elderly',
          name: 'elderly-home',
          component: () => import('@/views/elderly/ElderlyHomeView.vue'),
          meta: { title: '高龄津贴发放' },
        },
        {
          path: 'elderly/applications',
          name: 'elderly-applications',
          component: () => import('@/views/elderly/ElderlyApplicationListView.vue'),
          meta: { title: '津贴申请登记' },
        },
        {
          path: 'elderly/applications/:id',
          name: 'elderly-application-detail',
          component: () => import('@/views/elderly/ElderlyApplicationDetailView.vue'),
          meta: { title: '高龄津贴档案' },
        },
        {
          path: 'elderly/pending',
          name: 'elderly-pending',
          component: () => import('@/views/elderly/ElderlyPendingView.vue'),
          meta: { title: '下月待办' },
        },
        {
          path: 'elderly/reviews',
          name: 'elderly-reviews',
          component: () => import('@/views/elderly/ElderlyReviewListView.vue'),
          meta: { title: '高龄津贴复核' },
        },
        {
          path: 'asset-verification',
          name: 'asset-verification-home',
          component: () => import('@/views/asset-verification/AssetVerificationHomeView.vue'),
          meta: { title: '家庭经济状况核对' },
        },
        {
          path: 'asset-verification/tasks',
          name: 'asset-verification-tasks',
          component: () => import('@/views/asset-verification/AssetTaskListView.vue'),
          meta: { title: '核对任务' },
        },
        {
          path: 'asset-verification/monthly',
          name: 'asset-verification-monthly',
          component: () => import('@/views/asset-verification/AssetMonthlyAuditView.vue'),
          meta: { title: '月度审核' },
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
