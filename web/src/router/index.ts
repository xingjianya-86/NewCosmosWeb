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
          path: 'applications/new',
          name: 'application-create',
          component: () => import('@/views/applications/ApplicationFormView.vue'),
          meta: { title: '低收入人口认定办理' },
        },
        {
          path: 'applications/:id/edit',
          name: 'application-edit',
          component: () => import('@/views/applications/ApplicationFormView.vue'),
          meta: { title: '低收入人口认定办理' },
        },
        {
          path: 'applications/:id',
          name: 'application-detail',
          component: () => import('@/views/applications/ApplicationDetailView.vue'),
          meta: { title: '申请详情' },
        },
        {
          path: 'special-approvals/new',
          name: 'special-approval-new',
          component: () => import('@/views/special-approvals/SpecialApprovalView.vue'),
          meta: { title: '一事一议申报' },
        },
        {
          path: 'special-approvals/:id',
          name: 'special-approval-detail',
          component: () => import('@/views/special-approvals/SpecialApprovalView.vue'),
          meta: { title: '一事一议申报' },
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
          path: 'social-assistance/college-students',
          name: 'social-assistance-college-students',
          component: () => import('@/views/social-assistance/CollegeStudentManagementView.vue'),
          meta: { title: '大学生管理' },
        },
        {
          path: 'social-assistance/near-relatives',
          name: 'social-assistance-near-relatives',
          component: () => import('@/views/social-assistance/NearRelativeListView.vue'),
          meta: { title: '近亲属备案' },
        },
        {
          path: 'social-assistance/near-relatives/new',
          name: 'social-assistance-near-relative-new',
          component: () => import('@/views/social-assistance/NearRelativeEntryView.vue'),
          meta: { title: '近亲属备案录入' },
        },
        {
          path: 'social-assistance/near-relatives/:id/edit',
          name: 'social-assistance-near-relative-edit',
          component: () => import('@/views/social-assistance/NearRelativeEntryView.vue'),
          meta: { title: '近亲属备案编辑' },
        },
        {
          path: 'social-assistance/quick-verify',
          name: 'social-assistance-quick-verify',
          component: () => import('@/views/social-assistance/QuickVerifyView.vue'),
          meta: { title: '快速核验 · 模拟测算' },
        },
        {
          path: 'social-assistance/temp-relief',
          name: 'social-assistance-temp-relief',
          component: () => import('@/views/social-assistance/temp-relief/TempReliefListView.vue'),
          meta: { title: '临时救助' },
        },
        {
          path: 'social-assistance/temp-relief/new',
          name: 'social-assistance-temp-relief-new',
          component: () => import('@/views/social-assistance/temp-relief/TempReliefFormView.vue'),
          meta: { title: '临时救助申请' },
        },
        {
          path: 'social-assistance/temp-relief/:id/edit',
          name: 'social-assistance-temp-relief-edit',
          component: () => import('@/views/social-assistance/temp-relief/TempReliefFormView.vue'),
          meta: { title: '临时救助申请' },
        },
        {
          path: 'social-assistance/change-management',
          name: 'social-assistance-change-management',
          component: () => import('@/views/social-assistance/change/ChangeManagementView.vue'),
          meta: { title: '保障对象动态管理' },
        },
        {
          path: 'social-assistance/change-management/head/:id',
          name: 'social-assistance-head-change',
          component: () => import('@/views/social-assistance/change/HeadChangeView.vue'),
          meta: { title: '户主变更' },
        },
        {
          path: 'social-assistance/change-management/death/:id',
          name: 'social-assistance-household-death',
          component: () =>
            import('@/views/social-assistance/change/HouseholdDeathChangeView.vue'),
          meta: { title: '户主死亡变更' },
        },
        {
          path: 'social-assistance/change-management/grace/:id',
          name: 'social-assistance-grace-confirm',
          component: () =>
            import('@/views/social-assistance/change/GracePeriodConfirmView.vue'),
          meta: { title: '渐退期确认' },
        },
        {
          path: 'social-assistance/monthly-report',
          name: 'social-assistance-monthly-report',
          component: () => import('@/views/social-assistance/MonthlyReportView.vue'),
          meta: { title: '救助月报表' },
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
          path: 'elderly/applications/new',
          name: 'elderly-application-new',
          component: () => import('@/views/elderly/ElderlyApplicationFormView.vue'),
          meta: { title: '新增高龄津贴登记' },
        },
        {
          path: 'elderly/applications/:id',
          name: 'elderly-application-detail',
          component: () => import('@/views/elderly/ElderlyApplicationFormView.vue'),
          meta: { title: '高龄津贴档案' },
        },
        {
          path: 'elderly/applications/:id/edit',
          name: 'elderly-application-edit',
          component: () => import('@/views/elderly/ElderlyApplicationFormView.vue'),
          meta: { title: '编辑高龄津贴登记' },
        },
        {
          path: 'elderly/stops',
          name: 'elderly-stops',
          component: () => import('@/views/elderly/ElderlyStopListView.vue'),
          meta: { title: '停发办理' },
        },
        {
          path: 'elderly/stops/history/:historyId',
          name: 'elderly-stop-history',
          component: () => import('@/views/elderly/ElderlyStopFormView.vue'),
          meta: { title: '高龄津贴建档并停发' },
        },
        {
          path: 'elderly/stops/:id',
          name: 'elderly-stop',
          component: () => import('@/views/elderly/ElderlyStopFormView.vue'),
          meta: { title: '高龄津贴停发办理' },
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
          path: 'asset-verification/new',
          name: 'asset-verification-new',
          component: () => import('@/views/asset-verification/AssetQuickCreateView.vue'),
          meta: { title: '快速资产核查申请' },
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
          path: 'asset-verification/report',
          name: 'asset-verification-report',
          component: () => import('@/views/asset-verification/AssetMonthlyReportView.vue'),
          meta: { title: '核对月报表' },
        },
        {
          path: 'printers',
          name: 'printers',
          component: () => import('@/views/PrintersView.vue'),
          meta: { title: '打印机绑定' },
        },
        {
          path: 'archives/production',
          name: 'archive-production',
          component: () => import('@/views/archives/ArchiveProductionView.vue'),
          meta: { title: '档案制作' },
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
