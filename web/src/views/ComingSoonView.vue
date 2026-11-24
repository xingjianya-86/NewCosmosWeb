<script setup lang="ts">
import { computed, watchEffect } from 'vue'
import { useRoute } from 'vue-router'
import { ArrowLeft, Clock } from '@element-plus/icons-vue'
import { APP_NAME } from '@/config/app'

interface ModuleMeta {
  title: string
  category: string
  description: string
}

/*
 * 未在 Web 端实现的旧版模块 → 标题与旧文案，供占位页展示。
 * key 与侧栏/首页卡片的 /coming-soon/:module 一一对应。
 */
const moduleCatalog: Record<string, ModuleMeta> = {
  elderly: {
    title: '高龄津贴发放',
    category: '救助业务',
    description: '覆盖高龄津贴申请登记、停发办理、类别复核与月度报表',
  },
  'asset-verification': {
    title: '家庭经济状况核对',
    category: '救助业务',
    description: '覆盖快速核对、报告上传月度审核与核对月报表',
  },
  archive: {
    title: '救助档案管理',
    category: '救助业务',
    description: '现存低收入人口历史档案查询',
  },
  'death-statement': {
    title: '死亡情况说明',
    category: '救助业务',
    description: '覆盖村内死亡申报、在库身份聚合、数据提醒与情况说明出具',
  },
  recovery: {
    title: '后补追缴',
    category: '救助业务',
    description: '覆盖低保、刚性支出、临时救助与高龄停止人员的追缴办理与台账',
  },
  'photo-backfill': {
    title: '照片补传',
    category: '救助业务',
    description: '跨低收入/高龄/临时救助检索人员已传照片数，勾选后连续采集证件照并可推送打印',
  },
  database: {
    title: '数据中心',
    category: '管理工具',
    description: '数据库管理、数据字典、标准配置、数据导入与区域数据维护',
  },
  users: {
    title: '用户与权限管理',
    category: '管理工具',
    description: '系统用户、角色权限与经办机构信息维护（经办机构亦可从左侧菜单进入）',
  },
  organizations: {
    title: '经办机构管理',
    category: '管理工具',
    description: '系统用户、角色权限与经办机构信息维护（经办机构亦可从左侧菜单进入）',
  },
  templates: {
    title: '文书模板管理',
    category: '管理工具',
    description: '公文文书模板维护与字段绑定',
  },
  duty: {
    title: '值班管理',
    category: '管理工具',
    description: '值班表轮转生成、请假/串班/代班调整、节假日维护与政务值班表导出',
  },
  reprint: {
    title: '历史补打中心',
    category: '管理工具',
    description:
      '跨域按人检索低保/临时救助/普惠高龄/资产核查/动态管理档案，支持留痕原样补打与按月批量打印',
  },
  lottery: {
    title: '彩票助手',
    category: '娱乐天地',
    description: '双色球/大乐透历史数据、号码统计分析与智能预测',
  },
  settings: {
    title: '系统设置',
    category: '系统',
    description: '文档输出位置等本机配置',
  },
}

const route = useRoute()

const moduleKey = computed(() => String(route.params.module ?? ''))
const meta = computed<ModuleMeta>(
  () =>
    moduleCatalog[moduleKey.value] ?? {
      title: '功能模块',
      category: '未分类',
      description: '该模块尚未在 Web 端开放。',
    },
)

watchEffect(() => {
  document.title = `${meta.value.title} · ${APP_NAME}`
})
</script>

<template>
  <div class="nc-page coming-soon">
    <RouterLink to="/" class="back-link">
      <el-icon :size="14"><ArrowLeft /></el-icon>
      返回首页
    </RouterLink>

    <section class="placeholder">
      <span class="placeholder-stamp">{{ meta.category }}</span>
      <span class="placeholder-icon">
        <el-icon :size="26"><Clock /></el-icon>
      </span>
      <p class="placeholder-kicker">MODULE / COMING SOON</p>
      <h1 class="placeholder-title">{{ meta.title }}</h1>
      <p class="placeholder-desc">{{ meta.description }}</p>

      <div class="placeholder-note">
        <strong>该模块尚未在 Web 端开放</strong>
        <p>当前请使用桌面端办理；Web 端将按业务优先级逐步迁移。</p>
      </div>

      <div class="placeholder-actions">
        <RouterLink to="/" class="primary-link">返回工作台</RouterLink>
        <RouterLink to="/applications" class="plain-link">前往申请管理</RouterLink>
      </div>
    </section>
  </div>
</template>

<style scoped>
.coming-soon {
  max-width: 880px;
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 16px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.04em;
}

.back-link:hover {
  color: var(--nc-seal);
}

.placeholder {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  overflow: hidden;
  padding: 48px 28px 40px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
  text-align: center;
}

.placeholder::before {
  content: '';
  position: absolute;
  top: 0;
  right: 0;
  left: 0;
  height: 3px;
  background-color: var(--nc-seal);
}

.placeholder-stamp {
  padding: 1px 8px;
  border: 1px solid rgba(171, 50, 39, 0.5);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.18em;
}

.placeholder-icon {
  display: grid;
  place-items: center;
  width: 56px;
  height: 56px;
  margin-top: 20px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper);
  color: var(--nc-seal);
}

.placeholder-kicker {
  margin-top: 18px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.22em;
}

.placeholder-title {
  margin-top: 6px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

.placeholder-desc {
  max-width: 520px;
  margin-top: 10px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.8;
}

.placeholder-note {
  width: 100%;
  max-width: 520px;
  margin-top: 24px;
  padding: 16px 18px;
  border: 1px solid var(--nc-line-faint);
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  text-align: left;
}

.placeholder-note strong {
  display: block;
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-base);
}

.placeholder-note p {
  margin-top: 4px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.placeholder-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 12px;
  margin-top: 26px;
}

.primary-link,
.plain-link {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 132px;
  height: 44px;
  padding: 0 20px;
  border-radius: 2px;
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.04em;
}

.primary-link {
  background-color: var(--nc-seal);
  color: #ffffff;
}

.primary-link:hover {
  background-color: var(--nc-seal-hover);
}

.plain-link {
  border: 1px solid var(--nc-line-strong);
  color: var(--nc-ink-2);
}

.plain-link:hover {
  border-color: var(--nc-seal);
  color: var(--nc-seal);
}

@media (max-width: 767px) {
  .placeholder {
    padding: 34px 18px 30px;
  }

  .placeholder-title {
    font-size: 21px;
  }

  .primary-link,
  .plain-link {
    width: 100%;
  }
}
</style>
