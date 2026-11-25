<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Delete, Document, FolderOpened, Refresh } from '@element-plus/icons-vue'
import {
  classifyApplication,
  createApplicationForm,
  deleteApplication,
  getApplicationForm,
  isFullApplicationForm,
  updateApplicationForm,
} from '@/api/applicationForm'
import { getErrorMessage } from '@/api/http'
import Step1Basic from './form/Step1Basic.vue'
import Step2Members from './form/Step2Members.vue'
import Step3Economy from './form/Step3Economy.vue'
import Step4Survey from './form/Step4Survey.vue'
import Step5Classification from './form/Step5Classification.vue'
import VisitRecords from './form/VisitRecords.vue'
import { cloneForm, createHeadMember, normalizeForm } from './form/formModel'
import { createEmptyForm, type ApplicationFormModel } from './form/types'
import { validateBasic } from './form/validation'

interface FormStep {
  value: number
  label: string
}

const STEPS: FormStep[] = [
  { value: 1, label: '基本信息' },
  { value: 2, label: '家庭成员' },
  { value: 3, label: '经济状况' },
  { value: 4, label: '入户调查' },
  { value: 5, label: '分类认定' },
]

const route = useRoute()
const router = useRouter()

const form = ref<ApplicationFormModel>(createEmptyForm())
const loading = ref(false)
const loadError = ref('')
const saving = ref(false)
const classifying = ref(false)
const submitting = ref(false)
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
const basicIssues = computed(() => validateBasic(form.value.basic))
const step1Valid = computed(() => basicIssues.value.length === 0)
/** 编辑模式下必须等 GET 成功后再允许保存，避免把空表单 POST 成新申请。 */
const formReady = computed(
  () => isNew.value || (form.value.id > 0 && form.value.id === applicationId.value),
)

/** 草稿可编辑；已提交等状态按旧版“查看”模式处理，仅隐藏编辑动作，步骤仍可回看。 */
const isViewMode = computed(
  () => !isNew.value && !!form.value.status && form.value.status !== 'Draft',
)
const modeLabel = computed(() => (isViewMode.value ? '查看' : '编辑'))
const statusLabel = computed(() => {
  if (form.value.statusText) return form.value.statusText
  if (!form.value.status || form.value.status === 'Draft') return '草稿'
  return form.value.status
})
const canDelete = computed(
  () =>
    !isNew.value &&
    form.value.id > 0 &&
    !isViewMode.value &&
    (form.value.status === 'Draft' || !form.value.status),
)

/** 提交前置：必须已有分类判定结果。 */
const hasClassification = computed(
  () =>
    Boolean(
      (form.value.classification.classificationResult ?? '').trim() ||
        (form.value.classification.classificationText ?? '').trim(),
    ),
)

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

/** 保存草稿，返回最新表单 id；updateRoute=false 时由调用方接管跳转。 */
async function persist(options: { updateRoute?: boolean } = {}): Promise<number> {
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

  if (creating && options.updateRoute !== false) moveToEditRoute(form.value.id)
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
  if (isViewMode.value || classifying.value || submitting.value) return
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

/** 提交（旧版语义）：仅保存草稿，随后进入档案制作，由档案制作页完成归档。 */
async function handleGoArchive(): Promise<void> {
  if (isViewMode.value || submitting.value || classifying.value) return

  if (!hasClassification.value) {
    ElMessage.warning('请先完成分类判定')
    return
  }

  submitting.value = true
  actionError.value = ''
  try {
    await persist({ updateRoute: false })
    if (form.value.id <= 0) throw new Error('保存后未取得申请编号，无法进入档案制作')
    ElMessage.success('已保存，正在进入档案制作')
    await router.push({
      path: '/archives/production',
      query: { applicationId: String(form.value.id) },
    })
  } catch (error) {
    actionError.value = getErrorMessage(error, '保存申请失败')
    ElMessage.error(actionError.value)
  } finally {
    submitting.value = false
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

/** 旧版 Head 返回键与底部“放弃申请”同义：确认后离开，未保存内容丢弃。 */
async function handleAbandon(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '确定要放弃本次办理吗？已录入但未保存的信息将会丢失。',
      '放弃办理',
      {
        type: 'warning',
        confirmButtonText: '放弃办理',
        cancelButtonText: '继续填写',
      },
    )
  } catch {
    return
  }
  backToWorkflow()
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
  <div class="form-page">
    <!-- 顶部标题栏：旧版 Primary 横条 + 返回 + 图标 + 标题/副标题 + 模式状态 -->
    <header class="form-hero">
      <div class="form-hero__main">
        <el-button
          class="hero-back"
          :icon="ArrowLeft"
          circle
          aria-label="返回工作流"
          title="返回工作流"
          @click="handleAbandon"
        />
        <el-icon class="hero-icon" :size="34"><Document /></el-icon>
        <div class="hero-text">
          <h1 class="hero-title">低收入人口认定申请</h1>
          <p class="hero-subtitle">家庭申请 · 信息录入 · 分类认定</p>
        </div>
      </div>

      <div class="form-hero__meta">
        <span v-if="!isNew" class="hero-no">{{ form.applicationNo || `#${form.id}` }}</span>
        <span class="hero-chip">{{ statusLabel }}</span>
        <span class="hero-chip hero-chip--mode">{{ modeLabel }}</span>
        <el-button
          v-if="!isNew"
          class="hero-action"
          :icon="Refresh"
          circle
          :loading="loading"
          aria-label="刷新"
          title="刷新"
          @click="reload"
        />
        <el-button
          v-if="canDelete"
          class="hero-action"
          :icon="Delete"
          circle
          aria-label="删除申请"
          title="删除申请"
          @click="handleDelete"
        />
      </div>
    </header>

    <!-- 步骤卡片条：旧版横向 5 卡 + 连接线 -->
    <section class="form-steps" aria-label="办理步骤">
      <div class="step-scroll">
        <div class="step-track">
          <template v-for="(step, index) in STEPS" :key="step.value">
            <button
              type="button"
              class="step-card"
              :class="{
                'is-active': currentStep === step.value,
                'is-done': currentStep > step.value,
                'is-locked': !canEnterStep(step.value),
              }"
              :aria-current="currentStep === step.value ? 'step' : undefined"
              @click="goToStep(step.value)"
            >
              <span class="step-card__no">{{ step.value }}</span>
              <span class="step-card__label">{{ step.label }}</span>
            </button>
            <span
              v-if="index < STEPS.length - 1"
              class="step-link"
              :class="{ 'is-done': currentStep > step.value }"
              aria-hidden="true"
            />
          </template>
        </div>
      </div>
    </section>

    <!-- 回访记录独立区：旧版位于五步卡之后、内容区之前，所有步骤可见；新申请不显示 -->
    <section v-if="form.id > 0" class="form-visit" aria-label="回访记录">
      <VisitRecords :form="form" :busy="saving || classifying || submitting" />
    </section>

    <!-- 内容区：左侧当前步骤 + 右侧 320px 来源数据栏 -->
    <div class="form-body">
      <section class="form-main" aria-live="polite">
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
          <Step1Basic v-if="currentStep === 1" :form="form" @reload="reload" />
          <Step2Members v-else-if="currentStep === 2" :form="form" />
          <Step3Economy
            v-else-if="currentStep === 3"
            :form="form"
            :busy="saving || classifying || submitting"
          />
          <Step4Survey
            v-else-if="currentStep === 4"
            :form="form"
            :busy="saving || classifying || submitting"
          />
          <Step5Classification
            v-else
            :form="form"
            :busy="saving || classifying || submitting"
            @classify="handleClassify"
          />
        </template>
      </section>

      <aside class="form-aside" aria-label="来源数据">
        <div class="aside-head">
          <el-icon :size="18"><FolderOpened /></el-icon>
          <strong>来源数据</strong>
        </div>

        <div class="aside-body">
          <p v-if="assetCheckId" class="aside-note">已关联资产核对记录 #{{ assetCheckId }}</p>
          <div class="aside-empty">
            <el-icon :size="38"><FolderOpened /></el-icon>
            <span>{{ assetCheckId ? '来源字段待接入' : '暂无来源数据' }}</span>
          </div>
        </div>

        <div class="aside-actions">
          <strong class="aside-subtitle">字段选择</strong>
          <el-button plain disabled title="资产核对字段选择暂未接入 Web 端">全选</el-button>
          <el-button type="primary" disabled title="资产核对字段选择暂未接入 Web 端">
            填充选中
          </el-button>
          <div class="aside-divider" aria-hidden="true" />
          <strong class="aside-subtitle">家庭成员</strong>
          <el-button type="primary" disabled title="暂无可导入的家庭成员来源">
            导入全部家庭成员
          </el-button>
        </div>
      </aside>
    </div>

    <!-- 底部按钮栏：放弃申请 / 保存草稿 / 上一步 / 下一步 / 提交 -->
    <footer class="form-actions">
      <div class="form-actions__cell">
        <el-button v-if="!isViewMode" class="action-abandon" plain @click="handleAbandon">
          放弃申请
        </el-button>
      </div>
      <div class="form-actions__cell">
        <el-button
          v-if="!isViewMode"
          :loading="saving"
          :disabled="classifying || submitting || !formReady"
          @click="saveDraft"
        >
          保存草稿
        </el-button>
      </div>
      <div class="form-actions__cell">
        <el-button
          v-if="currentStep > 1"
          :disabled="saving || classifying || submitting"
          @click="prevStep"
        >
          ← 上一步
        </el-button>
      </div>
      <div class="form-actions__cell">
        <el-button
          v-if="currentStep < 5"
          type="primary"
          :disabled="saving || classifying || submitting"
          @click="nextStep"
        >
          下一步 →
        </el-button>
      </div>
      <div class="form-actions__cell form-actions__cell--submit">
        <span
          v-if="currentStep >= 5 && !isViewMode && !hasClassification"
          class="submit-hint"
        >
          请先完成分类判定
        </span>
        <el-button
          v-if="currentStep >= 5 && !isViewMode"
          class="action-submit"
          :loading="submitting"
          :disabled="saving || classifying || !formReady || !hasClassification"
          :title="hasClassification ? '保存并进入档案制作' : '请先完成分类判定'"
          @click="handleGoArchive"
        >
          提交
        </el-button>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.form-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  max-width: none;
  min-height: 100%;
  margin: 0;
  padding: 0;
  background-color: var(--nc-paper);
}

/* ── 顶部标题栏（旧版 Primary 横条） ─────────────────────────── */
.form-hero {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 18px 20px;
  background-color: var(--nc-seal);
  box-shadow: 0 4px 8px rgba(127, 35, 27, 0.3);
  color: #fff;
}

.form-hero__main {
  display: flex;
  align-items: center;
  gap: 16px;
  min-width: 0;
}

.form-hero__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  margin-left: auto;
}

.form-hero :deep(.hero-back.el-button) {
  flex: none;
  width: 36px;
  height: 36px;
  margin: 0;
  border-color: rgba(255, 255, 255, 0.5);
  background-color: rgba(255, 255, 255, 0.1);
  color: #fff;
}

.form-hero :deep(.hero-back.el-button:hover) {
  border-color: #fff;
  background-color: #fff;
  color: var(--nc-seal);
}

.hero-icon {
  flex: none;
  color: #fff;
}

.hero-text {
  min-width: 0;
}

.hero-title {
  margin: 0;
  color: #fff;
  font-family: var(--nc-font-serif);
  font-size: 20px;
  font-weight: 700;
  line-height: 1.35;
  letter-spacing: 0.04em;
}

.hero-subtitle {
  margin: 2px 0 0;
  color: var(--nc-seal-soft);
  font-size: 14px;
  line-height: 1.5;
  opacity: 0.9;
}

.hero-no {
  padding-right: 8px;
  border-right: 1px solid rgba(255, 255, 255, 0.35);
  color: rgba(255, 255, 255, 0.78);
  font-family: var(--nc-font-mono);
  font-size: 12px;
  line-height: 1.5;
  white-space: nowrap;
}

.hero-chip {
  flex: none;
  padding: 5px 12px;
  border: 1px solid rgba(255, 255, 255, 0.35);
  border-radius: 2px;
  background-color: rgba(255, 255, 255, 0.12);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 1.5;
  white-space: nowrap;
}

.hero-chip--mode {
  border-color: #fff;
  background-color: #fff;
  color: var(--nc-seal);
  font-weight: 700;
}

.form-hero :deep(.hero-action.el-button) {
  width: 32px;
  height: 32px;
  margin: 0;
  border-color: rgba(255, 255, 255, 0.35);
  background-color: transparent;
  color: #fff;
}

.form-hero :deep(.hero-action.el-button:hover) {
  border-color: #fff;
  background-color: #fff;
  color: var(--nc-seal);
}

/* ── 步骤卡片条 ───────────────────────────────────────────────── */
.form-steps {
  padding: 12px 20px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: #fff;
  box-shadow: 0 1px 3px rgba(23, 19, 16, 0.03);
}

.step-scroll {
  width: 100%;
  overflow-x: auto;
  scrollbar-width: thin;
}

.step-track {
  display: flex;
  align-items: stretch;
  width: 100%;
}

.step-card {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  min-width: 0;
  margin: 0;
  padding: 10px 16px;
  border: 1px solid var(--nc-line);
  border-radius: 2px;
  background-color: #fff;
  color: var(--nc-muted-2);
  font-family: inherit;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    border-color var(--nc-transition),
    color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.step-card:hover {
  border-color: var(--nc-seal-light);
}

.step-card.is-done {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.step-card.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal);
  color: #fff;
  box-shadow: 0 2px 6px rgba(127, 35, 27, 0.25);
}

.step-card.is-locked {
  opacity: 0.55;
  cursor: not-allowed;
}

.step-card.is-locked:hover {
  border-color: var(--nc-line);
}

.step-card__no {
  font-family: var(--nc-font-mono);
  font-size: 18px;
  font-weight: 700;
  line-height: 1.2;
}

.step-card__label {
  font-size: 17px;
  font-weight: 600;
  line-height: 1.35;
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.step-link {
  flex: 0 0 24px;
  align-self: center;
  height: 2px;
  margin: 0 4px;
  background-color: var(--nc-line-strong);
}

.step-link.is-done {
  background-color: var(--nc-seal);
}

/* ── 回访记录区（五步卡之后、内容区之前） ─────────────────────── */
.form-visit {
  padding: 12px 16px 0;
}

/* ── 内容区 ───────────────────────────────────────────────────── */
.form-body {
  display: grid;
  flex: 1 1 auto;
  grid-template-columns: minmax(0, 1fr) 320px;
  align-items: stretch;
  min-height: 0;
}

.form-main {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  padding: 16px;
}

.asset-alert,
.form-alert {
  margin: 0;
}

.loading-card {
  padding: 22px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

/* ── 右侧来源数据栏（旧版 320px） ─────────────────────────────── */
.form-aside {
  display: flex;
  flex-direction: column;
  min-width: 0;
  border-left: 1px solid var(--nc-line);
  background-color: #fff;
}

.aside-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: var(--nc-seal);
}

.aside-head strong {
  color: var(--nc-ink);
  font-size: 17px;
  font-weight: 700;
}

.aside-body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
}

.aside-note {
  margin: 0;
  padding: 10px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: 12px;
  line-height: 1.6;
}

.aside-empty {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 40px 20px;
  color: var(--nc-disabled);
  font-size: 14px;
}

.aside-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.aside-actions :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

.aside-divider {
  height: 1px;
  margin: 4px 0;
  background-color: var(--nc-line);
}

.aside-subtitle {
  margin-top: 4px;
  color: var(--nc-ink);
  font-size: 16px;
  font-weight: 700;
}

/* ── 底部按钮栏 ───────────────────────────────────────────────── */
.form-actions {
  position: sticky;
  bottom: 0;
  z-index: 8;
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
  padding: 12px 20px;
  background-color: #fff;
  box-shadow: 0 -2px 4px rgba(23, 19, 16, 0.1);
}

.form-actions__cell {
  display: flex;
  min-width: 0;
}

.form-actions__cell :deep(.el-button) {
  width: 100%;
  min-width: 100px;
  margin-left: 0;
}

.form-actions__cell--submit {
  flex-wrap: wrap;
}

.submit-hint {
  flex: 1 1 100%;
  color: var(--nc-amber-dark);
  font-size: 12px;
  line-height: 1.5;
  text-align: center;
}

.form-actions :deep(.action-abandon) {
  border-color: rgba(127, 35, 27, 0.45);
  color: var(--nc-seal-dark);
}

.form-actions :deep(.action-abandon:hover) {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
}

.form-actions :deep(.action-submit) {
  border-color: var(--nc-pine);
  background-color: var(--nc-pine);
  color: #fff;
}

.form-actions :deep(.action-submit:hover) {
  border-color: var(--nc-pine-light);
  background-color: var(--nc-pine-light);
  color: #fff;
}

/* ── 响应式 ───────────────────────────────────────────────────── */
@media (max-width: 1100px) {
  .form-body {
    grid-template-columns: minmax(0, 1fr);
  }

  .form-aside {
    display: none;
  }
}

@media (max-width: 900px) {
  .step-track {
    min-width: 760px;
  }

  .step-card {
    flex: 0 0 136px;
  }
}

@media (max-width: 767px) {
  .form-hero {
    align-items: flex-start;
    padding: 14px 12px;
  }

  .form-hero__main {
    gap: 12px;
  }

  .hero-title {
    font-size: 18px;
  }

  .hero-subtitle {
    font-size: 13px;
  }

  .form-hero__meta {
    justify-content: flex-start;
    width: 100%;
    margin-left: 0;
  }

  .hero-no {
    display: none;
  }

  .form-steps {
    padding: 12px;
  }

  .form-visit {
    padding: 12px 12px 0;
  }

  .form-main {
    padding: 12px;
  }

  .form-actions {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
    padding: 10px 12px;
  }

  .form-actions__cell:empty {
    display: none;
  }

  .form-actions__cell--submit {
    grid-column: 1 / -1;
  }
}
</style>
