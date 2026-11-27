<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { getCertificationTask } from '@/api/elderlyCertification'
import {
  createTaskInfoChange,
  listTaskInfoChanges,
} from '@/api/elderlyInfoChange'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { CertificationTaskDetail } from '@/types/elderlyCertification'
import type {
  InfoChangeCreateDetail,
  InfoChangeCreateRequest,
  InfoChangeFieldDefinition,
  InfoChangeFieldGroup,
  InfoChangeFieldKey,
  InfoChangeRecord,
} from '@/types/elderlyInfoChange'
import {
  INFO_CHANGE_FIELDS,
  INFO_CHANGE_FIELD_GROUP_LABELS,
} from '@/types/elderlyInfoChange'
import ElderlyChip from './ElderlyChip.vue'
import { certStatusTone } from './certificationDisplay'
import { formatAge } from './display'
import {
  infoChangeRiskText,
  infoChangeRiskTone,
  infoChangeStatusText,
  infoChangeStatusTone,
  isBankRelatedField,
  nextMonthValue,
  recordFieldLabels,
} from './infoChangeDisplay'

const route = useRoute()
const router = useRouter()

const GROUP_ORDER: InfoChangeFieldGroup[] = ['contact', 'bank', 'agent', 'agentReceive']

const taskId = computed(() => {
  const raw = route.query.taskId
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' ? value.trim() : ''
})

const task = ref<CertificationTaskDetail | null>(null)
const pendingChanges = ref<InfoChangeRecord[]>([])
const loading = ref(false)
const loadError = ref('')
const saving = ref(false)

const selectedKeys = ref<string[]>([])
const newValues = reactive<Record<string, string>>({})
for (const field of INFO_CHANGE_FIELDS) newValues[field.fieldKey] = ''

const reason = ref('')
const effectiveMonth = ref(nextMonthValue())

const groupedFields = computed(() =>
  GROUP_ORDER.map((group) => ({
    group,
    label: INFO_CHANGE_FIELD_GROUP_LABELS[group],
    fields: INFO_CHANGE_FIELDS.filter(
      (field) => field.group === group,
    ) as InfoChangeFieldDefinition[],
  })).filter((item) => item.fields.length > 0),
)

const selectedFields = computed(() =>
  INFO_CHANGE_FIELDS.filter((field) =>
    selectedKeys.value.includes(field.fieldKey),
  ) as InfoChangeFieldDefinition[],
)

const hasSelection = computed(() => selectedFields.value.length > 0)
const selectedHighRisk = computed(() =>
  selectedFields.value.some((field) => field.risk === 'HIGH'),
)
const selectedBank = computed(() =>
  selectedFields.value.some((field) => isBankRelatedField(field.fieldKey)),
)

/** 合同 4.5：已停发 / 已去世任务不允许再建信息变更。 */
const blockedReason = computed(() => {
  const current = task.value
  if (!current) return ''
  if (current.certStatus === 'Stopped' || current.certStatus === 'Deceased') {
    return '该任务已停发或已去世，不能新建信息变更；请先处理停发业务。'
  }
  if (current.stopDecision === 'Approved') {
    return '该任务停发申请已通过，不能新建信息变更。'
  }
  return ''
})

function currentValue(key: InfoChangeFieldKey | string): string {
  const current = task.value
  if (!current) return ''
  switch (key) {
    case 'phone':
      return current.phone ?? ''
    case 'family_address':
      return current.familyAddress ?? ''
    case 'detail_address':
      return current.detailAddress ?? ''
    case 'bank_name':
      return current.bankName ?? ''
    case 'bank_account':
      return current.bankAccount ?? ''
    case 'agent_name':
      return current.agentName ?? ''
    case 'agent_relation':
      return current.agentRelation ?? ''
    case 'agent_receive_name':
      return current.agentReceiveName ?? ''
    case 'agent_receive_relation':
      return current.agentReceiveRelation ?? ''
    case 'agent_receive_bank_name':
      return current.agentReceiveBankName ?? ''
    case 'agent_receive_bank_account':
      return current.agentReceiveBankAccount ?? ''
    default:
      return ''
  }
}

async function load(): Promise<void> {
  if (!taskId.value) {
    loadError.value = '缺少任务编号（taskId），请从年度身份认证名单的「信息变更」入口进入。'
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    const [detail, changes] = await Promise.all([
      getCertificationTask(taskId.value),
      listTaskInfoChanges(taskId.value).catch(() => [] as InfoChangeRecord[]),
    ])
    task.value = detail
    pendingChanges.value = changes.filter((item) => item.status === 'Pending')

    // 用当前值作为输入提示，但不预填新值，避免误提交未修改字段。
    for (const field of INFO_CHANGE_FIELDS) {
      if (newValues[field.fieldKey] === undefined) newValues[field.fieldKey] = ''
    }
  } catch (error) {
    task.value = null
    pendingChanges.value = []
    loadError.value = getErrorMessage(error, '认证任务加载失败')
  } finally {
    loading.value = false
  }
}

function validate(): string | null {
  if (blockedReason.value) return blockedReason.value
  if (!hasSelection.value) return '请至少勾选一个要变更的字段'

  for (const field of selectedFields.value) {
    const value = (newValues[field.fieldKey] ?? '').trim()
    if (!value) return `请填写「${field.fieldLabel}」的新值`
    if (value === currentValue(field.fieldKey).trim()) {
      return `「${field.fieldLabel}」的新值与当前值相同`
    }
  }

  if (!reason.value.trim()) return '请填写变更原因'
  return null
}

async function submit(): Promise<void> {
  const validation = validate()
  if (validation) {
    ElMessage.warning(validation)
    return
  }

  const details: InfoChangeCreateDetail[] = selectedFields.value.map((field) => ({
    fieldKey: field.fieldKey,
    oldValue: currentValue(field.fieldKey),
    newValue: (newValues[field.fieldKey] ?? '').trim(),
  }))

  saving.value = true
  try {
    const request: InfoChangeCreateRequest = {
      reason: reason.value.trim(),
      sourceType: 'Manual',
      details,
    }
    if (selectedBank.value && effectiveMonth.value) {
      request.effectiveMonth = effectiveMonth.value
    }

    await createTaskInfoChange(taskId.value, request)
    ElMessage.success('信息变更已提交，等待审批')
    await router.push('/elderly/certifications/info-changes')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '信息变更提交失败'))
  } finally {
    saving.value = false
  }
}

function goBack(): void {
  const campaignId = task.value?.campaignId
  void router.push({
    path: '/elderly/certifications',
    query: campaignId === undefined ? {} : { campaignId: String(campaignId) },
  })
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page info-change-form-page">
    <NcPageHeader
      title="信息变更登记"
      description="选择要变更的字段并填写新值；提交后生成待审批变更单，审批通过才写入申请档案。"
      kicker="高龄津贴 · 年度认证"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回认证名单</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button v-if="taskId" link type="primary" @click="load()">重新加载</el-button>
      <el-button v-else link type="primary" @click="goBack">返回认证名单</el-button>
    </el-alert>

    <NcCard v-if="loading" padding="lg">
      <el-skeleton :rows="6" animated />
    </NcCard>

    <template v-else-if="task">
      <el-alert
        v-if="blockedReason"
        class="load-error"
        type="error"
        :title="blockedReason"
        :closable="false"
        show-icon
      />

      <el-alert
        v-if="pendingChanges.length > 0"
        class="load-error"
        type="warning"
        :title="`该任务已有 ${pendingChanges.length} 条待审批变更，同一字段再次提交会覆盖旧单。`"
        :closable="false"
        show-icon
      >
        <div class="pending-list">
          <span v-for="record in pendingChanges" :key="record.id" class="pending-item">
            <span class="mono">{{ record.changeNo || '—' }}</span>
            {{ recordFieldLabels(record).join('、') || '—' }}
            <ElderlyChip
              :text="infoChangeStatusText(record.status, record.statusText)"
              :tone="infoChangeStatusTone(record.status)"
            />
          </span>
        </div>
      </el-alert>

      <NcCard title="人员信息" subtitle="以下为认证任务快照中的当前值；变更审批通过后以申请档案为准。">
        <div class="info-grid">
          <div>
            <span>姓名</span>
            <strong>{{ task.name || '—' }}</strong>
          </div>
          <div>
            <span>身份证号</span>
            <strong class="mono">{{ task.idCard || '—' }}</strong>
          </div>
          <div>
            <span>性别 / 年龄</span>
            <strong>{{ task.gender || '—' }} · {{ formatAge(task.ageSnapshot) }}</strong>
          </div>
          <div>
            <span>类别</span>
            <strong>{{ task.categoryText || '—' }}</strong>
          </div>
          <div>
            <span>认证状态</span>
            <strong>
              <ElderlyChip
                :text="task.certStatusText || task.certStatus"
                :tone="certStatusTone(task.certStatus)"
              />
            </strong>
          </div>
          <div>
            <span>认证方式</span>
            <strong>{{ task.verifyMethodText || '—' }}</strong>
          </div>
          <div>
            <span>联系电话</span>
            <strong>{{ task.phone || '—' }}</strong>
          </div>
          <div>
            <span>社保卡开户行</span>
            <strong>{{ task.bankName || '—' }}</strong>
          </div>
          <div class="span-2">
            <span>家庭住址</span>
            <strong>{{ task.familyAddress || '—' }}</strong>
          </div>
          <div class="span-2">
            <span>详细地址</span>
            <strong>{{ task.detailAddress || '—' }}</strong>
          </div>
          <div class="span-2">
            <span>社保卡账号</span>
            <strong class="mono">{{ task.bankAccount || '—' }}</strong>
          </div>
          <div>
            <span>代办人</span>
            <strong>{{ task.agentName || '—' }} {{ task.agentRelation || '' }}</strong>
          </div>
          <div>
            <span>代领人</span>
            <strong>
              {{ task.agentReceiveName || '—' }} {{ task.agentReceiveRelation || '' }}
            </strong>
          </div>
        </div>
      </NcCard>

      <NcCard
        title="选择变更字段"
        subtitle="勾选需要变更的字段并填写新值；姓名、身份证、户籍、类别不在本流程内。"
      >
        <el-checkbox-group v-model="selectedKeys" class="field-groups">
          <section v-for="group in groupedFields" :key="group.group" class="field-group">
            <h3 class="field-group__title">{{ group.label }}</h3>

            <div class="field-list">
              <div
                v-for="field in group.fields"
                :key="field.fieldKey"
                class="field-row"
                :class="{ 'is-selected': selectedKeys.includes(field.fieldKey) }"
              >
                <div class="field-head">
                  <el-checkbox :value="field.fieldKey" class="field-check">
                    {{ field.fieldLabel }}
                  </el-checkbox>
                  <ElderlyChip
                    :text="infoChangeRiskText(field.risk)"
                    :tone="infoChangeRiskTone(field.risk)"
                  />
                </div>

                <div class="field-body">
                  <div class="field-current">
                    <span>当前值</span>
                    <strong class="mono">{{ currentValue(field.fieldKey) || '—' }}</strong>
                  </div>
                  <el-input
                    v-model="newValues[field.fieldKey]"
                    :disabled="!selectedKeys.includes(field.fieldKey)"
                    :placeholder="`填写新的${field.fieldLabel}`"
                    maxlength="200"
                    clearable
                  />
                </div>
              </div>
            </div>
          </section>
        </el-checkbox-group>

        <el-alert
          v-if="selectedHighRisk"
          class="form-alert"
          type="warning"
          :closable="false"
          show-icon
          title="所选字段包含高风险项（社保卡 / 代办人 / 代领人），需附凭证并复核，审批通过后才生效。"
        />

        <div class="form-block">
          <span class="field-label">变更原因 *</span>
          <el-input
            v-model="reason"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="如：老人更换社保卡 / 联系电话已停用"
          />
        </div>

        <div v-if="selectedBank" class="form-block">
          <span class="field-label">银行卡生效月</span>
          <el-date-picker
            v-model="effectiveMonth"
            type="month"
            value-format="YYYY-MM"
            placeholder="选择生效月份"
            style="width: 100%"
          />
          <p class="form-hint">默认下月生效；审批人可在审批时调整。</p>
        </div>
      </NcCard>

      <div class="bottom-bar">
        <p class="bottom-note">
          {{
            blockedReason ||
            '提交后生成待审批变更单；电话 / 地址审批通过后立即生效，社保卡 / 代办人需复核。'
          }}
        </p>
        <div class="bottom-actions">
          <el-button :disabled="saving" @click="goBack">取消</el-button>
          <el-button
            type="primary"
            :loading="saving"
            :disabled="Boolean(blockedReason)"
            @click="submit"
          >
            提交变更
          </el-button>
        </div>
      </div>
    </template>

    <template v-else-if="!loading && !loadError">
      <el-empty description="未找到认证任务，请从年度身份认证名单重新进入">
        <el-button type="primary" @click="goBack">返回认证名单</el-button>
      </el-empty>
    </template>
  </div>
</template>

<style scoped>
.info-change-form-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.load-error {
  margin: 0;
}

.pending-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: 6px;
}

.pending-item {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: var(--nc-text-sm);
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px 18px;
}

.info-grid > div {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 3px;
}

.info-grid .span-2 {
  grid-column: span 2;
}

.info-grid span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.info-grid strong {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 500;
  word-break: break-all;
}

.field-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
  width: 100%;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.field-group__title {
  margin: 0;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
}

.field-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.field-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-2);
  transition: border-color 0.16s ease, background-color 0.16s ease;
}

.field-row.is-selected {
  border-color: var(--nc-line);
  background: var(--nc-paper);
}

.field-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.field-check {
  margin-right: 0;
  font-weight: 600;
}

.field-body {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  align-items: center;
  gap: 10px;
}

.field-current {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 2px;
}

.field-current span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.field-current strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.form-alert {
  margin: 14px 0 0;
}

.form-block {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 14px;
}

.field-label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
}

.form-hint {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.bottom-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.bottom-note {
  flex: 1 1 320px;
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.bottom-actions {
  display: flex;
  gap: 8px;
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 1100px) {
  .info-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .info-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .info-grid .span-2 {
    grid-column: auto;
  }

  .field-body {
    grid-template-columns: minmax(0, 1fr);
  }

  .bottom-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .bottom-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .bottom-actions :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }
}
</style>
