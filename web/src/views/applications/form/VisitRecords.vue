<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, RefreshRight } from '@element-plus/icons-vue'
import { apiHttp, getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime } from '../format'
import type { ApplicationFormModel, ApplicationFormVisit } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const isNarrow = useMediaQuery('(max-width: 767px)')
const auth = useAuthStore()

/** 回访情况四项（legacy ApplicationFormViewModel.Visit.cs:33，默认“已回访”）。 */
const VISIT_RESULT_OPTIONS = [
  { label: '已回访', value: '已回访' },
  { label: '电话回访', value: '电话回访' },
  { label: '未联系上', value: '未联系上' },
  { label: '无需回访', value: '无需回访' },
]

/** 历史数据可能存旧版英文枚举，展示时统一转中文。 */
const VISIT_RESULT_NAMES: Record<string, string> = {
  Visited: '已回访',
  PhoneVisited: '电话回访',
  Unreachable: '未联系上',
  NotRequired: '无需回访',
}

function visitResultText(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return '—'
  const mapped = VISIT_RESULT_NAMES[text]
  if (mapped) return mapped
  return /[\u4e00-\u9fa5]/.test(text) ? text : '其他'
}

interface VisitDraft {
  visitAt: string
  visitBy: string
  visitResult: string
  visitRemark: string
}

const submitting = ref(false)
/** 旧版默认收起补录表单，点“补录回访”再展开。 */
const showForm = ref(false)

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** 默认回访时间为当前时间，取秒级 ISO 字符串。 */
function nowDateTime(): string {
  const now = new Date()
  return (
    `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}` +
    `T${pad(now.getHours())}:${pad(now.getMinutes())}:00`
  )
}

/** 旧版默认经办人取当前用户全名（ApplicationFormViewModel.Visit.cs:27）。 */
const currentUserName = computed(
  () => auth.user?.fullName?.trim() || auth.user?.username?.trim() || '',
)

const draft = reactive<VisitDraft>({
  visitAt: nowDateTime(),
  visitBy: currentUserName.value,
  visitResult: '已回访',
  visitRemark: '',
})

const records = computed<ApplicationFormVisit[]>(() => props.form.visitRecords ?? [])

function timeValue(value: string): number {
  const time = Date.parse(value)
  return Number.isNaN(time) ? 0 : time
}

/** 按回访时间倒序；时间相同按 id 倒序，避免顺序抖动。 */
const sortedRecords = computed(() =>
  [...records.value].sort(
    (a, b) => timeValue(b.visitAt) - timeValue(a.visitAt) || b.id - a.id,
  ),
)

const latestRecord = computed(() => sortedRecords.value[0] ?? null)

/** 旧版回访区标题语义（ApplicationFormViewModel.Visit.cs:57/62）。 */
const sectionTitle = computed(() =>
  latestRecord.value
    ? `已回访 · ${formatDateTime(latestRecord.value.visitAt)}`
    : '待回访 —— 认定通过后请补录回访情况',
)

const isNewApplication = computed(() => props.form.id <= 0)
const canSubmit = computed(() => !isNewApplication.value && !props.busy && !submitting.value)

function resetDraft(): void {
  draft.visitAt = nowDateTime()
  draft.visitBy = currentUserName.value
  draft.visitResult = '已回访'
  draft.visitRemark = ''
}

async function submitVisit(): Promise<void> {
  if (isNewApplication.value) {
    ElMessage.warning('请先保存申请后再补录回访')
    return
  }
  if (props.busy || submitting.value) return

  const visitAt = draft.visitAt.trim()
  const visitBy = draft.visitBy.trim()
  const visitResult = draft.visitResult.trim()
  const visitRemark = draft.visitRemark.trim()

  if (!visitAt) {
    ElMessage.warning('请选择回访时间')
    return
  }
  if (!visitBy) {
    ElMessage.warning('请填写回访经办人员')
    return
  }
  if (!visitResult) {
    ElMessage.warning('请选择回访情况')
    return
  }

  submitting.value = true
  try {
    const payload = { visitAt, visitBy, visitResult, visitRemark }
    const result = await apiHttp
      .post<{ id: number }>(`/applications/${props.form.id}/visits`, payload)
      .then((response) => response.data)

    props.form.visitRecords.push({
      id: Number(result?.id ?? 0) || 0,
      applicationId: props.form.id,
      applicantIdCard: props.form.basic.applicantIdCard.trim(),
      ...payload,
    })
    resetDraft()
    showForm.value = false
    ElMessage.success('回访已补录')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '回访补录失败'))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <NcCard
    v-if="!isNewApplication"
    :title="sectionTitle"
    class="visit-section"
    :class="{ 'is-pending': !latestRecord }"
    padding="sm"
  >
    <template #actions>
      <div class="visit-actions">
        <span class="record-count">共 {{ sortedRecords.length }} 条</span>
        <el-button
          size="small"
          :icon="showForm ? undefined : Plus"
          :disabled="busy"
          @click="showForm = !showForm"
        >
          {{ showForm ? '收起' : '补录回访' }}
        </el-button>
      </div>
    </template>

    <h3 v-if="sortedRecords.length > 0" class="visit-section__title">回访历史</h3>

    <p v-else class="visit-empty-text">暂无回访记录</p>

    <template v-if="sortedRecords.length > 0">
      <el-table v-if="!isNarrow" :data="sortedRecords" row-key="id" class="visit-table">
        <el-table-column label="回访时间" min-width="158">
          <template #default="{ row }">
            <span class="mono">{{ formatDateTime(row.visitAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="回访经办人员" min-width="128">
          <template #default="{ row }">{{ row.visitBy || '—' }}</template>
        </el-table-column>

        <el-table-column label="回访情况" width="118">
          <template #default="{ row }">
            <span class="visit-result">{{ visitResultText(row.visitResult) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="回访备注" min-width="200">
          <template #default="{ row }">
            <span :class="{ 'is-empty': !row.visitRemark }">{{ row.visitRemark || '—' }}</span>
          </template>
        </el-table-column>
      </el-table>

      <ul v-else class="visit-cards">
        <li v-for="record in sortedRecords" :key="record.id" class="visit-card">
          <div class="visit-card__head">
            <span class="mono">{{ formatDateTime(record.visitAt) }}</span>
            <span class="visit-result">{{ visitResultText(record.visitResult) }}</span>
          </div>
          <div class="visit-card__row">
            <span>回访经办人员</span>
            <strong>{{ record.visitBy || '—' }}</strong>
          </div>
          <p class="visit-card__remark" :class="{ 'is-empty': !record.visitRemark }">
            {{ record.visitRemark || '无备注' }}
          </p>
        </li>
      </ul>
    </template>

    <div v-if="showForm" class="visit-form">
      <h3 class="visit-section__title">补录回访</h3>

      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="visit-at">回访时间</label>
          <el-date-picker
            id="visit-at"
            v-model="draft.visitAt"
            class="nc-control"
            type="datetime"
            format="YYYY-MM-DD HH:mm"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择回访时间"
            :disabled="busy || submitting"
            aria-label="回访时间"
          />
        </div>

        <div class="field">
          <label class="field-label" for="visit-by">回访经办人员</label>
          <el-input
            id="visit-by"
            v-model="draft.visitBy"
            class="nc-control"
            maxlength="40"
            placeholder="填写回访经办人员姓名"
            :disabled="busy || submitting"
          />
        </div>

        <div class="field">
          <label class="field-label" for="visit-result">回访情况</label>
          <el-select
            id="visit-result"
            v-model="draft.visitResult"
            class="nc-control"
            placeholder="选择回访情况"
            :disabled="busy || submitting"
            aria-label="回访情况"
          >
            <el-option
              v-for="item in VISIT_RESULT_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </div>

        <div class="field">
          <label class="field-label" for="visit-remark">回访备注</label>
          <el-input
            id="visit-remark"
            v-model="draft.visitRemark"
            class="nc-control"
            placeholder="回访备注（可选）"
            :disabled="busy || submitting"
          />
        </div>
      </div>

      <div class="visit-actions">
        <el-button
          type="primary"
          :icon="Plus"
          :loading="submitting"
          :disabled="!canSubmit"
          @click="submitVisit"
        >
          保存回访
        </el-button>
        <el-button :icon="RefreshRight" :disabled="busy || submitting" @click="resetDraft">
          重置
        </el-button>
      </div>
    </div>
  </NcCard>
</template>

<style scoped>
.record-count {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.visit-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.visit-section.is-pending :deep(.nc-card__title) {
  color: var(--nc-amber-dark);
}

.visit-section :deep(.nc-card__head) {
  margin-bottom: 8px;
  padding-bottom: 8px;
}

.visit-section__title {
  margin-bottom: 11px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.visit-empty-text {
  margin: 0;
  padding: 1px 0 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.visit-table {
  margin-bottom: 16px;
  border: 1px solid var(--nc-line-faint);
}

.visit-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.visit-table :deep(.el-table__cell) {
  padding: 9px 0;
}

.visit-result {
  display: inline-flex;
  align-items: center;
  padding: 1px 8px;
  border: 1px solid rgba(46, 71, 56, 0.32);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.is-empty {
  color: var(--nc-disabled);
}

.visit-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin: 0 0 16px;
  padding: 0;
  list-style: none;
}

.visit-card {
  padding: 11px 12px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-pine);
  background-color: var(--nc-paper-soft);
}

.visit-card__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding-bottom: 7px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.visit-card__row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  margin-top: 7px;
  font-size: var(--nc-text-sm);
}

.visit-card__row span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.visit-card__row strong {
  color: var(--nc-ink-2);
  font-weight: 600;
}

.visit-card__remark {
  margin: 6px 0 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-word;
}

.visit-card__remark.is-empty {
  color: var(--nc-disabled);
}

.visit-form {
  padding-top: 14px;
  border-top: 1px dashed var(--nc-line);
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 13px 16px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  letter-spacing: 0.02em;
  line-height: 1.45;
}

.nc-control {
  width: 100%;
}

.nc-control :deep(.el-input__wrapper) {
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  box-shadow: 0 0 0 1px var(--nc-line) inset;
}

.nc-control :deep(.el-input__wrapper.is-focus) {
  box-shadow: 0 0 0 1px var(--nc-seal) inset;
}

.nc-control :deep(.el-input__inner) {
  color: var(--nc-ink);
  font-size: 13px;
}

.nc-control :deep(.el-select__wrapper) {
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  box-shadow: 0 0 0 1px var(--nc-line) inset;
}

.nc-control :deep(.el-select__wrapper.is-focused) {
  box-shadow: 0 0 0 1px var(--nc-seal) inset;
}

.visit-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-top: 14px;
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
  }

  .visit-actions {
    align-items: stretch;
  }

  .visit-actions .el-button {
    flex: 1 1 auto;
  }
}
</style>
