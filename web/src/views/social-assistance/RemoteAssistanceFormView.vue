<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Delete, Document, Printer, Refresh } from '@element-plus/icons-vue'
import {
  createApplicationForm,
  deleteApplication,
  getApplicationForm,
  updateApplicationForm,
} from '@/api/applicationForm'
import { getErrorMessage } from '@/api/http'
import { completeRemoteAssistance } from '@/api/remoteAssistance'
import Step1Basic from '@/views/applications/form/Step1Basic.vue'
import Step4Survey from '@/views/applications/form/Step4Survey.vue'
import { createHeadMember, normalizeForm } from '@/views/applications/form/formModel'
import { createEmptyForm, type ApplicationFormModel } from '@/views/applications/form/types'
import { validateBasic } from '@/views/applications/form/validation'

const route = useRoute()
const router = useRouter()

const LIST_PATH = '/social-assistance/remote-assistance'

const form = ref<ApplicationFormModel>(createEmptyForm())
const loading = ref(false)
const loadError = ref('')
const saving = ref(false)
const completing = ref(false)
const actionError = ref('')

let loadTicket = 0
let skipNextRouteLoad = false

const rawId = computed(() => {
  const value = route.params.id
  return Array.isArray(value) ? (value[0] ?? '') : (value ?? '')
})

const isNew = computed(
  () => !rawId.value || rawId.value === 'new' || route.path.replace(/\/+$/, '').endsWith('/new'),
)

const applicationId = computed(() => (isNew.value ? 0 : Number(rawId.value) || 0))

const formReady = computed(
  () => isNew.value || (form.value.id > 0 && form.value.id === applicationId.value),
)

const isCompleted = computed(
  () => form.value.status === 'RemoteAssistanceCompleted',
)

const isReadonly = computed(() => isCompleted.value)

const statusLabel = computed(() => {
  if (form.value.status === 'RemoteAssistanceCompleted') return '异地协助已办结'
  if (form.value.status === 'RemoteAssistanceDraft') return '异地协助办理中'
  if (form.value.statusText) return form.value.statusText
  return '草稿'
})

const modeLabel = computed(() => (isReadonly.value ? '已办结' : '办理中'))

const basicIssues = computed(() => validateBasic(form.value.basic))
const step1Valid = computed(() => basicIssues.value.length === 0)

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
    empty.applicationType = 'RemoteAssistance'
    empty.members = [createHeadMember(empty.basic)]
    form.value = empty
    loading.value = false
    return
  }

  if (!applicationId.value) {
    loading.value = false
    loadError.value = '缺少记录编号，无法加载表单'
    return
  }

  loading.value = true
  try {
    const data = await getApplicationForm(applicationId.value)
    if (ticket !== loadTicket) return
    applyForm(data)
    if (!form.value.id) form.value.id = applicationId.value
    // 确保 applicationType 为 RemoteAssistance
    form.value.applicationType = 'RemoteAssistance'
  } catch (error) {
    if (ticket !== loadTicket) return
    loadError.value = getErrorMessage(error, '表单加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function moveToEditRoute(id: number): void {
  if (!id) return
  const registered = router.getRoutes().some((record) => record.path.includes('/:id/edit'))
  if (!registered) return
  skipNextRouteLoad = true
  void router.replace(`${LIST_PATH}/${id}/edit`)
}

async function persist(options: { updateRoute?: boolean } = {}): Promise<number> {
  if (!formReady.value) {
    throw new Error('表单尚未加载完成，请刷新后重试')
  }

  const creating = form.value.id <= 0
  const payload = { ...form.value, applicationType: 'RemoteAssistance' }
  const result = creating
    ? await createApplicationForm(payload)
    : await updateApplicationForm(form.value.id, payload)

  if (result.id) form.value.id = result.id
  if (result.applicationNo) form.value.applicationNo = result.applicationNo
  if (result.status) form.value.status = result.status
  if (result.statusText) form.value.statusText = result.statusText
  if (creating && !result.status) {
    form.value.status = 'RemoteAssistanceDraft'
    form.value.statusText = '异地协助办理中'
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
    ElMessage.success('已保存')
  } catch (error) {
    actionError.value = getErrorMessage(error, '保存失败')
    ElMessage.error(actionError.value)
  } finally {
    saving.value = false
  }
}

async function handleComplete(): Promise<void> {
  if (completing.value || saving.value || isReadonly.value) return

  if (!step1Valid.value) {
    ElMessage.warning(basicIssues.value[0]?.message ?? '请先完善第 1 步基本信息')
    return
  }

  try {
    await ElMessageBox.confirm(
      '确定办结吗？办结后不可再编辑。',
      '确认办结',
      { type: 'warning', confirmButtonText: '确认办结', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  completing.value = true
  actionError.value = ''
  try {
    // 先保存，再办结
    await persist({ updateRoute: false })
    if (form.value.id <= 0) throw new Error('保存后未取得记录编号，无法办结')

    await completeRemoteAssistance(form.value.id)
    form.value.status = 'RemoteAssistanceCompleted'
    form.value.statusText = '异地协助已办结'
    ElMessage.success('异地协助已办结')
  } catch (error) {
    actionError.value = getErrorMessage(error, '办结失败')
    ElMessage.error(actionError.value)
  } finally {
    completing.value = false
  }
}

async function handleDelete(): Promise<void> {
  if (form.value.id <= 0) return

  try {
    await ElMessageBox.confirm(
      `确定删除异地协助记录「${form.value.applicationNo || form.value.id}」吗？删除后不可恢复。`,
      '删除记录',
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
    ElMessage.success('记录已删除')
    void router.push(LIST_PATH)
  } catch (error) {
    actionError.value = getErrorMessage(error, '删除失败')
    ElMessage.error(actionError.value)
  }
}

function outputDocs(): void {
  if (form.value.id <= 0) {
    ElMessage.warning('请先保存记录后再出文书')
    return
  }
  void router.push({
    path: '/archives/output',
    query: {
      businessType: 'FamilyApplication',
      businessId: String(form.value.id),
      docTypes: '入户调查,邻里访问',
    },
  })
}

function goBack(): void {
  void router.push(LIST_PATH)
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
    <header class="form-hero">
      <div class="form-hero__main">
        <el-button
          class="hero-back"
          :icon="ArrowLeft"
          circle
          aria-label="返回列表"
          title="返回列表"
          @click="goBack"
        />
        <el-icon class="hero-icon" :size="34"><Document /></el-icon>
        <div class="hero-text">
          <h1 class="hero-title">异地协助</h1>
          <p class="hero-subtitle">入户调查 · 邻里走访 · 本地留存</p>
        </div>
      </div>

      <div class="form-hero__meta">
        <span v-if="!isNew" class="hero-no">{{ form.applicationNo || `#${form.id}` }}</span>
        <span class="hero-chip">{{ statusLabel }}</span>
        <span v-if="form.remoteMarkerText" class="hero-chip hero-chip--marker">
          {{ form.remoteMarkerText }}
        </span>
        <span class="hero-chip hero-chip--mode">{{ modeLabel }}</span>
        <el-button
          v-if="!isNew"
          class="hero-action"
          :icon="Refresh"
          circle
          :loading="loading"
          aria-label="刷新"
          title="刷新"
          @click="loadForm"
        />
        <el-button
          v-if="!isNew && !isReadonly"
          class="hero-action"
          :icon="Delete"
          circle
          aria-label="删除记录"
          title="删除记录"
          @click="handleDelete"
        />
      </div>
    </header>

    <el-alert
      v-if="loadError"
      class="form-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadForm">重新加载</el-button>
    </el-alert>

    <el-alert
      v-if="actionError"
      class="form-alert"
      type="error"
      :title="actionError"
      :closable="false"
      show-icon
      @close="actionError = ''"
    />

    <div v-if="loading" class="loading-card">
      <el-skeleton :rows="8" animated />
    </div>

    <template v-else>
      <section class="form-section" aria-label="基本信息">
        <div class="section-header">
          <span class="section-no">1</span>
          <h2 class="section-title">基本信息</h2>
        </div>
        <Step1Basic :form="form" :readonly="isReadonly" @reload="loadForm" />
      </section>

      <section class="form-section" aria-label="入户调查">
        <div class="section-header">
          <span class="section-no">2</span>
          <h2 class="section-title">入户调查</h2>
        </div>
        <Step4Survey
          :form="form"
          :busy="saving || completing || isReadonly"
        />
      </section>

      <section class="form-section" aria-label="文书输出">
        <div class="section-header">
          <span class="section-no">3</span>
          <h2 class="section-title">文书输出</h2>
        </div>
        <div class="doc-actions">
          <p class="doc-hint">
            可输出《入户调查表》和《邻里访问调查表》。办结前请先保存。
          </p>
          <el-button
            :icon="Printer"
            :disabled="form.id <= 0"
            @click="outputDocs"
          >
            出文书
          </el-button>
        </div>
      </section>
    </template>

    <footer class="form-actions">
      <div class="form-actions__cell">
        <el-button :icon="ArrowLeft" @click="goBack">返回列表</el-button>
      </div>
      <div class="form-actions__cell">
        <el-button
          v-if="!isReadonly"
          type="primary"
          :icon="Document"
          :loading="saving"
          :disabled="completing || !formReady"
          @click="saveDraft"
        >
          保存
        </el-button>
      </div>
      <div class="form-actions__cell form-actions__cell--submit">
        <span v-if="!isReadonly && !step1Valid" class="submit-hint">
          请先完善基本信息
        </span>
        <el-button
          v-if="!isReadonly"
          type="success"
          :loading="completing"
          :disabled="saving || !formReady || !step1Valid"
          @click="handleComplete"
        >
          办结
        </el-button>
        <span v-if="isReadonly" class="readonly-note">已办结，仅可查看与出文书</span>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.form-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  min-height: 100%;
  background-color: var(--nc-paper);
}

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
  white-space: nowrap;
}

.hero-chip--mode {
  border-color: #fff;
  background-color: #fff;
  color: var(--nc-seal);
  font-weight: 700;
}

.hero-chip--marker {
  border-color: #f0d9a8;
  background-color: #fdf3dd;
  color: #8a5a00;
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

.form-alert {
  margin: 12px 20px 0;
}

.loading-card {
  padding: 20px;
}

.form-section {
  margin: 16px 20px 0;
}

.section-header {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
}

.section-no {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background-color: var(--nc-seal);
  color: #fff;
  font-size: 14px;
  font-weight: 700;
  flex: none;
}

.section-title {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 18px;
  font-weight: 700;
}

.doc-actions {
  padding: 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.doc-hint {
  margin: 0 0 12px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.form-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 24px;
  padding: 14px 20px;
  border-top: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
}

.form-actions__cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.form-actions__cell--submit {
  margin-left: auto;
}

.submit-hint {
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
}

.readonly-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}
</style>
