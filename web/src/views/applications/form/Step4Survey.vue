<script setup lang="ts">
import { computed } from 'vue'
import NcCard from '@/components/NcCard.vue'
import { APPLICATION_REASON_OPTIONS } from './options'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const survey = computed(() => props.form.survey)

const reasonOptions = APPLICATION_REASON_OPTIONS

function setSurveyDate(value: string | number | Date | null): void {
  props.form.survey.surveyDate = value == null ? '' : String(value)
}
</script>

<template>
  <div class="step-survey">
    <NcCard title="调查信息" subtitle="带 * 为必填项；字段校验与保存由办理流程统一处理。">
      <template #actions>
        <span class="section-meta">入户调查</span>
      </template>

      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="survey-date">
            <span class="required-mark" aria-hidden="true">*</span>
            调查日期
          </label>
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
          <label class="field-label" for="surveyor-name">
            <span class="required-mark" aria-hidden="true">*</span>
            调查员姓名
          </label>
          <el-input
            id="surveyor-name"
            v-model="survey.surveyorName"
            class="nc-control"
            maxlength="40"
            placeholder="填写入户调查员姓名"
            :disabled="busy"
          />
        </div>

        <div class="field field--full">
          <label class="field-label" for="surveyor-organization">
            <span class="required-mark" aria-hidden="true">*</span>
            调查机构
          </label>
          <el-input
            id="surveyor-organization"
            v-model="survey.surveyorOrganization"
            class="nc-control"
            maxlength="120"
            placeholder="填写乡镇（街道）或调查机构名称"
            :disabled="busy"
          />
        </div>
      </div>
    </NcCard>

    <NcCard title="申请原因" subtitle="选择本次认定的申请类别，并说明主要致困原因。">
      <template #actions>
        <span class="section-meta">认定类别</span>
      </template>

      <div class="field-grid">
        <div class="field">
          <label class="field-label" for="survey-reason">
            <span class="required-mark" aria-hidden="true">*</span>
            申请原因
          </label>
          <el-select
            id="survey-reason"
            v-model="survey.applicationReason"
            class="nc-control"
            placeholder="选择申请原因"
            :disabled="busy"
            aria-label="申请原因"
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
          <label class="field-label" for="survey-reason-detail">原因说明</label>
          <el-input
            id="survey-reason-detail"
            v-model="survey.applicationReasonDetail"
            class="nc-control"
            type="textarea"
            :rows="4"
            maxlength="500"
            show-word-limit
            resize="vertical"
            placeholder="说明家庭困难情况、主要致困原因等（500 字以内）"
            :disabled="busy"
          />
        </div>
      </div>
    </NcCard>

    <NcCard title="调查记录" subtitle="记录入户走访情况与需要说明的事项。">
      <template #actions>
        <span class="section-meta">现场记录 / 备注</span>
      </template>

      <div class="field">
        <label class="field-label" for="survey-notes">调查记录 / 备注</label>
        <el-input
          id="survey-notes"
          v-model="survey.surveyNotes"
          class="nc-control"
          type="textarea"
          :rows="5"
          resize="vertical"
          placeholder="记录入户走访情况、家庭实际生活状况及需要说明的事项"
          :disabled="busy"
        />
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

.section-meta {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
  white-space: nowrap;
}

.required-mark {
  margin-right: 3px;
  color: var(--nc-seal);
  font-weight: 700;
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

  .section-meta {
    font-size: 10px;
    letter-spacing: 0.06em;
  }
}
</style>
