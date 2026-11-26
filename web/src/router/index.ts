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
          path: 'social-assistance/temp-relief/:id/output',
          name: 'social-assistance-temp-relief-output',
          component: () => import('@/views/social-assistance/temp-relief/TempReliefOutputView.vue'),
          meta: { title: '临时救助档案输出' },
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
          path: 'elderly/reports',
          name: 'elderly-reports',
          component: () => import('@/views/elderly/ElderlyMonthlyReportView.vue'),
          meta: { title: '高龄津贴月报表' },
        },
        {
          path: 'recovery',
          name: 'recovery',
          component: () => import('@/views/recovery/RecoveryListView.vue'),
          meta: { title: '后补追缴 - 搜索停止人员' },
        },
        {
          path: 'recovery/form',
          name: 'recovery-form',
          component: () => import('@/views/recovery/RecoveryFormView.vue'),
          meta: { title: '后补追缴 - 填写追缴信息' },
        },
        {
          path: 'recovery/manual',
          name: 'recovery-manual',
          component: () => import('@/views/recovery/RecoveryManualView.vue'),
          meta: { title: '后补追缴 - 手工录入' },
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
          path: 'reprint',
          name: 'reprint',
          component: () => import('@/views/reprint/ReprintView.vue'),
          meta: { title: '历史补打中心' },
        },
        {
          path: 'printers',
          name: 'printers',
          component: () => import('@/views/PrintersView.vue'),
          meta: { title: '打印机绑定' },
        },
        {
          path: 'archives/query',
          name: 'archive-query',
          component: () => import('@/views/archives/ArchiveQueryView.vue'),
          meta: { title: '救助档案管理' },
        },
        {
          path: 'archives/proof-templates',
          name: 'archive-proof-templates',
          component: () => import('@/views/archives/ProofUnitTemplateView.vue'),
          meta: { title: '单位模板维护' },
        },
        {
          path: 'archives/photos',
          name: 'archive-photos',
          component: () => import('@/views/archives/ArchivePhotoView.vue'),
          meta: { title: '档案照片查询' },
        },
        {
          path: 'photo-backfill',
          name: 'photo-backfill',
          component: () => import('@/views/photos/PhotoCenterView.vue'),
          meta: { title: '照片中心' },
        },
        {
          path: 'system/data-center',
          name: 'data-center',
          component: () => import('@/views/system/data-center/DataCenterHomeView.vue'),
          meta: { title: '数据中心' },
        },
        {
          path: 'system/data-center/import',
          name: 'data-center-import',
          component: () => import('@/views/system/data-center/DataImportView.vue'),
          meta: { title: '数据导入' },
        },
        {
          path: 'system/data-center/dictionary',
          name: 'data-center-dictionary',
          component: () => import('@/views/system/data-center/DictionaryManageView.vue'),
          meta: { title: '数据字典管理' },
        },
        {
          path: 'system/data-center/standards',
          name: 'data-center-standards',
          component: () => import('@/views/system/data-center/StandardConfigView.vue'),
          meta: { title: '标准配置管理' },
        },
        {
          path: 'system/data-center/regions',
          name: 'data-center-regions',
          component: () => import('@/views/system/data-center/RegionManageView.vue'),
          meta: { title: '行政区划管理' },
        },
        {
          path: 'system/data-center/bank-flow',
          name: 'data-center-bank-flow',
          component: () => import('@/views/system/data-center/BankFlowView.vue'),
          meta: { title: '银行流水核对' },
        },
        {
          path: 'system/organizations',
          name: 'system-organizations',
          component: () => import('@/views/system/organizations/OrganizationView.vue'),
          meta: { title: '经办机构管理' },
        },
        {
          path: 'system/organizations/import',
          name: 'system-organizations-import',
          component: () => import('@/views/system/organizations/OrganizationImportView.vue'),
          meta: { title: '组织数据导入' },
        },
        {
          path: 'system/users',
          name: 'system-users',
          component: () => import('@/views/system/users/UserManagementView.vue'),
          meta: { title: '用户与权限管理' },
        },
        {
          path: 'system/users/online',
          name: 'system-users-online',
          component: () => import('@/views/system/users/OnlineSessionsView.vue'),
          meta: { title: '在线会话' },
        },
        {
          path: 'archives/publicity',
          name: 'archive-publicity',
          component: () => import('@/views/archives/PublicityView.vue'),
          meta: { title: '每月公示文档输出' },
        },
        {
          path: 'archives/templates',
          name: 'archive-templates',
          component: () => import('@/views/archives/TemplateAdminView.vue'),
          meta: { title: '文书模板管理' },
        },
        {
          path: 'archives/production',
          name: 'archive-production',
          component: () => import('@/views/archives/ArchiveProductionView.vue'),
          meta: { title: '档案制作' },
        },
        {
          path: 'archives/output',
          name: 'archive-output',
          component: () => import('@/views/archives/ArchiveOutputView.vue'),
          meta: { title: '档案输出' },
        },
        {
          path: 'archives/death-statements',
          name: 'archive-death-statements',
          component: () => import('@/views/archives/DeathStatementView.vue'),
          meta: { title: '死亡情况说明' },
        },
        {
          path: 'duty',
          name: 'duty',
          component: () => import('@/views/duty/DutyScheduleView.vue'),
          meta: { title: '值班管理' },
        },
        {
          path: 'duty/members',
          name: 'duty-members',
          component: () => import('@/views/duty/DutyMemberView.vue'),
          meta: { title: '值班成员管理' },
        },
        {
          path: 'duty/adjustments',
          name: 'duty-adjustments',
          component: () => import('@/views/duty/DutyAdjustView.vue'),
          meta: { title: '班务调整管理' },
        },
        {
          path: 'duty/holidays',
          name: 'duty-holidays',
          component: () => import('@/views/duty/HolidayView.vue'),
          meta: { title: '节假日维护' },
        },
        {
          path: 'lottery',
          name: 'lottery',
          component: () => import('@/views/lottery/LotteryHomeView.vue'),
          meta: { title: '彩票助手' },
        },
        {
          path: 'lottery/prediction',
          name: 'lottery-prediction',
          component: () => import('@/views/lottery/LotteryPredictionView.vue'),
          meta: { title: '智能预测' },
        },
        {
          path: 'lottery/history',
          name: 'lottery-history',
          component: () => import('@/views/lottery/LotteryHistoryView.vue'),
          meta: { title: '历史开奖' },
        },
        {
          path: 'lottery/result',
          name: 'lottery-result',
          component: () => import('@/views/lottery/LotteryResultView.vue'),
          meta: { title: '中奖结果' },
        },
        {
          path: 'settings',
          name: 'settings',
          component: () => import('@/views/SettingsView.vue'),
          meta: { title: '系统设置' },
        },
        {
          path: 'coming-soon/reprint',
          redirect: '/reprint',
        },
        {
          path: 'coming-soon/organizations',
          redirect: '/system/organizations',
        },
        {
          path: 'coming-soon/lottery',
          redirect: '/lottery',
        },
        {
          path: 'coming-soon/duty',
          redirect: '/duty',
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
