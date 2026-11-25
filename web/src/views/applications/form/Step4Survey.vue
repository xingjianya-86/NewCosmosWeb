<script setup lang="ts">
import { computed } from 'vue'
import NcCard from '@/components/NcCard.vue'
import { APPLICATION_REASON_OPTIONS } from './options'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const survey = computed(() => props.form.survey)

const reasonOptions = APPLICATION_REASON_OPTIONS

/** 旧版入户调查结论严格三项（ApplicationFormViewModel.cs:763）。 */
const surveyConclusionOptions = [
  { label: '属实', value: '属实' },
  { label: '不属实', value: '不属实' },
  { label: '部分属实', value: '部分属实' },
]

/** 旧版字数统计文案：已输入: n/500 字。 */
const reasonDetailLength = computed(() => survey.value.applicationReasonDetail?.length ?? 0)

function setSurveyDate(value: string | number | Date | null): void {
  props.form.survey.surveyDate = value == null ? '' : String(value)
}
</script>

<template>
  <div class="step-survey">
    <!-- 调查基本信息：调查日期 / 调查员 / 所属机构 / 受访人（只读） -->
    <NcCard title="📋 调查基本信息">
      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="survey-date">调查日期</label>
          <el-date-picker
            id="survey-date"
            class="nc-control"
            type="date"
            format="YYYY-MM-DD"
            value-format="YYYY-MM-DD"
            placeholder="选择调查日期"
            :model-value="survey.surveyDate"
            :disabled="busy"
            aria-label="调查日期"
            @update:model-value="setSurveyDate"
          />
        </div>

        <div class="field">
          <label class="field-label" for="surveyor-name">调查员</label>
          <el-input
            id="surveyor-name"
            v-model="survey.surveyorName"
            class="nc-control"
            maxlength="40"
            placeholder="姓名"
            :disabled="busy"
          />
        </div>

        <div class="field">
          <label class="field-label" for="surveyor-organization">所属机构</label>
          <el-input
            id="surveyor-organization"
            v-model="survey.surveyorOrganization"
            class="nc-control"
            maxlength="120"
            placeholder="民政机构名称"
            :disabled="busy"
          />
        </div>

        <div class="field">
          <span class="field-label">受访人</span>
          <div class="static-value">{{ form.basic.applicantName || '—' }}</div>
        </div>
      </div>
    </NcCard>

    <!-- 申请原因：申请理由 / 原因详情 -->
    <NcCard title="📝 申请原因">
      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="survey-reason">申请理由</label>
          <el-select
            id="survey-reason"
            v-model="survey.applicationReason"
            class="nc-control"
            placeholder="选择申请理由"
            :disabled="busy"
            aria-label="申请理由"
          >
            <el-option
              v-for="reason in reasonOptions"
              :key="reason.value"
              :label="reason.label"
              :value="reason.value"
            />
          </el-select>
        </div>

        <div class="field field--full">
          <label class="field-label" for="survey-reason-detail">原因详情</label>
          <el-input
            id="survey-reason-detail"
            v-model="survey.applicationReasonDetail"
            class="nc-control"
            type="textarea"
            :rows="5"
            maxlength="500"
            resize="vertical"
            placeholder="选择申请理由后自动填充模板，可编辑修改..."
            :disabled="busy"
          />
          <div class="word-count">已输入: {{ reasonDetailLength }}/500 字</div>
        </div>
      </div>
    </NcCard>

    <!-- 调查结论：结论 / 备注 -->
    <NcCard title="✅ 调查结论">
      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="survey-conclusion">结论</label>
          <el-select
            id="survey-conclusion"
            v-model="survey.surveyConclusion"
            class="nc-control"
            placeholder="选择调查结论"
            :disabled="busy"
            aria-label="调查结论"
          >
            <el-option
              v-for="item in surveyConclusionOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </div>

        <div class="field field--full">
          <label class="field-label" for="survey-notes">备注</label>
          <el-input
            id="survey-notes"
            v-model="survey.surveyNotes"
            class="nc-control"
            type="textarea"
            :rows="3"
            resize="vertical"
            placeholder="补充说明（选填）"
            :disabled="busy"
          />
        </div>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.step-survey {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 18px;
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

.static-value {
  display: flex;
  align-items: center;
  min-height: 32px;
  color: var(--nc-ink);
  font-size: 15px;
  line-height: 1.5;
  word-break: break-word;
}

.word-count {
  align-self: flex-end;
  color: var(--nc-muted);
  font-size: 12px;
  line-height: 1.5;
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

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 13px;
  }

  .field--full {
    grid-column: auto;
  }
}
</style>
