<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Upload } from '@element-plus/icons-vue'
import {
  createStandard,
  deleteStandard,
  getStandardOverview,
  listStandards,
  seedStandardsFromYaml,
  syncStandardSchema,
  updateStandard,
} from '@/api/standards'
import { ApiError, getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import {
  HUKOU_TYPE_OPTIONS,
  STANDARD_CATEGORIES,
  SUPPORT_MODE_OPTIONS,
} from '@/types/standards'
import type {
  StandardCategoryDefinition,
  StandardConfig,
  StandardConfigRequest,
  StandardOverview,
} from '@/types/standards'

const isNarrow = useMediaQuery('(max-width: 960px)')

const standards = ref<StandardConfig[]>([])
const overview = ref<StandardOverview | null>(null)
const selectedCategoryKey = ref(STANDARD_CATEGORIES[0]?.key ?? '')
const loading = ref(false)
const loadError = ref('')
const overviewError = ref('')
const syncing = ref(false)
const seeding = ref(false)

const selectedCategory = computed<StandardCategoryDefinition | null>(
  () => STANDARD_CATEGORIES.find((item) => item.key === selectedCategoryKey.value) ?? null,
)

const filteredStandards = computed<StandardConfig[]>(() => {
  const category = selectedCategory.value
  if (!category) return []
  return standards.value.filter((row) => category.standardTypes.includes(row.standardType))
})

/* ───────────────────────────── 格式化 ───────────────────────────── */

function formatAmount(value: number | null | undefined): string {
  return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(2) : '—'
}

/** 日期列只取日期部分，避免时区偏移。 */
function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return value.slice(0, 10)
}

function formatDateRange(row: StandardConfig): string {
  const end = row.effectiveEndDate ? formatDate(row.effectiveEndDate) : '长期'
  return `${formatDate(row.effectiveStartDate)} ~ ${end}`
}

function hukouLabel(value: string | null): string {
  if (!value) return '—'
  return HUKOU_TYPE_OPTIONS.find((item) => item.value === value)?.label ?? value
}

function supportModeLabel(value: string | null): string {
  if (!value) return '—'
  return SUPPORT_MODE_OPTIONS.find((item) => item.value === value)?.label ?? value
}

function categoryCount(category: StandardCategoryDefinition): number {
  return standards.value.filter((row) => category.standardTypes.includes(row.standardType)).length
}

function todayString(): string {
  const now = new Date()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

/* ───────────────────────────── 加载 ───────────────────────────── */

async function loadOverview(): Promise<void> {
  overviewError.value = ''
  try {
    overview.value = await getStandardOverview()
  } catch (error) {
    overviewError.value = getErrorMessage(error, '标准配置概览加载失败')
  }
}

async function loadStandards(): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    standards.value = await listStandards()
  } catch (error) {
    loadError.value = getErrorMessage(error, '标准配置加载失败')
  } finally {
    loading.value = false
  }
}

async function loadAll(): Promise<void> {
  await Promise.all([loadOverview(), loadStandards()])
}

/* ───────────────────────────── 表单 ───────────────────────────── */

interface StandardFormState {
  standardType: string
  standardName: string
  standardValue: number | null
  unit: string
  hukouType: string | null
  supportMode: string | null
  effectiveStartDate: string | null
  effectiveEndDate: string | null
  version: number
  isActive: boolean
  description: string
}

const standardDialog = reactive({
  visible: false,
  editingId: 0,
  saving: false,
})

const standardForm = reactive<StandardFormState>({
  standardType: '',
  standardName: '',
  standardValue: 0,
  unit: '',
  hukouType: null,
  supportMode: null,
  effectiveStartDate: todayString(),
  effectiveEndDate: null,
  version: 1,
  isActive: true,
  description: '',
})

const hukouOptions = computed(() => {
  const list = [...HUKOU_TYPE_OPTIONS]
  const current = standardForm.hukouType
  if (current && !list.some((item) => item.value === current)) {
    list.push({ label: `其他（${current}）`, value: current })
  }
  return list
})

const supportModeOptions = computed(() => {
  const list = [...SUPPORT_MODE_OPTIONS]
  const current = standardForm.supportMode
  if (current && !list.some((item) => item.value === current)) {
    list.push({ label: `其他（${current}）`, value: current })
  }
  return list
})

function resetForm(): void {
  standardForm.standardType = selectedCategory.value?.standardTypes[0] ?? ''
  standardForm.standardName = ''
  standardForm.standardValue = 0
  standardForm.unit = ''
  standardForm.hukouType = null
  standardForm.supportMode = null
  standardForm.effectiveStartDate = todayString()
  standardForm.effectiveEndDate = null
  standardForm.version = 1
  standardForm.isActive = true
  standardForm.description = ''
}

function openCreate(): void {
  if (!selectedCategory.value) return
  standardDialog.editingId = 0
  resetForm()
  standardDialog.visible = true
}

function openEdit(row: StandardConfig): void {
  standardDialog.editingId = row.id
  standardForm.standardType = row.standardType
  standardForm.standardName = row.standardName
  standardForm.standardValue = row.standardValue
  standardForm.unit = row.unit
  standardForm.hukouType = row.hukouType
  standardForm.supportMode = row.supportMode
  standardForm.effectiveStartDate = formatDate(row.effectiveStartDate)
  standardForm.effectiveEndDate = row.effectiveEndDate ? formatDate(row.effectiveEndDate) : null
  standardForm.version = row.version
  standardForm.isActive = row.isActive
  standardForm.description = row.description ?? ''
  standardDialog.visible = true
}

function disableEndDate(date: Date): boolean {
  const start = standardForm.effectiveStartDate
  if (!start) return false
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}` < start
}

function validateForm(): string {
  if (!standardForm.standardType) return '请选择标准类型'
  if (!standardForm.standardName.trim()) return '请输入标准名称'

  const value = standardForm.standardValue
  if (value === null || !Number.isFinite(value) || value < 0) {
    return '请输入不小于 0 的标准值'
  }
  if (!standardForm.effectiveStartDate) return '请选择生效开始日期'
  if (
    standardForm.effectiveEndDate &&
    standardForm.effectiveEndDate < standardForm.effectiveStartDate
  ) {
    return '生效结束日期不能早于生效开始日期'
  }
  if (!Number.isFinite(standardForm.version) || standardForm.version <= 0) {
    return '版本号必须大于 0'
  }
  return ''
}

async function saveStandard(): Promise<void> {
  const validation = validateForm()
  if (validation) {
    ElMessage.warning(validation)
    return
  }

  const value = standardForm.standardValue
  const startDate = standardForm.effectiveStartDate
  if (value === null || startDate === null) return

  const payload: StandardConfigRequest = {
    standardType: standardForm.standardType,
    standardName: standardForm.standardName.trim(),
    hukouType: standardForm.hukouType || null,
    supportMode: standardForm.supportMode || null,
    standardValue: value,
    unit: standardForm.unit.trim(),
    effectiveStartDate: startDate,
    effectiveEndDate: standardForm.effectiveEndDate || null,
    version: standardForm.version,
    isActive: standardForm.isActive,
    description: standardForm.description.trim(),
  }

  standardDialog.saving = true
  try {
    if (standardDialog.editingId > 0) {
      const result = await updateStandard(standardDialog.editingId, payload)
      ElMessage.success(result.message || '标准配置已更新')
    } else {
      const result = await createStandard(payload)
      ElMessage.success(result.message || '标准配置已创建')
    }
    standardDialog.visible = false
    await loadAll()
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      ElMessage.error('该标准已被他人修改，请刷新后重试')
      standardDialog.visible = false
      await loadAll()
    } else {
      ElMessage.error(getErrorMessage(error, '保存标准配置失败'))
    }
  } finally {
    standardDialog.saving = false
  }
}

async function handleDelete(row: StandardConfig): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认删除标准「${row.standardName}」？删除后该标准将停用并从此列表移除。`,
      '删除标准配置',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const result = await deleteStandard(row.id)
    ElMessage.success(result.message || '标准配置已删除')
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除标准配置失败'))
  }
}

/* ───────────────────────────── 同步 / 初始化 ───────────────────────────── */

async function handleSyncSchema(): Promise<void> {
  syncing.value = true
  try {
    const result = await syncStandardSchema()
    ElMessage.success(result.message || '标准配置表结构同步完成')
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '同步标准配置表结构失败'))
  } finally {
    syncing.value = false
  }
}

async function handleSeed(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将从 YAML 种子重新导入标准配置：现有标准配置数据会被清空并覆盖，此操作不可撤销。是否继续？',
      '从种子初始化',
      { confirmButtonText: '继续初始化', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  seeding.value = true
  try {
    const result = await seedStandardsFromYaml()
    ElMessage.success(result.message || '标准配置数据初始化完成')
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '标准配置初始化失败'))
  } finally {
    seeding.value = false
  }
}

onMounted(() => {
  void loadAll()
})
</script>

<template>
  <div class="nc-page standards-page">
    <NcPageHeader
      title="标准配置管理"
      description="管理低保、特困、补贴等政策标准配置"
      kicker="系统 · 数据中心"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="syncing" @click="handleSyncSchema">
          同步表结构
        </el-button>
        <el-button type="primary" plain :icon="Upload" :loading="seeding" @click="handleSeed">
          从种子初始化
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadAll">刷新</el-button>
      </template>
    </NcPageHeader>

    <!-- 概览 -->
    <el-skeleton v-if="loading && !overview" :rows="2" animated />
    <div v-else class="overview-grid">
      <article class="overview-card">
        <span>农村低保标准</span>
        <strong>{{ formatAmount(overview?.ruralIncomeStandard) }}</strong>
        <small>元 · 来自最低生活保障标准</small>
      </article>
      <article class="overview-card">
        <span>城市低保标准</span>
        <strong>{{ formatAmount(overview?.urbanIncomeStandard) }}</strong>
        <small>元 · 来自最低生活保障标准</small>
      </article>
      <article class="overview-card">
        <span>农村补贴</span>
        <strong>{{ formatAmount(overview?.ruralSubsidyAmount) }}</strong>
        <small>元 · 分类补贴标准</small>
      </article>
      <article class="overview-card">
        <span>城市补贴</span>
        <strong>{{ formatAmount(overview?.urbanSubsidyAmount) }}</strong>
        <small>元 · 分类补贴标准</small>
      </article>
    </div>

    <el-alert
      v-if="overviewError"
      class="page-error"
      type="warning"
      :title="overviewError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadOverview">重新加载概览</el-button>
    </el-alert>

    <el-alert
      v-if="loadError"
      class="page-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadAll">重新加载</el-button>
    </el-alert>

    <div class="standards-layout">
      <!-- 固定分类 -->
      <NcCard title="标准类别" class="category-panel">
        <div class="category-list" role="listbox" aria-label="标准类别">
          <button
            v-for="category in STANDARD_CATEGORIES"
            :key="category.key"
            type="button"
            role="option"
            class="category-item"
            :class="{ 'is-active': category.key === selectedCategoryKey }"
            :aria-selected="category.key === selectedCategoryKey"
            @click="selectedCategoryKey = category.key"
          >
            <span class="category-name">{{ category.displayName }}</span>
            <span class="category-count">{{ categoryCount(category) }} 项</span>
          </button>
        </div>
      </NcCard>

      <!-- 标准列表 -->
      <NcCard
        :title="selectedCategory ? `标准列表 - ${selectedCategory.displayName}` : '标准列表'"
        class="list-panel"
      >
        <template #actions>
          <el-button
            size="small"
            type="primary"
            :icon="Plus"
            :disabled="!selectedCategory"
            @click="openCreate"
          >
            新增标准
          </el-button>
        </template>

        <el-skeleton v-if="loading && standards.length === 0" :rows="5" animated />

        <el-empty
          v-else-if="filteredStandards.length === 0"
          :description="`「${selectedCategory?.displayName ?? ''}」暂无标准配置`"
          :image-size="80"
        >
          <el-button type="primary" plain :icon="Plus" @click="openCreate">
            新增标准
          </el-button>
        </el-empty>

        <el-table
          v-else
          v-loading="loading"
          :data="filteredStandards"
          row-key="id"
          size="small"
          class="standard-table"
        >
          <el-table-column label="标准类型" min-width="190" show-overflow-tooltip>
            <template #default="{ row }">
              <span class="mono">{{ row.standardType }}</span>
            </template>
          </el-table-column>
          <el-table-column label="标准名称" min-width="170" show-overflow-tooltip>
            <template #default="{ row }">
              <span class="standard-name">{{ row.standardName }}</span>
            </template>
          </el-table-column>
          <el-table-column label="户口类型" width="92">
            <template #default="{ row }">{{ hukouLabel(row.hukouType) }}</template>
          </el-table-column>
          <el-table-column label="供养方式" width="100">
            <template #default="{ row }">{{ supportModeLabel(row.supportMode) }}</template>
          </el-table-column>
          <el-table-column label="标准值" width="112" align="right">
            <template #default="{ row }">
              <span class="amount">{{ formatAmount(row.standardValue) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="单位" width="86">
            <template #default="{ row }">{{ row.unit || '—' }}</template>
          </el-table-column>
          <el-table-column label="生效日期" min-width="190">
            <template #default="{ row }">
              <span class="date-range">{{ formatDateRange(row as StandardConfig) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="84">
            <template #default="{ row }">
              <el-tag size="small" effect="plain" :type="row.isActive ? 'success' : 'info'">
                {{ row.isActive ? '启用' : '停用' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120" align="right" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="openEdit(row as StandardConfig)">
                编辑
              </el-button>
              <el-button link type="danger" @click="handleDelete(row as StandardConfig)">
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </NcCard>
    </div>

    <!-- 新增/编辑标准 -->
    <el-dialog
      v-model="standardDialog.visible"
      :title="standardDialog.editingId > 0 ? '编辑标准配置' : '新增标准配置'"
      :width="isNarrow ? '94%' : '680px'"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-position="top" @submit.prevent="saveStandard">
        <div class="form-grid">
          <el-form-item label="标准类型" required>
            <el-select
              v-if="standardDialog.editingId === 0"
              v-model="standardForm.standardType"
              class="full-width"
              placeholder="请选择标准类型"
            >
              <el-option
                v-for="type in selectedCategory?.standardTypes ?? []"
                :key="type"
                :label="type"
                :value="type"
              />
            </el-select>
            <el-input v-else :model-value="standardForm.standardType" disabled />
          </el-form-item>
          <el-form-item label="标准名称" required>
            <el-input
              v-model="standardForm.standardName"
              maxlength="100"
              placeholder="如 2026年农村低保标准"
            />
          </el-form-item>
        </div>

        <div class="form-grid">
          <el-form-item label="标准值" required>
            <el-input-number
              v-model="standardForm.standardValue"
              :min="0"
              :precision="2"
              :step="1"
              controls-position="right"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="单位">
            <el-input v-model="standardForm.unit" maxlength="20" placeholder="如 元/月" />
          </el-form-item>
        </div>

        <div class="form-grid">
          <el-form-item label="户口类型">
            <el-select
              v-model="standardForm.hukouType"
              class="full-width"
              clearable
              placeholder="不限（空）"
            >
              <el-option
                v-for="option in hukouOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="供养方式">
            <el-select
              v-model="standardForm.supportMode"
              class="full-width"
              clearable
              placeholder="不限（空）"
            >
              <el-option
                v-for="option in supportModeOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
          </el-form-item>
        </div>

        <div class="form-grid">
          <el-form-item label="生效开始日期" required>
            <el-date-picker
              v-model="standardForm.effectiveStartDate"
              type="date"
              value-format="YYYY-MM-DD"
              placeholder="选择开始日期"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="生效结束日期">
            <el-date-picker
              v-model="standardForm.effectiveEndDate"
              type="date"
              value-format="YYYY-MM-DD"
              placeholder="留空表示长期有效"
              :disabled-date="disableEndDate"
              class="full-width"
            />
          </el-form-item>
        </div>

        <div class="form-grid">
          <el-form-item label="版本">
            <el-input-number
              v-model="standardForm.version"
              :min="1"
              :step="1"
              :precision="0"
              controls-position="right"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="standardForm.isActive" />
          </el-form-item>
        </div>

        <el-form-item label="说明">
          <el-input
            v-model="standardForm.description"
            type="textarea"
            :rows="3"
            maxlength="2000"
            show-word-limit
            placeholder="选填"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="standardDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="standardDialog.saving" @click="saveStandard">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.standards-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.page-error {
  margin: 0;
}

.overview-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.overview-card {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: linear-gradient(160deg, var(--nc-paper-2), var(--nc-paper-3));
  box-shadow: var(--nc-shadow-xs);
}

.overview-card span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.overview-card strong {
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-serif);
  font-size: 24px;
  font-weight: 700;
  line-height: 1.25;
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.overview-card small {
  color: var(--nc-muted-2);
  font-size: 11px;
  line-height: 1.5;
}

.standards-layout {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr);
  gap: 15px;
  align-items: start;
}

.category-panel,
.list-panel {
  min-width: 0;
}

.category-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 620px;
  overflow-y: auto;
}

.category-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  width: 100%;
  padding: 9px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.category-item:hover {
  border-color: var(--nc-line-strong);
}

.category-item.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.category-name {
  overflow: hidden;
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.category-item.is-active .category-name {
  color: var(--nc-ink);
  font-weight: 700;
}

.category-count {
  flex: none;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
}

.standard-table {
  width: 100%;
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.standard-name {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.amount {
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.date-range {
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 14px;
}

.full-width {
  width: 100%;
}

@media (max-width: 1100px) {
  .overview-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 960px) {
  .standards-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .category-list {
    max-height: 300px;
  }
}

@media (max-width: 767px) {
  .overview-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }

  .overview-card strong {
    font-size: 20px;
  }

  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 420px) {
  .overview-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
