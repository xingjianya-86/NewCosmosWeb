<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Delete, Refresh } from '@element-plus/icons-vue'
import {
  classifyApplication,
  completeApplication,
  createApplicationForm,
  deleteApplication,
  getApplicationForm,
  isFullApplicationForm,
  updateApplicationForm,
} from '@/api/applicationForm'
import { getErrorMessage } from '@/api/http'
import NcPageHeader from '@/components/NcPageHeader.vue'
import Step1Basic from './form/Step1Basic.vue'
import Step2Members from './form/Step2Members.vue'
import Step3Economy from './form/Step3Economy.vue'
import Step4Survey from './form/Step4Survey.vue'
import Step5Classification from './form/Step5Classification.vue'
import { cloneForm, createHeadMember, normalizeForm } from './form/formModel'
import { createEmptyForm, type ApplicationFormModel } from './form/types'
import { validateBasic } from './form/validation'

interface FormStep {
  value: number
  label: string
  hint: string
}

const STEPS: FormStep[] = [
  { value: 1, label: '基本信息', hint: '申请人、户籍与申请说明' },
  { value: 2, label: '家庭成员', hint: '共同生活成员与赡养人' },
  { value: 3, label: '经济状况', hint: '收入、支出与土地情况' },
  { value: 4, label: '入户调查', hint: '调查记录与佐证材料' },
  { value: 5, label: '分类认定', hint: '认定结果与保障金额' },
]

const route = useRoute()
const router = useRouter()

const form = ref<ApplicationFormModel>(createEmptyForm())
const loading = ref(false)
const loadError = ref('')
const saving = ref(false)
const classifying = ref(false)
const completing = ref(false)
const actionError = ref('')

let loadTicket = 0
/** 新建保存后跳编辑地址时，跳过路由监听触发的一次重新加载，避免覆盖后续分类结果。 */
let skipNextRouteLoad = false

const rawId = computed(() => {
  const value = route.params.id
  return Array.isArray(value) ? (value[0] ?? '') : (value ?? '')
})

const isNew = computed(
  () => !rawId.value || rawId.value === 'new' || route.path.replace(/\/+$/, '').endsWith('/new'),
)

const applicationId = computed(() => (isNew.value ? 0 : Number(rawId.value) || 0))

const assetCheckId = computed(() => {
  const value = route.query.assetCheckId
  const text = Array.isArray(value) ? (value[0] ?? '') : (value ?? '')
  return text.trim()
})

const currentStep = computed(() => form.value.currentStep)
const currentStepMeta = computed(
  () => STEPS.find((step) => step.value === currentStep.value) ?? STEPS[0],
)
const basicIssues = computed(() => validateBasic(form.value.basic))
const step1Valid = computed(() => basicIssues.value.length === 0)
/** 编辑模式下必须等 GET 成功后再允许保存，避免把空表单 POST 成新申请。 */
const formReady = computed(
  () => isNew.value || (form.value.id > 0 && form.value.id === applicationId.value),
)

const headerDescription = computed(() => {
  if (isNew.value) return '新建低收入人口认定申请 · 保存草稿后生成申请编号'
  const no = form.value.applicationNo || `#${form.value.id}`
  return `申请编号 ${no} · ${form.value.statusText || form.value.status || '草稿'} · 第 ${currentStep.value} / 5 步`
})

/** 已完成的步骤可回看；向前切换要求第 1 步基本信息通过校验。 */
function canEnterStep(step: number): boolean {
  if (step === 1) return true
  if (step <= currentStep.value) return true
  return step1Valid.value
}

function applyForm(data: ApplicationFormModel): void {
  const normalized = normalizeForm(data)
  if (!normalized.id && form.value.id) normalized.id = form.value.id
  if (!normalized.applicationNo && form.value.applicationNo) {
    normalized.applicationNo = form.value.applicationNo
  }
  form.value = normalized
}

async function loadForm(): Promise<void> {
  const ticket = ++loadTicket
  actionError.value = ''
  loadError.value = ''

  if (isNew.value) {
    const empty = createEmptyForm()
    empty.members = [createHeadMember(empty.basic)]
    form.value = empty
    loading.value = false
    return
  }

  if (!applicationId.value) {
    loading.value = false
    loadError.value = '缺少申请编号，无法加载表单'
    return
  }

  loading.value = true
  try {
    const data = await getApplicationForm(applicationId.value)
    if (ticket !== loadTicket) return
    applyForm(data)
    if (!form.value.id) form.value.id = applicationId.value
  } catch (error) {
    if (ticket !== loadTicket) return
    loadError.value = getErrorMessage(error, '表单加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

/** 新建后切到编辑地址，避免刷新时回到空白表单。 */
function moveToEditRoute(id: number): void {
  if (!id) return
  const registered = router.getRoutes().some((record) => record.path.includes('/:id/edit'))
  if (!registered) return
  skipNextRouteLoad = true
  void router.replace(`/applications/${id}/edit`)
}

/** 保存草稿，返回最新表单 id。 */
async function persist(): Promise<number> {
  if (!formReady.value) {
    throw new Error('表单尚未加载完成，请刷新后重试')
  }

  const creating = form.value.id <= 0
  const payload = cloneForm(form.value)
  const result = creating
    ? await createApplicationForm(payload)
    : await updateApplicationForm(form.value.id, payload)

  if (result.id) form.value.id = result.id
  if (result.applicationNo) form.value.applicationNo = result.applicationNo
  if (result.status) form.value.status = result.status
  if (result.statusText) form.value.statusText = result.statusText
  if (creating && !result.status) {
    form.value.status = 'Draft'
    form.value.statusText = '草稿'
  }

  if (creating) moveToEditRoute(form.value.id)
  return form.value.id
}

async function saveDraft(): Promise<void> {
  if (saving.value) return
  saving.value = true
  actionError.value = ''
  try {
    await persist()
    ElMessage.success('草稿已保存')
  } catch (error) {
    actionError.value = getErrorMessage(error, '保存草稿失败')
    ElMessage.error(actionError.value)
  } finally {
    saving.value = false
  }
}

function goToStep(step: number): void {
  if (step === currentStep.value) return
  if (!canEnterStep(step)) {
    ElMessage.warning(basicIssues.value[0]?.message ?? '请先完善第 1 步基本信息')
    if (currentStep.value !== 1) form.value.currentStep = 1
    return
  }
  form.value.currentStep = step
}

function prevStep(): void {
  if (currentStep.value <= 1) return
  form.value.currentStep -= 1
}

function nextStep(): void {
  if (currentStep.value >= 5) return
  if (currentStep.value === 1 && !step1Valid.value) {
    ElMessage.warning(basicIssues.value[0]?.message ?? '请先完善基本信息')
    return
  }
  form.value.currentStep += 1
}

/** 先保存再分类认定，兼容接口返回完整表单或仅 classification。 */
async function handleClassify(): Promise<void> {
  if (classifying.value || completing.value) return
  classifying.value = true
  actionError.value = ''
  try {
    await persist()
    if (form.value.id <= 0) throw new Error('保存后未取得申请编号，无法分类认定')

    const result = await classifyApplication(form.value.id, cloneForm(form.value))
    if (isFullApplicationForm(result)) {
      applyForm(result)
    } else {
      form.value.classification = { ...form.value.classification, ...result }
    }
    form.value.currentStep = 5
    ElMessage.success('分类认定已完成')
  } catch (error) {
    actionError.value = getErrorMessage(error, '分类认定失败')
    ElMessage.error(actionError.value)
  } finally {
    classifying.value = false
  }
}

/** 先保存再完成认定，成功后回到工作流列表。 */
async function handleComplete(): Promise<void> {
  if (completing.value || classifying.value) return

  try {
    await ElMessageBox.confirm(
      '完成认定后将生成档案并结束本次办理，是否继续？',
      '完成认定',
      {
        type: 'warning',
        confirmButtonText: '完成认定',
        cancelButtonText: '取消',
      },
    )
  } catch {
    return
  }

  completing.value = true
  actionError.value = ''
  try {
    await persist()
    if (form.value.id <= 0) throw new Error('保存后未取得申请编号，无法完成认定')

    const result = await completeApplication(form.value.id)
    if (result.status) form.value.status = result.status
    form.value.statusText = '已完结'
    if (result.currentStep) form.value.currentStep = result.currentStep
    ElMessage.success('认定完成，档案已生成')
    await router.push('/applications')
  } catch (error) {
    actionError.value = getErrorMessage(error, '完成认定失败')
    ElMessage.error(actionError.value)
  } finally {
    completing.value = false
  }
}

async function handleDelete(): Promise<void> {
  if (form.value.id <= 0) return

  try {
    await ElMessageBox.confirm(
      `确定删除申请「${form.value.applicationNo || form.value.id}」吗？删除后不可恢复。`,
      '删除申请',
      {
        type: 'error',
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--danger',
      },
    )
  } catch {
    return
  }

  try {
    await deleteApplication(form.value.id)
    ElMessage.success('申请已删除')
    await router.push('/applications')
  } catch (error) {
    actionError.value = getErrorMessage(error, '删除申请失败')
    ElMessage.error(actionError.value)
  }
}

function backToWorkflow(): void {
  void router.push('/applications')
}

function reload(): void {
  void loadForm()
}

watch(
  () => route.params.id,
  (value, previous) => {
    if (value === previous) return
    if (skipNextRouteLoad) {
      skipNextRouteLoad = false
      return
    }
    void loadForm()
  },
)

onMounted(() => {
  void loadForm()
})
</script>

<template>
  <div class="nc-page form-page">
    <NcPageHeader
      title="低收入人口认定办理"
      :description="headerDescription"
      kicker="低收入人口认定 / 五步办理"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToWorkflow">返回工作流</el-button>
        <el-button v-if="!isNew" :icon="Refresh" :loading="loading" @click="reload">刷新</el-button>
        <el-button
          v-if="!isNew && form.id > 0 && (form.status === 'Draft' || !form.status)"
          type="danger"
          plain
          :icon="Delete"
          @click="handleDelete"
        >
          删除
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="assetCheckId && isNew"
      class="asset-alert"
      type="info"
      :closable="false"
      show-icon
      :title="`本次申请关联资产核对记录 #${assetCheckId}`"
    >
      资产核对数据不会自动写入本表单，请据实录入家庭成员与经济状况。
    </el-alert>

    <div class="form-layout">
      <nav class="step-rail" aria-label="办理步骤">
        <ol class="step-list">
          <li v-for="step in STEPS" :key="step.value">
            <button
              type="button"
              class="step-item"
              :class="{
                'is-active': currentStep === step.value,
                'is-done': currentStep > step.value,
                'is-locked': !canEnterStep(step.value),
              }"
              :aria-current="currentStep === step.value ? 'step' : undefined"
              @click="goToStep(step.value)"
            >
              <span class="step-no">{{ String(step.value).padStart(2, '0') }}</span>
              <span class="step-text">
                <strong>{{ step.label }}</strong>
                <small>{{ step.hint }}</small>
              </span>
            </button>
          </li>
        </ol>

        <div class="rail-meta">
          <div>
            <span>申请编号</span>
            <strong>{{ form.applicationNo || '保存草稿后生成' }}</strong>
          </div>
          <div>
            <span>办理状态</span>
            <strong>{{ form.statusText || form.status || '草稿' }}</strong>
          </div>
          <div>
            <span>家庭人数</span>
            <strong>{{ form.members.length }} 人</strong>
          </div>
        </div>
      </nav>

      <section class="form-main" aria-live="polite">
        <el-alert
          v-if="loadError"
          class="form-alert"
          type="error"
          :title="loadError"
          :closable="false"
          show-icon
        >
          <el-button link type="primary" @click="reload">重新加载</el-button>
        </el-alert>

        <el-alert
          v-if="actionError"
          class="form-alert"
          type="error"
          :title="actionError"
          show-icon
          @close="actionError = ''"
        />

        <div v-if="loading" class="loading-card">
          <el-skeleton :rows="8" animated />
        </div>

        <template v-else>
          <header class="step-heading">
            <p class="step-heading__kicker">STEP {{ currentStep }} / 5</p>
            <h2 class="step-heading__title">{{ currentStepMeta.label }}</h2>
            <p class="step-heading__hint">{{ currentStepMeta.hint }}</p>
          </header>

          <Step1Basic v-if="currentStep === 1" :form="form" />
          <Step2Members v-else-if="currentStep === 2" :form="form" />
          <Step3Economy
            v-else-if="currentStep === 3"
            :form="form"
            :busy="saving || classifying || completing"
          />
          <Step4Survey
            v-else-if="currentStep === 4"
            :form="form"
            :busy="saving || classifying || completing"
          />
          <Step5Classification
            v-else
            :form="form"
            :busy="saving || classifying || completing"
            @classify="handleClassify"
            @complete="handleComplete"
          />
        </template>
      </section>
    </div>

    <footer class="action-bar">
      <div class="action-bar__info">
        <span class="action-bar__step">第 {{ currentStep }} / 5 步</span>
        <span v-if="step1Valid" class="action-bar__state is-ok">基本信息已完善</span>
        <span v-else class="action-bar__state is-warn">基本信息未完善</span>
        <span v-if="saving" class="action-bar__state">正在保存…</span>
      </div>

      <div class="action-bar__buttons">
        <el-button
          :loading="saving"
          :disabled="classifying || completing || !formReady"
          @click="saveDraft"
        >
          保存草稿
        </el-button>
        <el-button
          :disabled="currentStep <= 1 || saving || classifying || completing"
          @click="prevStep"
        >
          上一步
        </el-button>
        <el-button
          v-if="currentStep < 5"
          type="primary"
          :disabled="saving || classifying || completing"
          @click="nextStep"
        >
          下一步
        </el-button>
        <el-button
          v-else
          type="primary"
          :loading="completing"
          :disabled="saving || classifying"
          @click="handleComplete"
        >
          完成认定 / 生成档案
        </el-button>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.form-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding-bottom: 8px;
}

.asset-alert,
.form-alert {
  margin: 0;
}

.form-layout {
  display: grid;
  grid-template-columns: 236px minmax(0, 1fr);
  align-items: start;
  gap: 16px;
}

/* ── 左侧步骤条 ─────────────────────────────────────────────── */
.step-rail {
  position: sticky;
  top: 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.step-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.step-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  width: 100%;
  padding: 12px 13px;
  border: none;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: transparent;
  color: var(--nc-ink-2);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.step-item:hover {
  background-color: var(--nc-paper-3);
}

.step-item.is-active {
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  box-shadow: inset 3px 0 0 var(--nc-seal);
}

.step-item.is-locked {
  opacity: 0.55;
  cursor: not-allowed;
}

.step-item.is-locked:hover {
  background-color: transparent;
}

.step-no {
  display: grid;
  flex: none;
  place-items: center;
  width: 26px;
  height: 26px;
  border: 1px solid var(--nc-line);
  border-radius: 50%;
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
}

.step-item.is-active .step-no {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal);
  color: #fff;
}

.step-item.is-done .step-no {
  border-color: var(--nc-pine);
  background-color: var(--nc-pine);
  color: #fff;
}

.step-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.step-text strong {
  color: inherit;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.02em;
  line-height: 1.4;
}

.step-text small {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.45;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rail-meta {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 13px;
  border-top: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
}

.rail-meta div {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.rail-meta span {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.rail-meta strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 右侧内容 ───────────────────────────────────────────────── */
.form-main {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.loading-card {
  padding: 22px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.step-heading {
  padding: 2px 2px 0;
}

.step-heading__kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.18em;
}

.step-heading__title {
  margin-top: 3px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.04em;
  line-height: 1.4;
}

.step-heading__hint {
  margin-top: 4px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

/* ── 底部固定操作栏 ─────────────────────────────────────────── */
.action-bar {
  position: sticky;
  bottom: 0;
  z-index: 8;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 2px;
  padding: 11px 16px;
  border: 1px solid var(--nc-line);
  border-top: 2px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: 0 -6px 18px rgba(23, 19, 16, 0.08);
}

.action-bar__info {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.action-bar__step {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.1em;
}

.action-bar__state {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.action-bar__state.is-ok {
  color: var(--nc-pine);
}

.action-bar__state.is-warn {
  color: var(--nc-seal);
}

.action-bar__buttons {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

@media (max-width: 1023px) {
  .form-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .step-rail {
    position: static;
  }

  .step-list {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
  }

  .step-item {
    flex-direction: column;
    gap: 5px;
    border-right: 1px solid var(--nc-line-faint);
    border-bottom: none;
  }

  .step-item.is-active {
    box-shadow: inset 0 -3px 0 var(--nc-seal);
  }

  .step-text small {
    display: none;
  }

  .rail-meta {
    flex-direction: row;
    flex-wrap: wrap;
    gap: 6px 20px;
  }

  .rail-meta div {
    gap: 6px;
  }
}

@media (max-width: 767px) {
  .form-page {
    gap: 11px;
  }

  .step-list {
    display: flex;
    overflow-x: auto;
    scrollbar-width: thin;
  }

  .step-item {
    flex: none;
    min-width: 116px;
    flex-direction: row;
    align-items: center;
    padding: 10px 11px;
  }

  .step-text small {
    display: none;
  }

  .rail-meta {
    display: none;
  }

  .action-bar {
    flex-direction: column;
    align-items: stretch;
    padding: 10px 12px;
  }

  .action-bar__info {
    justify-content: space-between;
    gap: 6px;
  }

  .action-bar__buttons {
    width: 100%;
    flex-wrap: nowrap;
  }

  .action-bar__buttons :deep(.el-button) {
    flex: 1;
    min-width: 0;
    margin-left: 0;
  }
}
</style>
