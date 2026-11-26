<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Download, Plus, Refresh } from '@element-plus/icons-vue'
import {
  deleteDutyHoliday,
  downloadDutyHolidays,
  listDutyHolidays,
  saveDutyHoliday,
} from '@/api/duty'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { DutyHoliday, DutyHolidayType } from '@/types/duty'
import { dutyError, formatDate, formatDateTime, weekdayText } from './display'

const router = useRouter()
const currentYear = new Date().getFullYear()
const yearOptions = Array.from({ length: 5 }, (_, index) => currentYear - 2 + index)

const selectedYear = ref(currentYear)
const holidays = ref<DutyHoliday[]>([])
const loading = ref(false)
const loadError = ref('')

const adding = ref(false)
const newDate = ref(`${currentYear}-01-01`)
const newType = ref<DutyHolidayType>('HOLIDAY')
const newName = ref('')
const saving = ref(false)
const downloading = ref(false)

let loadTicket = 0
let downloadTicket = 0

const yearCountText = computed(() => `共 ${holidays.value.length} 条记录`)
const holidayTypeOptions: Array<{ value: DutyHolidayType; label: string }> = [
  { value: 'HOLIDAY', label: '法定节假日' },
  { value: 'MAKEUP', label: '调休补班' },
]

function typeLabel(type: DutyHolidayType): string {
  return type === 'MAKEUP' ? '调休补班' : '法定节假日'
}

function sourceLabel(source: string): string {
  switch (source) {
    case 'API':
      return '接口下载'
    case 'SEED':
      return '内置种子'
    case 'MANUAL':
      return '手工维护'
    default:
      return source || '—'
  }
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''
  try {
    const rows = await listDutyHolidays(selectedYear.value)
    if (ticket !== loadTicket) return
    holidays.value = rows
  } catch (error) {
    if (ticket !== loadTicket) return
    holidays.value = []
    loadError.value = dutyError(error, '节假日数据加载失败', '查看节假日', 'DUTY_VIEW')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function handleYearChange(): void {
  adding.value = false
  void load()
}

function goBack(): void {
  void router.push('/duty')
}

function openAdd(): void {
  newDate.value = `${selectedYear.value}-01-01`
  newType.value = 'HOLIDAY'
  newName.value = ''
  adding.value = true
}

function cancelAdd(): void {
  adding.value = false
}

async function save(): Promise<void> {
  if (!newDate.value) {
    ElMessage.warning('请选择日期')
    return
  }
  if (!newName.value.trim()) {
    ElMessage.warning('节日名称不能为空')
    return
  }
  if (!newDate.value.startsWith(`${selectedYear.value}-`)) {
    ElMessage.warning(`日期须属于 ${selectedYear.value} 年，切换年份后请重新选择`)
    return
  }

  saving.value = true
  try {
    await saveDutyHoliday({
      date: newDate.value,
      dateType: newType.value,
      name: newName.value.trim(),
      source: 'MANUAL',
    })
    ElMessage.success('节假日记录已保存')
    adding.value = false
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '保存节假日失败', '保存节假日'))
  } finally {
    saving.value = false
  }
}

async function remove(holiday: DutyHoliday): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除 ${formatDate(holiday.date)}（${holiday.name}）的节假日记录吗？`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteDutyHoliday(holiday.id)
    ElMessage.success('节假日记录已删除')
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '删除节假日失败', '删除节假日'))
  }
}

function isNotPublished(error: unknown): boolean {
  if (error instanceof Error) {
    if (error.message.includes('尚未发布') || error.message.includes('未收录')) return true
  }
  const code = (error as { code?: string }).code ?? ''
  const status = (error as { status?: number }).status ?? 0
  return code === 'HOLIDAY_YEAR_NOT_PUBLISHED' || status === 404
}

function isUnreachable(error: unknown): boolean {
  const code = (error as { code?: string }).code ?? ''
  const status = (error as { status?: number }).status ?? 0
  const message = error instanceof Error ? error.message : ''
  return (
    code === 'HOLIDAY_API_UNREACHABLE' ||
    code === 'NETWORK_ERROR' ||
    status === 0 ||
    status === 502 ||
    status === 503 ||
    status === 504 ||
    message.includes('网络') ||
    message.includes('无法连接')
  )
}

async function download(): Promise<void> {
  const year = selectedYear.value
  try {
    await ElMessageBox.confirm(
      `确定要从节假日接口下载 ${year} 年数据吗？\n该年份的接口来源数据将被覆盖（手工维护的记录保留）。主接口无数据时会自动尝试备用数据源。`,
      '下载节假日数据',
      { confirmButtonText: '下载', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  const ticket = ++downloadTicket
  downloading.value = true
  try {
    const summary = await downloadDutyHolidays(year)
    if (ticket !== downloadTicket) return
    let message = `下载完成：法定节假日 ${summary.holidayCount} 天，调休补班 ${summary.makeupCount} 天`
    if (summary.isFromFallback) {
      message += '\n（主接口无该年度数据，已从备用数据源获取）'
      await ElMessageBox.alert(message, '下载完成', { confirmButtonText: '知道了' })
    } else {
      ElMessage.success(message)
    }
    await load()
  } catch (error) {
    if (ticket !== downloadTicket) return
    if (isNotPublished(error)) {
      await ElMessageBox.alert(
        `节假日接口尚未发布 ${year} 年的安排。\n可等待国务院办公厅公布后再下载，期间也可手工新增维护。`,
        `${year} 年安排尚未发布`,
        { confirmButtonText: '知道了' },
      )
    } else if (isUnreachable(error)) {
      await ElMessageBox.alert(
        '无法连接节假日接口，请检查网络或稍后重试；期间可手工新增维护。',
        '网络连接失败',
        { confirmButtonText: '知道了' },
      )
    } else {
      await ElMessageBox.alert(
        dutyError(error, '下载节假日数据失败', '下载节假日数据'),
        '下载失败',
        { confirmButtonText: '知道了' },
      )
    }
  } finally {
    if (ticket === downloadTicket) downloading.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page holiday-page">
    <NcPageHeader
      title="节假日维护"
      description="法定节假日与调休补班日 · 随国家安排更新 · 值班表与业务时间轴共用"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回值班表</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button v-if="!loadError.includes('无权限')" link type="primary" @click="load()">
        重新加载
      </el-button>
    </el-alert>

    <NcCard padding="sm" class="toolbar-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <span class="toolbar-label">年份</span>
          <el-select v-model="selectedYear" class="year-select" @change="handleYearChange">
            <el-option
              v-for="item in yearOptions"
              :key="item"
              :label="`${item} 年`"
              :value="item"
            />
          </el-select>
          <el-button :icon="Refresh" text :loading="loading" @click="load()">刷新</el-button>
          <span class="record-count">{{ yearCountText }}</span>
        </div>

        <div class="toolbar-actions">
          <el-button :icon="Download" :loading="downloading" @click="download">
            下载 {{ selectedYear }} 年数据
          </el-button>
          <el-button type="primary" :icon="Plus" @click="openAdd">手工新增</el-button>
        </div>
      </div>
    </NcCard>

    <NcCard v-if="adding" title="新增节假日记录" class="add-card">
      <p class="add-hint">相同日期将覆盖已有记录；来源记为「手工维护」。</p>
      <form class="holiday-form" @submit.prevent="save">
        <label class="field">
          <span>日期 <em>*</em></span>
          <el-date-picker
            v-model="newDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择日期"
          />
        </label>
        <label class="field">
          <span>类型 <em>*</em></span>
          <el-select v-model="newType">
            <el-option
              v-for="option in holidayTypeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </label>
        <label class="field field--name">
          <span>名称 <em>*</em></span>
          <el-input v-model="newName" maxlength="50" placeholder="如：元旦 / 春节前补班" />
        </label>
        <div class="form-actions">
          <el-button @click="cancelAdd">取消</el-button>
          <el-button type="primary" native-type="submit" :loading="saving">保存</el-button>
        </div>
      </form>
    </NcCard>

    <NcCard padding="none" class="list-card">
      <div v-loading="loading" class="holiday-list">
        <div v-if="holidays.length > 0" class="table-scroll">
          <table class="holiday-table">
            <colgroup>
              <col class="col-date" />
              <col class="col-type" />
              <col class="col-name" />
              <col class="col-source" />
              <col class="col-updated" />
              <col class="col-action" />
            </colgroup>
            <thead>
              <tr>
                <th>日期</th>
                <th>类型</th>
                <th>名称</th>
                <th>来源</th>
                <th>更新时间</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="holiday in holidays" :key="holiday.id">
                <td>
                  <strong>{{ formatDate(holiday.date) }}</strong>
                  <small>{{ weekdayText(holiday.date) }}</small>
                </td>
                <td>
                  <span
                    class="type-badge"
                    :class="holiday.dateType === 'MAKEUP' ? 'type-badge--makeup' : 'type-badge--holiday'"
                  >
                    {{ typeLabel(holiday.dateType) }}
                  </span>
                </td>
                <td class="name-cell">{{ holiday.name }}</td>
                <td class="muted-cell">{{ sourceLabel(holiday.source) }}</td>
                <td class="muted-cell">{{ holiday.updatedAt ? formatDateTime(holiday.updatedAt) : '—' }}</td>
                <td class="action-cell">
                  <el-button size="small" type="danger" plain @click="remove(holiday)">
                    删除
                  </el-button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="`${selectedYear} 年暂无节假日数据`">
            <p class="empty-hint">可点击「下载数据」从节假日接口获取，或手工新增</p>
          </el-empty>
        </div>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.holiday-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.toolbar-card,
.add-card,
.list-card {
  overflow: hidden;
}

.load-alert {
  margin: 0;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 2px 4px;
}

.toolbar-left,
.toolbar-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.toolbar-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.year-select {
  width: 118px;
}

.record-count {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.add-hint {
  margin-bottom: 12px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.holiday-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 12px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 150px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.field em {
  color: var(--nc-seal);
  font-style: normal;
}

.field--name {
  flex: 1 1 220px;
}

.form-actions {
  display: flex;
  gap: 8px;
  margin-left: auto;
}

.holiday-list {
  min-height: 180px;
}

.table-scroll {
  overflow-x: auto;
}

.holiday-table {
  width: 100%;
  min-width: 760px;
  border-collapse: collapse;
  table-layout: fixed;
}

.col-date {
  width: 130px;
}

.col-type {
  width: 120px;
}

.col-name {
  width: auto;
}

.col-source {
  width: 110px;
}

.col-updated {
  width: 150px;
}

.col-action {
  width: 86px;
}

.holiday-table th {
  padding: 10px 12px;
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  text-align: left;
  white-space: nowrap;
}

.holiday-table td {
  padding: 9px 12px;
  border-bottom: 1px solid var(--nc-line-faint);
  vertical-align: middle;
}

.holiday-table tbody tr:hover td {
  background-color: var(--nc-seal-wash);
}

.holiday-table td strong {
  display: block;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.holiday-table td small {
  display: block;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.type-badge {
  display: inline-block;
  padding: 1px 8px;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.7;
  white-space: nowrap;
}

.type-badge--holiday {
  background-color: #fde8e8;
  color: #c62828;
}

.type-badge--makeup {
  background-color: #e3f0fc;
  color: #1565c0;
}

.name-cell {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
}

.muted-cell {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.action-cell {
  text-align: right;
}

.empty-wrap {
  padding: 30px 10px;
}

.empty-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

@media (max-width: 767px) {
  .toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .toolbar-left,
  .toolbar-actions {
    width: 100%;
  }

  .toolbar-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .toolbar-actions :deep(.el-button) {
    margin-left: 0;
  }

  .holiday-form {
    align-items: stretch;
    flex-direction: column;
  }

  .field,
  .field--name {
    width: 100%;
  }

  .form-actions {
    justify-content: flex-end;
    margin-left: 0;
  }
}
</style>
