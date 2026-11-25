<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Refresh } from '@element-plus/icons-vue'
import { getApplication } from '@/api/applications'
import { ApiError, getErrorMessage } from '@/api/http'
import {
  createSpecialApproval,
  getSpecialApproval,
  getSpecialApprovalByApplication,
  reviewSpecialApproval,
  submitSpecialApproval,
  updateSpecialApproval,
  type SpecialApprovalRecord,
  type SpecialApprovalSavePayload,
} from '@/api/specialApprovals'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useAuthStore } from '@/stores/auth'
import type { ApplicationDetail } from '@/types/application'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

/** 会议由系统指定施保分类；城乡同名只保留 Rural 值，后端按申请户籍归一。 */
const CLASSIFICATION_OPTIONS = [
  { value: 'RuralSubsistence', label: '最低生活保障对象' },
  { value: 'RuralLowIncome', label: '最低生活保障边缘家庭' },
  { value: 'RuralLowIncomeSingle', label: '最低生活保障对象（单人）' },
  { value: 'RuralRigidExpenditure', label: '刚性支出困难家庭' },
  { value: 'RuralDestituteScattered', label: '特困人员（分散供养）' },
  { value: 'RuralDestituteCentralized', label: '特困人员（集中供养）' },
] as const

/** 分类代码 → 中文（与 Core ClassificationConstants.CodeToDescription 同源）。 */
const CLASSIFICATION_NAMES: Record<string, string> = {
  RuralSubsistence: '最低生活保障对象',
  UrbanSubsistence: '最低生活保障对象',
  RuralLowIncome: '最低生活保障边缘家庭',
  UrbanLowIncome: '最低生活保障边缘家庭',
  RuralLowIncomeSingle: '最低生活保障对象（单人）',
  UrbanLowIncomeSingle: '最低生活保障对象（单人）',
  RuralDestituteCentralized: '特困人员（集中供养）',
  UrbanDestituteCentralized: '特困人员（集中供养）',
  RuralDestituteScattered: '特困人员（分散供养）',
  UrbanDestituteScattered: '特困人员（分散供养）',
  RuralRigidExpenditure: '刚性支出困难家庭',
  UrbanRigidExpenditure: '刚性支出困难家庭',
  RuralIncomeExceeded: '收入超标',
  UrbanIncomeExceeded: '收入超标',
  IneligibleWithLabor: '不符合认定条件（有劳动力）',
  Ineligible: '不符合认定条件',
  IneligibleOther: '不符合认定条件（其他）',
}

function todayString(): string {
  const date = new Date()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

function toDateInput(value: string | null | undefined): string {
  if (!value) return ''
  const text = String(value).trim()
  return /^\d{4}-\d{2}-\d{2}/.test(text) ? text.slice(0, 10) : ''
}

function classificationName(value: string | null | undefined): string {
  const code = (value ?? '').trim()
  if (!code) return '—'
  return CLASSIFICATION_NAMES[code] ?? (/[\u4e00-\u9fa5]/.test(code) ? code : '未识别分类')
}

/** 后端保存的是按户籍归一后的完整代码，下拉仍以 Rural 值展示。 */
function toSelectClassification(value: string | null | undefined): string {
  const code = (value ?? '').trim()
  if (!code) return ''
  if (CLASSIFICATION_OPTIONS.some((item) => item.value === code)) return code
  if (code.startsWith('Urban')) {
    const rural = `Rural${code.slice('Urban'.length)}`
    if (CLASSIFICATION_OPTIONS.some((item) => item.value === rural)) return rural
  }
  return code
}

const record = ref<SpecialApprovalRecord | null>(null)
const application = ref<ApplicationDetail | null>(null)
const loading = ref(true)
const loadError = ref('')
const saving = ref(false)
const submitting = ref(false)
const reviewing = ref(false)

const form = reactive({
  basicSituation: '',
  specialMatters: '',
  templateKey: '',
  reportUnit: '',
  handlerName: '',
  formDate: todayString() as string | null,
  auditResult: '同意纳入',
  auditDate: null as string | null,
})

const meeting = reactive({
  meetingDate: todayString() as string | null,
  attendees: '',
  classification: '',
  approverName: '',
  approverPosition: '',
  approverUnit: '',
})

let loadTicket = 0

const routeId = computed(() => {
  const raw = route.params.id
  return Array.isArray(raw) ? (raw[0] ?? '') : (raw ?? '')
})

const queryApplicationId = computed(() => {
  const raw = route.query.applicationId
  const value = Array.isArray(raw) ? raw[0] : raw
  return Number(value ?? 0)
})

const isNewRoute = computed(() => route.name === 'special-approval-new' || !routeId.value)

const currentApplicationId = computed(
  () => record.value?.applicationId ?? application.value?.id ?? queryApplicationId.value,
)

const status = computed(() => record.value?.status ?? 'Draft')
const statusText = computed(() => {
  if (!record.value) return '未保存'
  return record.value.statusText || status.value
})
const isReviewed = computed(() => record.value?.isReviewed ?? false)
const canEdit = computed(() => Boolean(application.value) && !isReviewed.value)
const canSubmit = computed(() => canEdit.value && status.value === 'Draft')
const showReviewSection = computed(
  () => Boolean(record.value) && (record.value?.isSubmitted || isReviewed.value),
)

const statusTagType = computed(() => {
  if (isReviewed.value) return record.value?.approved ? 'success' : 'danger'
  if (status.value === 'Submitted') return 'warning'
  return 'info'
})

const pageTitle = computed(() =>
  application.value
    ? `一事一议 · ${application.value.applicantName}`
    : '一事一议申报表',
)

const pageDescription = computed(() => {
  if (!application.value) return '特殊困难群众救助申报与会议审议。'
  const place = [application.value.town, application.value.village].filter(Boolean).join(' / ')
  return place
    ? `特殊困难群众救助申报 · ${place}`
    : '特殊困难群众救助申报与会议审议。'
})

const familyAddress = computed(() => {
  if (!application.value) return '—'
  const parts = [
    application.value.town,
    application.value.village,
    application.value.address,
  ].filter((part) => Boolean(part && part.trim()))
  return parts.length > 0 ? parts.join(' ') : application.value.hukouAddress || '—'
})

const reviewResultDescription = computed(() => {
  if (!record.value?.isReviewed) return ''
  const parts = [
    record.value.approverName ? `审批人 ${record.value.approverName}` : '',
    record.value.approverTitle ? `职务 ${record.value.approverTitle}` : '',
    record.value.approverOrganization ? `单位 ${record.value.approverOrganization}` : '',
    record.value.overrideClassification
      ? `指定分类 ${classificationName(record.value.overrideClassification)}`
      : '',
  ].filter(Boolean)
  return parts.join(' · ') || '会议审议已完成。'
})

function text(value: string | null | undefined): string {
  return value && value.trim() ? value : '—'
}

function applyDefaults(app: ApplicationDetail): void {
  const user = auth.user
  form.basicSituation = app.applicationReasonDetail?.trim() ?? ''
  form.specialMatters = ''
  form.templateKey = ''
  form.reportUnit = user?.organizationName?.trim() ?? ''
  form.handlerName = user?.fullName?.trim() || user?.username || ''
  form.formDate = todayString()
  form.auditResult = '同意纳入'
  form.auditDate = null

  meeting.meetingDate = todayString()
  meeting.attendees = ''
  meeting.classification = ''
  meeting.approverName = user?.fullName?.trim() || user?.username || ''
  meeting.approverPosition = ''
  meeting.approverUnit = form.reportUnit
}

function applyRecord(next: SpecialApprovalRecord): void {
  form.basicSituation = next.basicSituation ?? ''
  form.specialMatters = next.specialMatters ?? ''
  form.templateKey = next.templateKey ?? ''
  form.reportUnit = next.reportUnit ?? ''
  form.handlerName = next.handlerName ?? ''
  form.formDate = toDateInput(next.formDate) || todayString()
  form.auditResult = next.auditResult || '同意纳入'
  form.auditDate = toDateInput(next.auditDate) || null

  meeting.meetingDate = toDateInput(next.meetingDate) || todayString()
  meeting.attendees = next.meetingMembers ?? ''
  meeting.classification = toSelectClassification(next.overrideClassification)
  meeting.approverName = next.approverName ?? ''
  meeting.approverPosition = next.approverTitle ?? ''
  meeting.approverUnit = next.approverOrganization ?? ''
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''
  record.value = null
  application.value = null

  try {
    let nextRecord: SpecialApprovalRecord | null = null
    let applicationId = queryApplicationId.value

    if (isNewRoute.value) {
      if (applicationId > 0) {
        try {
          nextRecord = await getSpecialApprovalByApplication(applicationId)
        } catch (error) {
          if (!(error instanceof ApiError && error.status === 404)) throw error
        }
      }
    } else {
      if (!routeId.value) {
        loadError.value = '缺少申报表编号'
        return
      }
      nextRecord = await getSpecialApproval(routeId.value)
      applicationId = nextRecord.applicationId
    }

    if (!applicationId) {
      loadError.value = '缺少申请编号，无法发起一事一议。'
      return
    }

    const app = await getApplication(applicationId)
    if (ticket !== loadTicket) return

    application.value = app
    if (isNewRoute.value && nextRecord) {
      record.value = nextRecord
      await router.replace({ name: 'special-approval-detail', params: { id: nextRecord.id } })
      return
    }

    record.value = nextRecord
    if (nextRecord) applyRecord(nextRecord)
    else applyDefaults(app)
  } catch (error) {
    if (ticket !== loadTicket) return
    loadError.value = getErrorMessage(error, '一事一议申报表加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function buildPayload(): SpecialApprovalSavePayload {
  return {
    basicSituation: form.basicSituation.trim(),
    specialMatters: form.specialMatters.trim(),
    templateKey: form.templateKey.trim(),
    reportUnit: form.reportUnit.trim(),
    handlerName: form.handlerName.trim(),
    formDate: form.formDate || null,
    auditResult: form.auditResult.trim(),
    auditDate: form.auditDate || null,
  }
}

/** 保存并返回最新记录；navigate=false 供提交/审议前的静默保存。 */
async function persist(navigate: boolean): Promise<SpecialApprovalRecord> {
  const payload = buildPayload()

  let next: SpecialApprovalRecord
  if (record.value) {
    next = await updateSpecialApproval(record.value.id, payload)
  } else {
    const applicationId = currentApplicationId.value
    if (!applicationId) throw new Error('缺少申请编号')
    next = await createSpecialApproval({ ...payload, applicationId })
  }

  record.value = next
  applyRecord(next)

  if (navigate && String(routeId.value) !== String(next.id)) {
    await router.replace({ name: 'special-approval-detail', params: { id: next.id } })
  }
  return next
}

function ensureSpecialMatters(): boolean {
  if (form.specialMatters.trim()) return true
  ElMessage.warning('请填写一事一议说明情况')
  return false
}

async function saveDraft(): Promise<void> {
  if (!ensureSpecialMatters()) return
  saving.value = true
  try {
    await persist(true)
    ElMessage.success('申报表草稿已保存')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存草稿失败'))
  } finally {
    saving.value = false
  }
}

async function submitApplication(): Promise<void> {
  if (!ensureSpecialMatters()) return

  try {
    await ElMessageBox.confirm(
      '提交后申报表进入会议审议环节，确定提交？',
      '提交申报',
      { confirmButtonText: '提交', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  submitting.value = true
  try {
    const saved = await persist(false)
    const next = await submitSpecialApproval(saved.id)
    record.value = next
    applyRecord(next)
    await router.replace({ name: 'special-approval-detail', params: { id: next.id } })
    ElMessage.success('申报表已提交，可以填写会议审议结果')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '提交申报失败'))
  } finally {
    submitting.value = false
  }
}

async function review(approved: boolean): Promise<void> {
  if (!record.value) return
  if (approved && !meeting.classification) {
    ElMessage.warning('审议通过前请选择指定救助分类')
    return
  }

  const action = approved ? '通过' : '驳回'
  try {
    await ElMessageBox.confirm(
      approved
        ? '通过后该申请按指定分类施保，确定审议通过？'
        : '驳回后该申请将不予救助，确定审议驳回？',
      `确认审议${action}`,
      { confirmButtonText: '确定', cancelButtonText: '取消', type: approved ? 'warning' : 'error' },
    )
  } catch {
    return
  }

  reviewing.value = true
  try {
    const saved = await persist(false)
    const next = await reviewSpecialApproval(saved.id, {
      approved,
      classification: meeting.classification || undefined,
      meetingDate: meeting.meetingDate || undefined,
      attendees: meeting.attendees.trim() || undefined,
      approverName: meeting.approverName.trim() || undefined,
      approverPosition: meeting.approverPosition.trim() || undefined,
      approverUnit: meeting.approverUnit.trim() || undefined,
    })
    record.value = next
    applyRecord(next)
    if (approved && application.value && next.overrideClassification) {
      application.value.classificationResult = next.overrideClassification
    }
    await router.replace({ name: 'special-approval-detail', params: { id: next.id } })
    ElMessage.success(`一事一议审议${action}`)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, `审议${action}失败`))
  } finally {
    reviewing.value = false
  }
}

function goBack(): void {
  const applicationId = currentApplicationId.value
  if (applicationId > 0) {
    void router.push({ name: 'application-detail', params: { id: applicationId } })
    return
  }
  if (window.history.length > 1) router.back()
  else void router.push({ name: 'applications' })
}

watch(
  () => [route.params.id, route.query.applicationId],
  () => {
    if (route.name === 'special-approval-detail' || route.name === 'special-approval-new') {
      void load()
    }
  },
)

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page special-approval-page">
    <NcPageHeader :title="pageTitle" :description="pageDescription" kicker="特殊困难 · 一事一议">
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <NcCard v-if="loading" padding="lg" class="state-card">
      <el-skeleton :rows="10" animated />
    </NcCard>

    <el-alert
      v-else-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <div class="error-actions">
        <el-button size="small" :icon="Refresh" @click="load()">重新加载</el-button>
        <el-button size="small" @click="goBack">返回</el-button>
      </div>
    </el-alert>

    <el-empty
      v-else-if="!application"
      description="没有可用的申请信息，请从申请档案进入一事一议。"
    >
      <el-button type="primary" @click="goBack">返回申请</el-button>
    </el-empty>

    <template v-else>
      <NcCard title="申请人信息" subtitle="来自低收入人口认定档案，只读。" class="section-card">
        <div class="info-grid">
          <div class="info-item">
            <span class="info-label">姓名</span>
            <strong class="info-value">{{ text(application.applicantName) }}</strong>
          </div>
          <div class="info-item">
            <span class="info-label">身份证号</span>
            <strong class="info-value">{{ text(application.applicantIdCard) }}</strong>
          </div>
          <div class="info-item">
            <span class="info-label">联系电话</span>
            <strong class="info-value">{{ text(application.applicantPhone) }}</strong>
          </div>
          <div class="info-item">
            <span class="info-label">家庭人口</span>
            <strong class="info-value">{{ application.familySize }} 人</strong>
          </div>
          <div class="info-item">
            <span class="info-label">申请类别</span>
            <strong class="info-value">
              {{ classificationName(application.classificationResult) }}
            </strong>
          </div>
          <div class="info-item info-item--wide">
            <span class="info-label">家庭住址</span>
            <strong class="info-value">{{ familyAddress }}</strong>
          </div>
        </div>
      </NcCard>

      <NcCard
        title="申报内容"
        subtitle="按旧版申报表填写；提交后仍可在会议审议前修改。"
        class="section-card"
      >
        <template #actions>
          <span class="status-pill" :class="`status-pill--${statusTagType}`">{{ statusText }}</span>
          <span v-if="record?.formNo" class="status-pill status-pill--info">
            {{ record.formNo }}
          </span>
        </template>

        <el-form label-position="top" class="approval-form" @submit.prevent>
          <el-form-item label="申请救助家庭基本情况">
            <el-input
              v-model="form.basicSituation"
              type="textarea"
              :rows="4"
              placeholder="请填写家庭基本情况、经济状况等"
              :disabled="!canEdit"
            />
          </el-form-item>

          <el-form-item label="一事一议说明情况">
            <el-input
              v-model="form.specialMatters"
              type="textarea"
              :rows="8"
              placeholder="请写明特殊困难、存在争议和有关情况说明……"
              :disabled="!canEdit"
            />
          </el-form-item>

          <el-form-item label="预案（可选）">
            <el-input
              v-model="form.templateKey"
              maxlength="100"
              clearable
              placeholder="如采用预案例文，可填写预案名称或标识"
              :disabled="!canEdit"
            />
          </el-form-item>

          <div class="field-grid">
            <el-form-item label="填报单位">
              <el-input
                v-model="form.reportUnit"
                maxlength="200"
                clearable
                placeholder="请输入填报单位"
                :disabled="!canEdit"
              />
            </el-form-item>
            <el-form-item label="经办人员">
              <el-input
                v-model="form.handlerName"
                maxlength="100"
                clearable
                placeholder="请输入经办人员姓名"
                :disabled="!canEdit"
              />
            </el-form-item>
            <el-form-item label="填报时间">
              <el-date-picker
                v-model="form.formDate"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="选择填报日期"
                :disabled="!canEdit"
                style="width: 100%"
              />
            </el-form-item>
            <el-form-item label="乡镇民政办审核结果">
              <el-select
                v-model="form.auditResult"
                filterable
                allow-create
                default-first-option
                clearable
                placeholder="请选择或输入审核结果"
                :disabled="!canEdit"
                style="width: 100%"
              >
                <el-option label="同意纳入" value="同意纳入" />
                <el-option label="不同意纳入" value="不同意纳入" />
              </el-select>
            </el-form-item>
          </div>
        </el-form>
      </NcCard>

      <NcCard
        v-if="showReviewSection"
        title="会议审议"
        subtitle="申报提交后填写；审议完成即锁定。"
        class="section-card"
      >
        <template #actions>
          <span class="status-pill" :class="`status-pill--${statusTagType}`">{{ statusText }}</span>
        </template>

        <el-alert
          v-if="isReviewed"
          class="review-result"
          :type="record?.approved ? 'success' : 'error'"
          :title="record?.approved ? '会议审议通过' : '会议审议驳回'"
          :description="reviewResultDescription"
          :closable="false"
          show-icon
        />

        <el-form label-position="top" class="approval-form" @submit.prevent>
          <div class="field-grid">
            <el-form-item label="会议日期">
              <el-date-picker
                v-model="meeting.meetingDate"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="选择会议日期"
                :disabled="isReviewed"
                style="width: 100%"
              />
            </el-form-item>
            <el-form-item label="指定救助分类">
              <el-select
                v-model="meeting.classification"
                clearable
                placeholder="请选择指定救助分类"
                :disabled="isReviewed"
                style="width: 100%"
              >
                <el-option
                  v-for="item in CLASSIFICATION_OPTIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>
          </div>

          <el-form-item label="参会人员">
            <el-input
              v-model="meeting.attendees"
              type="textarea"
              :rows="4"
              placeholder="每行一人"
              :disabled="isReviewed"
            />
          </el-form-item>

          <div class="field-grid field-grid--three">
            <el-form-item label="审批人姓名">
              <el-input
                v-model="meeting.approverName"
                maxlength="100"
                clearable
                placeholder="请输入审批人姓名"
                :disabled="isReviewed"
              />
            </el-form-item>
            <el-form-item label="审批人职务">
              <el-input
                v-model="meeting.approverPosition"
                maxlength="100"
                clearable
                placeholder="请输入审批人职务"
                :disabled="isReviewed"
              />
            </el-form-item>
            <el-form-item label="审批人单位">
              <el-input
                v-model="meeting.approverUnit"
                maxlength="200"
                clearable
                placeholder="请输入审批人单位"
                :disabled="isReviewed"
              />
            </el-form-item>
          </div>
        </el-form>

        <div v-if="!isReviewed" class="review-actions">
          <el-button type="success" :loading="reviewing" @click="review(true)">
            审议通过
          </el-button>
          <el-button type="danger" plain :loading="reviewing" @click="review(false)">
            审议驳回
          </el-button>
        </div>
      </NcCard>

      <div v-if="canEdit || canSubmit" class="form-actions">
        <div class="form-actions__inner">
          <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
          <el-button
            v-if="canEdit"
            type="primary"
            plain
            :loading="saving"
            @click="saveDraft"
          >
            保存草稿
          </el-button>
          <el-button
            v-if="canSubmit"
            type="primary"
            :loading="submitting"
            @click="submitApplication"
          >
            提交申报
          </el-button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.special-approval-page {
  padding-bottom: 92px;
}

.state-card,
.load-error,
.section-card {
  margin-bottom: 16px;
}

.error-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px 20px;
}

.info-item {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 5px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft, rgba(0, 0, 0, 0.015));
}

.info-item--wide {
  grid-column: span 3;
}

.info-label {
  font-size: var(--nc-text-xs);
  letter-spacing: 0.08em;
  color: var(--nc-muted);
}

.status-pill {
  display: inline-flex;
  align-items: center;
  padding: 2px 9px;
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.status-pill--info {
  border-color: #dcdfe6;
  background-color: #f4f4f5;
  color: #606266;
}

.status-pill--warning {
  border-color: #f3d19e;
  background-color: #fdf6ec;
  color: #b88230;
}

.status-pill--success {
  border-color: #b3e19d;
  background-color: #f0f9eb;
  color: #529b2e;
}

.status-pill--danger {
  border-color: #fab6b6;
  background-color: #fef0f0;
  color: #c45656;
}

.info-value {
  overflow-wrap: anywhere;
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  color: var(--nc-ink);
}

.approval-form :deep(.el-form-item) {
  margin-bottom: 18px;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 18px;
}

.field-grid--three {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.review-result {
  margin-bottom: 18px;
}

.review-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
  padding-top: 4px;
  border-top: 1px solid var(--nc-line-faint);
}

.review-actions :deep(.el-button) {
  margin-left: 0;
}

.form-actions {
  position: sticky;
  bottom: 0;
  z-index: 8;
  margin: 0 -20px -20px;
  padding: 12px 20px;
  border-top: 1px solid var(--nc-line);
  background-color: #fff;
  box-shadow: 0 -2px 6px rgba(23, 19, 16, 0.08);
}

.form-actions__inner {
  display: flex;
  max-width: var(--nc-content-max);
  margin: 0 auto;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
}

.form-actions__inner :deep(.el-button) {
  margin-left: 0;
  min-width: 108px;
}

@media (max-width: 960px) {
  .info-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .info-item--wide {
    grid-column: span 2;
  }

  .field-grid--three {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .special-approval-page {
    padding-bottom: 84px;
  }

  .info-grid,
  .field-grid,
  .field-grid--three {
    grid-template-columns: minmax(0, 1fr);
  }

  .info-item--wide {
    grid-column: span 1;
  }

  .review-actions,
  .form-actions__inner {
    justify-content: stretch;
  }

  .review-actions :deep(.el-button),
  .form-actions__inner :deep(.el-button) {
    flex: 1 1 100%;
    width: 100%;
  }

  .form-actions {
    margin: 0 -20px -20px;
  }
}
</style>
