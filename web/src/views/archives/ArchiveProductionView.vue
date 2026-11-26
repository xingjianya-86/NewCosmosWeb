<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  ArrowLeft,
  CopyDocument,
  FolderOpened,
  Refresh,
  Right,
} from '@element-plus/icons-vue'
import { getArchiveProduction } from '@/api/archiveOutput'
import { getErrorMessage } from '@/api/http'
import type { ArchiveProductionInfo } from '@/types/archiveOutput'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'

const route = useRoute()
const router = useRouter()

/* ───────────────────────── 入口参数 ───────────────────────── */

function firstQuery(key: string): string {
  const raw = route.query[key]
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' ? value.trim() : ''
}

interface ProductionContext {
  businessType: string
  businessId: number
}

/**
 * 兼容两类入口：
 * - applicationId → FamilyApplication + applicationId
 * - businessType + businessId（工作流/资产核查/月报等通用入口）
 * 显式 businessType + businessId 优先，applicationId 作为回退。
 */
const context = computed<ProductionContext | null>(() => {
  const businessType = firstQuery('businessType')
  const businessIdText = firstQuery('businessId')
  const businessId = Number(businessIdText)
  if (businessType && businessIdText && Number.isFinite(businessId) && businessId > 0) {
    return { businessType, businessId }
  }

  const applicationId = Number(firstQuery('applicationId'))
  if (Number.isFinite(applicationId) && applicationId > 0) {
    return { businessType: 'FamilyApplication', businessId: applicationId }
  }

  return null
})

/* ───────────────────────── 数据 ───────────────────────── */

const info = ref<ArchiveProductionInfo | null>(null)
const loading = ref(false)
const error = ref('')

const summaryItems = computed(() => info.value?.summary ?? [])
const sidebarGroups = computed(() => info.value?.sidebarGroups ?? [])

const totalFields = computed(() =>
  sidebarGroups.value.reduce((total, group) => total + group.fields.length, 0),
)
const autoFilledCount = computed(() =>
  sidebarGroups.value.reduce(
    (total, group) => total + group.fields.filter((field) => field.autoFilled).length,
    0,
  ),
)
const autoFillRate = computed(() => {
  if (totalFields.value === 0) return 0
  return Math.round((autoFilledCount.value / totalFields.value) * 100)
})

const dataSourceType = computed(
  () => info.value?.title || info.value?.businessType || context.value?.businessType || '—',
)

const subtitle = computed(() => {
  if (!context.value) return '请从工作流页面选择记录进入'
  if (loading.value && !info.value) return '正在加载业务数据…'
  if (error.value) return '业务数据加载失败'
  return info.value?.statusText || info.value?.title || '已加载业务数据'
})

const startDisabled = computed(() => loading.value || !info.value)

async function load(): Promise<void> {
  const current = context.value
  if (!current) {
    info.value = null
    error.value = ''
    return
  }

  loading.value = true
  error.value = ''
  try {
    info.value = await getArchiveProduction(current)
  } catch (loadError) {
    info.value = null
    error.value = getErrorMessage(loadError, '档案制作信息加载失败')
  } finally {
    loading.value = false
  }
}

/* ───────────────────────── 导航 ───────────────────────── */

function goBack(): void {
  if (window.history.length > 1) {
    router.back()
    return
  }
  goWorkflow()
}

function goWorkflow(): void {
  void router.push('/applications')
}

function goArchiveQuery(): void {
  void router.push('/archives/query')
}

function goOutput(): void {
  const current = context.value
  if (!current || !info.value) {
    ElMessage.warning('业务数据尚未加载完成')
    return
  }

  const query: Record<string, string> = {
    businessType: info.value.businessType || current.businessType,
    businessId: String(info.value.businessId || current.businessId),
  }

  // 优先使用后端返回的分类/操作覆盖；缺失时回退入口参数。
  const classification = info.value.classification || firstQuery('classification')
  if (classification) query.classification = classification

  const operationOverride = info.value.operationOverride || firstQuery('operationOverride')
  if (operationOverride) query.operationOverride = operationOverride

  void router.push({ name: 'archive-output', query })
}

/* ───────────────────────── 复制 ───────────────────────── */

function fallbackCopy(text: string): boolean {
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.top = '0'
  area.style.left = '0'
  area.style.opacity = '0'
  document.body.appendChild(area)
  try {
    area.select()
    return document.execCommand('copy')
  } finally {
    document.body.removeChild(area)
  }
}

async function copyField(label: string, value: string): Promise<void> {
  if (!value) {
    ElMessage.warning('该字段没有可复制的内容')
    return
  }

  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value)
    } else if (!fallbackCopy(value)) {
      throw new Error('copy failed')
    }
    ElMessage.success(`已复制：${label}`)
  } catch {
    ElMessage.error('复制失败，请手动选择内容复制')
  }
}

onMounted(() => {
  void load()
})

watch(
  () => route.fullPath,
  () => {
    void load()
  },
)
</script>

<template>
  <div class="nc-page archive-production">
    <NcPageHeader
      title="档案制作中心"
      :description="subtitle"
      kicker="救助档案管理 / 档案制作中心"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
      </template>
    </NcPageHeader>

    <!-- 无入口参数：保持简单提示，不再呈现旧版三栏检索 -->
    <NcCard v-if="!context" padding="lg">
      <div class="empty-state">
        <el-icon class="empty-state__icon" :size="46"><FolderOpened /></el-icon>
        <p class="empty-state__title">请从工作流页面选择记录进入</p>
        <p class="empty-state__hint">
          入口需携带 applicationId，或 businessType + businessId 参数。
        </p>
        <div class="empty-state__actions">
          <el-button type="primary" @click="goWorkflow">返回工作流</el-button>
          <el-button @click="goArchiveQuery">档案查询</el-button>
        </div>
      </div>
    </NcCard>

    <NcCard v-else-if="loading && !info" padding="lg">
      <el-skeleton :rows="6" animated />
    </NcCard>

    <template v-else>
      <el-alert
        v-if="error"
        class="panel-error"
        type="error"
        :title="error"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="load">重新加载</el-button>
      </el-alert>

      <NcCard v-if="!info && !loading" padding="lg">
        <div class="empty-state">
          <el-icon class="empty-state__icon" :size="46"><FolderOpened /></el-icon>
          <p class="empty-state__title">请从工作流页面选择记录进入</p>
          <p class="empty-state__hint">
            当前业务数据未加载成功，请返回工作流重新选择记录。
          </p>
          <div class="empty-state__actions">
            <el-button type="primary" @click="goWorkflow">返回工作流</el-button>
            <el-button @click="goArchiveQuery">档案查询</el-button>
          </div>
        </div>
      </NcCard>

      <div v-if="info" class="production-layout">
        <!-- ── 左：业务信息 / 统计 / 快捷操作 ── -->
        <div class="production-main">
          <NcCard title="业务信息" :subtitle="info.title">
            <div v-if="summaryItems.length > 0" class="summary-list">
              <div
                v-for="(item, index) in summaryItems"
                :key="`${item.label}-${index}`"
                class="summary-row"
                :class="{ 'is-odd': index % 2 === 1 }"
              >
                <span class="summary-row__label">{{ item.label }}</span>
                <span class="summary-row__value">{{ item.value || '—' }}</span>
              </div>
            </div>
            <el-empty
              v-else
              :image-size="64"
              description="暂无业务摘要"
            />
          </NcCard>

          <NcCard title="自动填充统计" subtitle="来源数据字段的自动填充情况">
            <div class="stat-grid">
              <NcStat label="字段总数" :value="totalFields" tone="ink" compact />
              <NcStat label="已自动填充" :value="autoFilledCount" tone="pine" compact />
              <NcStat label="填充比例" :value="`${autoFillRate}%`" tone="seal" compact />
            </div>
            <p class="stat-note">
              自动填充字段可直接复制到模板或业务表单；未填充字段以右侧来源数据为准。
            </p>
          </NcCard>

          <NcCard title="快捷操作">
            <div class="quick-actions">
              <el-button
                type="primary"
                :icon="Right"
                :disabled="startDisabled"
                @click="goOutput"
              >
                开始制作
              </el-button>
              <el-button @click="goArchiveQuery">档案查询</el-button>
              <el-button @click="goWorkflow">返回工作流</el-button>
            </div>
          </NcCard>
        </div>

        <!-- ── 右：来源数据边栏 ── -->
        <aside class="production-sidebar">
          <NcCard
            class="sidebar-card"
            title="来源数据"
            :subtitle="dataSourceType"
            padding="none"
          >
            <div v-if="sidebarGroups.length > 0" class="sidebar-groups">
              <section
                v-for="(group, groupIndex) in sidebarGroups"
                :key="`${group.title}-${groupIndex}`"
                class="sidebar-group"
              >
                <h3 class="sidebar-group__title">{{ group.title }}</h3>

                <div class="sidebar-fields">
                  <div
                    v-for="(field, fieldIndex) in group.fields"
                    :key="`${group.title}-${fieldIndex}-${field.label}`"
                    class="sidebar-field"
                    :class="{
                      'is-odd': fieldIndex % 2 === 1,
                      'is-auto': field.autoFilled,
                    }"
                  >
                    <span
                      class="field-flag"
                      :class="{ 'is-auto': field.autoFilled }"
                      :title="field.autoFilled ? '业务数据自动填充' : '非自动填充字段'"
                    >
                      {{ field.autoFilled ? '自动' : '—' }}
                    </span>

                    <div class="sidebar-field__body">
                      <span class="sidebar-field__label">{{ field.label }}</span>
                      <span class="sidebar-field__value">{{ field.value || '—' }}</span>
                    </div>

                    <el-button
                      class="sidebar-field__copy"
                      link
                      :icon="CopyDocument"
                      :disabled="!field.value"
                      title="复制该字段值"
                      @click="copyField(field.label, field.value)"
                    />
                  </div>
                </div>
              </section>
            </div>

            <el-empty
              v-else
              class="sidebar-empty"
              :image-size="60"
              description="暂无来源数据"
            />
          </NcCard>
        </aside>
      </div>

      <!-- ── 底部操作栏 ── -->
      <div v-if="info" class="action-bar">
        <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
        <div class="action-bar__spacer" />
        <el-button
          type="primary"
          :icon="Right"
          :disabled="startDisabled"
          @click="goOutput"
        >
          开始制作
        </el-button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.archive-production {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.panel-error {
  margin: 0;
}

/* ── 空态 ─────────────────────────────────────────────────────── */
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 36px 20px;
  text-align: center;
}

.empty-state__icon {
  color: var(--nc-muted);
}

.empty-state__title {
  margin: 6px 0 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
}

.empty-state__hint {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.empty-state__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 10px;
  margin-top: 10px;
}

/* ── 两栏工作区 ───────────────────────────────────────────────── */
.production-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 320px;
  gap: 14px;
  align-items: start;
}

.production-main {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.production-sidebar {
  min-width: 0;
}

.sidebar-card {
  overflow: hidden;
}

/* ── 业务摘要 ─────────────────────────────────────────────────── */
.summary-list {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  overflow: hidden;
}

.summary-row {
  display: grid;
  grid-template-columns: minmax(92px, 128px) minmax(0, 1fr);
  gap: 14px;
  padding: 9px 12px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.summary-row:last-child {
  border-bottom: 0;
}

.summary-row.is-odd {
  background-color: var(--nc-paper-3);
}

.summary-row__label {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.summary-row__value {
  overflow-wrap: anywhere;
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 600;
  line-height: 1.65;
}

/* ── 统计 ─────────────────────────────────────────────────────── */
.stat-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.stat-note {
  margin: 12px 0 0;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

/* ── 快捷操作 ─────────────────────────────────────────────────── */
.quick-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

/* ── 来源数据边栏 ─────────────────────────────────────────────── */
.sidebar-groups {
  display: flex;
  flex-direction: column;
}

.sidebar-group {
  border-bottom: 1px solid var(--nc-line-faint);
}

.sidebar-group:last-child {
  border-bottom: 0;
}

.sidebar-group__title {
  margin: 0;
  padding: 9px 14px;
  background-color: var(--nc-paper-3);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.sidebar-fields {
  display: flex;
  flex-direction: column;
}

.sidebar-field {
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr) auto;
  align-items: start;
  gap: 8px;
  padding: 8px 12px 8px 10px;
  border-left: 2px solid transparent;
  background-color: var(--nc-paper-soft);
}

.sidebar-field.is-odd {
  background-color: var(--nc-paper-3);
}

.sidebar-field.is-auto {
  border-left-color: var(--nc-pine);
}

.field-flag {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 30px;
  height: 19px;
  margin-top: 2px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  color: var(--nc-muted);
  font-size: 10px;
  line-height: 1;
  white-space: nowrap;
}

.field-flag.is-auto {
  border-color: rgba(46, 71, 56, 0.24);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
  font-weight: 600;
}

.sidebar-field__body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.sidebar-field__label {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.sidebar-field__value {
  overflow-wrap: anywhere;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.sidebar-field__copy {
  margin-top: -2px;
  color: var(--nc-muted);
}

.sidebar-field__copy:hover {
  color: var(--nc-seal);
}

.sidebar-empty {
  padding: 28px 12px;
}

/* ── 底部操作栏 ───────────────────────────────────────────────── */
.action-bar {
  position: sticky;
  bottom: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-sm);
}

.action-bar__spacer {
  flex: 1;
}

/* ── 响应式 ───────────────────────────────────────────────────── */
@media (max-width: 1023px) {
  .production-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .sidebar-card {
    max-height: none;
  }
}

@media (max-width: 767px) {
  .archive-production {
    gap: 10px;
  }

  .stat-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .summary-row {
    grid-template-columns: minmax(0, 1fr);
    gap: 2px;
  }

  .quick-actions {
    flex-direction: column;
  }

  .quick-actions :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }

  .action-bar {
    flex-wrap: wrap;
  }

  .action-bar :deep(.el-button) {
    flex: 1;
    margin-left: 0;
  }

  .action-bar__spacer {
    display: none;
  }
}
</style>
