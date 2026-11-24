<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, RefreshRight } from '@element-plus/icons-vue'
import { apiHttp, getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { formatDateTime } from '../format'
import type { ApplicationFormModel, ApplicationFormVisit } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const isNarrow = useMediaQuery('(max-width: 767px)')

const VISIT_RESULT_OPTIONS = [
  { label: '属实', value: '属实' },
  { label: '基本属实', value: '基本属实' },
  { label: '不属实', value: '不属实' },
  { label: '无法调查', value: '无法调查' },
]

interface VisitDraft {
  visitAt: string
  visitBy: string
  visitResult: string
  visitRemark: string
}

const submitting = ref(false)

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** 默认走访时间为当前时间，取秒级 ISO 字符串。 */
function nowDateTime(): string {
  const now = new Date()
  return (
    `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}` +
    `T${pad(now.getHours())}:${pad(now.getMinutes())}:00`
  )
}

const draft = reactive<VisitDraft>({
  visitAt: nowDateTime(),
  visitBy: '',
  visitResult: '属实',
  visitRemark: '',
})

const records = computed<ApplicationFormVisit[]>(() => props.form.visitRecords ?? [])

function timeValue(value: string): number {
  const time = Date.parse(value)
  return Number.isNaN(time) ? 0 : time
}

/** 按走访时间倒序；时间相同按 id 倒序，避免顺序抖动。 */
const sortedRecords = computed(() =>
  [...records.value].sort(
    (a, b) => timeValue(b.visitAt) - timeValue(a.visitAt) || b.id - a.id,
  ),
)

const isNewApplication = computed(() => props.form.id <= 0)
const canSubmit = computed(() => !isNewApplication.value && !props.busy && !submitting.value)

function resetDraft(): void {
  draft.visitAt = nowDateTime()
  draft.visitBy = ''
  draft.visitResult = '属实'
  draft.visitRemark = ''
}

async function submitVisit(): Promise<void> {
  if (isNewApplication.value) {
    ElMessage.warning('请先保存草稿，再添加走访记录')
    return
  }
  if (props.busy || submitting.value) return

  const visitAt = draft.visitAt.trim()
  const visitBy = draft.visitBy.trim()
  const visitResult = draft.visitResult.trim()
  const visitRemark = draft.visitRemark.trim()

  if (!visitAt) {
    ElMessage.warning('请选择走访时间')
    return
  }
  if (!visitBy) {
    ElMessage.warning('请填写走访人')
    return
  }
  if (!visitResult) {
    ElMessage.warning('请选择走访结果')
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
    ElMessage.success('走访记录已添加')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '走访记录添加失败'))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <NcCard
    title="走访记录"
    subtitle="按走访时间倒序展示；新增记录单独保存，不影响其他表单内容。"
  >
    <template #actions>
      <span class="section-meta">{{ sortedRecords.length }} 条</span>
    </template>

    <el-alert
      v-if="isNewApplication"
      class="visit-tip"
      type="info"
      :closable="false"
      show-icon
      title="新申请需先保存草稿，生成申请编号后才能添加走访记录。"
    />

    <el-empty
      v-if="sortedRecords.length === 0"
      class="visit-empty"
      description="暂无走访记录"
      :image-size="68"
    />

    <template v-else>
      <el-table v-if="!isNarrow" :data="sortedRecords" row-key="id" class="visit-table">
        <el-table-column label="走访时间" min-width="158">
          <template #default="{ row }">
            <span class="mono">{{ formatDateTime(row.visitAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="走访人" min-width="108">
          <template #default="{ row }">{{ row.visitBy || '—' }}</template>
        </el-table-column>

        <el-table-column label="结果" width="96">
          <template #default="{ row }">
            <span class="visit-result">{{ row.visitResult || '—' }}</span>
          </template>
        </el-table-column>

        <el-table-column label="备注" min-width="200">
          <template #default="{ row }">
            <span :class="{ 'is-empty': !row.visitRemark }">{{ row.visitRemark || '—' }}</span>
          </template>
        </el-table-column>
      </el-table>

      <ul v-else class="visit-cards">
        <li v-for="record in sortedRecords" :key="record.id" class="visit-card">
          <div class="visit-card__head">
            <span class="mono">{{ formatDateTime(record.visitAt) }}</span>
            <span class="visit-result">{{ record.visitResult || '—' }}</span>
          </div>
          <div class="visit-card__row">
            <span>走访人</span>
            <strong>{{ record.visitBy || '—' }}</strong>
          </div>
          <p class="visit-card__remark" :class="{ 'is-empty': !record.visitRemark }">
            {{ record.visitRemark || '无备注' }}
          </p>
        </li>
      </ul>
    </template>

    <div class="visit-form">
      <h3 class="visit-form__title">新增走访记录</h3>

      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="visit-at">
            <span class="required-mark" aria-hidden="true">*</span>
            走访时间
          </label>
          <el-date-picker
            id="visit-at"
            v-model="draft.visitAt"
            class="nc-control"
            type="datetime"
            format="YYYY-MM-DD HH:mm"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="选择走访时间"
            :disabled="busy || submitting"
            aria-label="走访时间"
          />
        </div>

        <div class="field">
          <label class="field-label" for="visit-by">
            <span class="required-mark" aria-hidden="true">*</span>
            走访人
          </label>
          <el-input
            id="visit-by"
            v-model="draft.visitBy"
            class="nc-control"
            maxlength="40"
            placeholder="填写走访人姓名"
            :disabled="busy || submitting"
          />
        </div>

        <div class="field">
          <label class="field-label" for="visit-result">
            <span class="required-mark" aria-hidden="true">*</span>
            走访结果
          </label>
          <el-select
            id="visit-result"
            v-model="draft.visitResult"
            class="nc-control"
            placeholder="选择走访结果"
            :disabled="busy || submitting"
            aria-label="走访结果"
          >
            <el-option
              v-for="item in VISIT_RESULT_OPTIONS"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </div>

        <div class="field field--full">
          <label class="field-label" for="visit-remark">备注</label>
          <el-input
            id="visit-remark"
            v-model="draft.visitRemark"
            class="nc-control"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            resize="vertical"
            placeholder="记录走访情况、见证人及需要说明的事项（500 字以内）"
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
          添加走访记录
        </el-button>
        <el-button
          :icon="RefreshRight"
          :disabled="busy || submitting"
          @click="resetDraft"
        >
          重置
        </el-button>
        <span class="action-note">走访记录提交后立即生效；其他字段修改请使用底部「保存草稿」。</span>
      </div>
    </div>
  </NcCard>
</template>

<style scoped>
.section-meta {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
  white-space: nowrap;
}

.visit-tip {
  margin-bottom: 12px;
}

.visit-empty {
  padding: 6px 0 14px;
}

.visit-table {
  margin-bottom: 16px;
  border: 1px solid var(--nc-line-faint);
}

.visit-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.08em;
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
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
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

.visit-form__title {
  margin-bottom: 11px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 13px 16px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field--full {
  grid-column: 1 / -1;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  letter-spacing: 0.02em;
  line-height: 1.45;
}

.required-mark {
  margin-right: 3px;
  color: var(--nc-seal);
  font-weight: 700;
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

.nc-control :deep(.el-textarea__inner) {
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  box-shadow: 0 0 0 1px var(--nc-line) inset;
  color: var(--nc-ink);
  font-family: var(--nc-font-sans);
  font-size: 13px;
  line-height: 1.7;
}

.nc-control :deep(.el-textarea__inner:focus) {
  box-shadow: 0 0 0 1px var(--nc-seal) inset;
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

.action-note {
  margin-left: auto;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

@media (max-width: 1099px) {
  .field-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
  }

  .field--full {
    grid-column: auto;
  }

  .visit-actions {
    align-items: stretch;
  }

  .visit-actions .el-button {
    flex: 1 1 auto;
  }

  .action-note {
    flex-basis: 100%;
    margin-left: 0;
    text-align: center;
  }
}
</style>
