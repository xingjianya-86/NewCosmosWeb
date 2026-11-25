<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Clock, Delete, Plus } from '@element-plus/icons-vue'
import {
  createQuickAssetCheck,
  getAssetCheckHistory,
  getAssetOperatorContext,
  type AssetHistoryTaskItem,
  type AssetOperatorContext,
  type QuickAssetCheckCreated,
  type QuickAssetCheckPayload,
} from '@/api/assetVerification'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { APPLICATION_REASON_OPTIONS } from '@/views/applications/form/options'

const router = useRouter()
const context = ref<AssetOperatorContext | null>(null)
const loadingContext = ref(false)
const submitting = ref(false)

/** 旧版 AssetCheckItemDto.Relationships 的 11 项显示值。 */
const RELATIONSHIP_OPTIONS = [
  '本人/户主',
  '配偶',
  '儿子',
  '女儿',
  '孙子',
  '孙女',
  '父亲',
  '母亲',
  '兄弟',
  '姐妹',
  '其他',
]
const ID_TYPE_OPTIONS = [{ label: '居民身份证', value: 'ResidentIdCard' }]

function today(): string {
  const now = new Date()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

const form = reactive({
  applicationDate: today(),
  community: '',
  addressDetail: '',
  contactPhone: '',
  applicationReason: 'LowIncome',
})

interface ApplicantRow {
  name: string
  idType: string
  idCard: string
  relationship: string
  isHead: boolean
}

const applicants = ref<ApplicantRow[]>([
  { name: '', idType: 'ResidentIdCard', idCard: '', relationship: '本人/户主', isHead: true },
])

const isAgent = ref(false)
const agent = reactive({
  name: '',
  idType: 'ResidentIdCard',
  idCard: '',
  relationship: '',
})

const villages = computed(() => context.value?.villages ?? [])
const canSubmit = computed(() => !submitting.value && !loadingContext.value)

const historyVisible = ref(false)
const historyLoadingIdCard = ref('')
const historyTitle = ref('历史核查记录')
const historyTasks = ref<AssetHistoryTaskItem[]>([])

async function loadContext(): Promise<void> {
  loadingContext.value = true
  try {
    context.value = await getAssetOperatorContext()
    if (context.value.villages.length > 0 && !form.community) {
      form.community = context.value.villages[0].code
    }
  } catch (error) {
    context.value = null
    ElMessage.error(getErrorMessage(error, '读取经办机构区划失败'))
  } finally {
    loadingContext.value = false
  }
}

function addApplicant(): void {
  applicants.value.push({
    name: '',
    idType: 'ResidentIdCard',
    idCard: '',
    relationship: '配偶',
    isHead: false,
  })
}

function removeApplicant(row: ApplicantRow): void {
  if (row.isHead) return
  const index = applicants.value.indexOf(row)
  if (index >= 0) applicants.value.splice(index, 1)
}

function formatHistoryDate(value: string): string {
  return value ? value.slice(0, 10) : '—'
}

/** 旧版授权人行“查询历史”：按身份证查询历史核查记录。 */
async function queryHistory(row: ApplicantRow): Promise<void> {
  const idCard = row.idCard.trim()
  if (idCard.length !== 18) {
    ElMessage.warning('请输入完整的18位身份证号后查询')
    return
  }

  historyLoadingIdCard.value = idCard
  try {
    const response = await getAssetCheckHistory(idCard)
    const tasks = response.tasks ?? []
    if (tasks.length === 0) {
      ElMessage.info('暂无历史核查记录')
      return
    }

    historyTasks.value = tasks
    historyTitle.value = `历史核查记录 · ${row.name.trim() || '当前授权人'}`
    historyVisible.value = true
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '查询历史核查记录失败'))
  } finally {
    historyLoadingIdCard.value = ''
  }
}

/** 提交成功后按旧版询问是否去打印授权书/核查报告。 */
async function promptPrint(batchId: string): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '提交成功，是否去打印授权书/核查报告？',
      '提交成功',
      {
        type: 'success',
        confirmButtonText: '去打印',
        cancelButtonText: '稍后',
        distinguishCancelAndClose: true,
      },
    )
  } catch {
    await router.push('/asset-verification/tasks')
    return
  }

  await router.push({
    path: '/archives/production',
    query: { assetCheckBatchId: batchId },
  })
}

function reset(): void {
  form.applicationDate = today()
  form.community = villages.value[0]?.code ?? ''
  form.addressDetail = ''
  form.contactPhone = ''
  form.applicationReason = 'LowIncome'
  applicants.value = [
    { name: '', idType: 'ResidentIdCard', idCard: '', relationship: '本人/户主', isHead: true },
  ]
  isAgent.value = false
  agent.name = ''
  agent.idType = 'ResidentIdCard'
  agent.idCard = ''
  agent.relationship = ''
}

function validate(): string | null {
  if (!context.value?.townId) {
    return '当前账号未配置经办乡镇，请先补齐经办机构区划'
  }
  if (!form.community) {
    return '请选择村社区'
  }
  if (!form.addressDetail.trim()) {
    return '请填写详细地址'
  }
  if (!form.applicationReason) {
    return '请选择申请原因'
  }

  const validApplicants = applicants.value.filter(
    (item) => item.name.trim() || item.idCard.trim(),
  )
  if (validApplicants.length === 0) {
    return '请至少录入一名申请人信息'
  }

  const head = validApplicants.find((item) => item.isHead) ?? validApplicants[0]
  if (!head.name.trim()) {
    return '请填写申请人姓名'
  }
  if (head.idCard.trim().length !== 18) {
    return '申请人身份证号格式错误：必须为 18 位'
  }

  for (const item of validApplicants) {
    if (!item.name.trim() || !item.idCard.trim()) {
      return '授权人姓名和身份证号不能为空'
    }
    if (item.idCard.trim().length !== 18) {
      return `授权人“${item.name}”身份证号格式错误：必须为 18 位`
    }
    if (!item.relationship) {
      return `授权人“${item.name}”未选择家庭关系`
    }
  }

  if (isAgent.value) {
    if (!agent.name.trim() || !agent.idCard.trim() || !agent.relationship.trim()) {
      return '已勾选委托代理，请填写代理人姓名、身份证号和关系'
    }
    if (agent.idCard.trim().length !== 18) {
      return '代理人身份证号格式错误：必须为 18 位'
    }
  }

  return null
}

function buildPayload(overwriteExisting: boolean): QuickAssetCheckPayload {
  return {
    applicationDate: form.applicationDate,
    community: form.community,
    addressDetail: form.addressDetail.trim(),
    contactPhone: form.contactPhone.trim(),
    applicationReason: form.applicationReason,
    isAgent: isAgent.value,
    agentName: agent.name.trim(),
    agentIdType: agent.idType,
    agentIdCard: agent.idCard.trim(),
    agentRelationship: agent.relationship.trim(),
    overwriteExisting,
    applicants: applicants.value
      .filter((item) => item.name.trim() || item.idCard.trim())
      .map((item, index) => ({
        name: item.name.trim(),
        idType: item.idType,
        idCard: item.idCard.trim(),
        relationship: item.relationship,
        isHead: index === 0,
      })),
  }
}

async function submit(overwriteExisting = false): Promise<void> {
  const error = validate()
  if (error) {
    ElMessage.warning(error)
    return
  }

  submitting.value = true
  let created: QuickAssetCheckCreated | null = null
  try {
    created = await createQuickAssetCheck(buildPayload(overwriteExisting))
  } catch (submitError) {
    const status = (submitError as { response?: { status?: number } })?.response?.status
    if (status === 409 && !overwriteExisting) {
      try {
        await ElMessageBox.confirm(
          getErrorMessage(submitError, '该申请人在本业务月度已有核查记录，是否覆盖提交？'),
          '重复核查',
          {
            type: 'warning',
            confirmButtonText: '覆盖提交',
            cancelButtonText: '取消',
          },
        )
      } catch {
        return
      }
      submitting.value = false
      await submit(true)
      return
    }
    ElMessage.error(getErrorMessage(submitError, '提交核查申请失败'))
  } finally {
    submitting.value = false
  }

  if (created) {
    await promptPrint(created.batchId)
  }
}

onMounted(() => {
  void loadContext()
})
</script>

<template>
  <div class="nc-page asset-create">
    <NcPageHeader
      title="低收入人口经济状况核查申请"
      description="经济状况 · 信息录入 · 提交核查"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/asset-verification')">
          返回工作台
        </el-button>
        <el-button @click="router.push('/asset-verification/tasks')">查看历史任务</el-button>
      </template>
    </NcPageHeader>

    <NcCard title="基本信息">
      <div class="operator-row">
        <span class="operator-label">经办单位</span>
        <strong class="operator-value">
          {{ context?.unitName || context?.townName || '—' }}
        </strong>
        <span v-if="context?.townName" class="operator-town">{{ context.townName }}</span>
      </div>
      <p v-if="!loadingContext && !context?.townId" class="form-warning">
        当前账号未配置经办乡镇，请先在“经办机构管理”补齐区划。
      </p>

      <div class="form-grid form-grid--five">
        <label class="field">
          <span class="field__label">申请日期</span>
          <el-date-picker
            v-model="form.applicationDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择申请日期"
          />
        </label>

        <label class="field">
          <span class="field__label">村社区</span>
          <el-select
            v-model="form.community"
            filterable
            :loading="loadingContext"
            placeholder="请选择村社区"
          >
            <el-option
              v-for="village in villages"
              :key="village.code"
              :label="village.name"
              :value="village.code"
            />
          </el-select>
        </label>

        <label class="field">
          <span class="field__label">详细地址</span>
          <el-input v-model="form.addressDetail" placeholder="门牌号等" clearable />
        </label>

        <label class="field">
          <span class="field__label">联系方式</span>
          <el-input
            v-model="form.contactPhone"
            maxlength="11"
            placeholder="手机号码"
            clearable
          />
        </label>

        <label class="field">
          <span class="field__label">申请原因</span>
          <el-select v-model="form.applicationReason" placeholder="请选择申请原因">
            <el-option
              v-for="option in APPLICATION_REASON_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </label>
      </div>
    </NcCard>

    <NcCard title="授权人信息（家庭成员）">
      <template #actions>
        <el-button :icon="Plus" @click="addApplicant">添加授权人</el-button>
      </template>

      <div class="applicant-head">
        <span>姓名</span>
        <span>证件类型</span>
        <span>身份证号</span>
        <span>家庭关系</span>
        <span>操作</span>
      </div>

      <div
        v-for="(row, index) in applicants"
        :key="index"
        class="applicant-row"
        :class="{ 'is-head': row.isHead }"
      >
        <el-input v-model="row.name" placeholder="姓名" clearable />
        <el-select v-model="row.idType" placeholder="证件类型">
          <el-option
            v-for="option in ID_TYPE_OPTIONS"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
        <el-input v-model="row.idCard" maxlength="18" placeholder="身份证号" clearable />
        <el-select v-model="row.relationship" placeholder="家庭关系">
          <el-option
            v-for="option in RELATIONSHIP_OPTIONS"
            :key="option"
            :label="option"
            :value="option"
          />
        </el-select>
        <div class="row-actions">
          <el-button
            text
            type="primary"
            :icon="Clock"
            :loading="historyLoadingIdCard === row.idCard.trim()"
            title="查询历史核查记录"
            @click="queryHistory(row)"
          />
          <el-button
            text
            type="danger"
            :icon="Delete"
            :disabled="row.isHead"
            :title="row.isHead ? '户主行不可删除' : '删除授权人'"
            @click="removeApplicant(row)"
          >
            删除
          </el-button>
        </div>
      </div>

      <p class="field-note">
        注：第一行必须填写户主完整信息；其余授权人填写姓名、身份证号并选择家庭关系。
      </p>
    </NcCard>

    <NcCard>
      <div class="agent-head">
        <el-checkbox v-model="isAgent">委托代理人（可填写代理人信息）</el-checkbox>
      </div>

      <div v-if="isAgent" class="form-grid form-grid--four">
        <label class="field">
          <span class="field__label">代理人姓名</span>
          <el-input v-model="agent.name" placeholder="填写代理人姓名" clearable />
        </label>
        <label class="field">
          <span class="field__label">代理人证件类型</span>
          <el-select v-model="agent.idType">
            <el-option
              v-for="option in ID_TYPE_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </label>
        <label class="field">
          <span class="field__label">代理人身份证号</span>
          <el-input v-model="agent.idCard" maxlength="18" placeholder="填写代理人身份证号" clearable />
        </label>
        <label class="field">
          <span class="field__label">代理人关系</span>
          <el-input v-model="agent.relationship" placeholder="如：村干部、亲属" clearable />
        </label>
      </div>
    </NcCard>

    <div class="form-actions">
      <el-button type="primary" :loading="submitting" :disabled="!canSubmit" @click="submit()">
        提交申请
      </el-button>
      <el-button :disabled="submitting" @click="reset">重置</el-button>
      <el-button :disabled="submitting" @click="router.push('/asset-verification/tasks')">
        查看历史任务
      </el-button>
    </div>

    <el-dialog v-model="historyVisible" :title="historyTitle" width="min(680px, 92vw)">
      <el-table :data="historyTasks" size="small" border>
        <el-table-column label="日期" width="120">
          <template #default="{ row }">{{ formatHistoryDate(row.applicationDate) }}</template>
        </el-table-column>
        <el-table-column prop="statusText" label="状态" width="120" />
        <el-table-column prop="familyAddress" label="地址" min-width="260" />
      </el-table>
      <template #footer>
        <el-button @click="historyVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.asset-create {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.operator-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 10px;
  margin-bottom: 14px;
}

.operator-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.operator-value {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
}

.operator-town {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.form-warning {
  margin: 0 0 12px;
  color: var(--nc-danger);
  font-size: var(--nc-text-sm);
}

.form-grid {
  display: grid;
  gap: 14px 16px;
}

.form-grid--five {
  grid-template-columns: repeat(5, minmax(0, 1fr));
}

.form-grid--four {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field__label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.field :deep(.el-select),
.field :deep(.el-date-editor),
.field :deep(.el-input) {
  width: 100%;
}

.applicant-head,
.applicant-row {
  display: grid;
  grid-template-columns: minmax(120px, 1fr) 130px minmax(180px, 1.6fr) 150px 112px;
  gap: 10px;
  align-items: center;
}

.applicant-head {
  padding: 8px 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  background: var(--nc-paper-3);
}

.applicant-row {
  padding: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.applicant-row.is-head {
  background: var(--nc-paper-soft);
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.row-actions :deep(.el-button) {
  margin-left: 0;
  padding-inline: 6px;
}

.field-note {
  margin: 12px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.agent-head {
  margin-bottom: 14px;
}

.form-actions {
  display: flex;
  justify-content: center;
  gap: 14px;
  padding: 4px 0 24px;
}

@media (max-width: 1100px) {
  .form-grid--five {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .form-grid--four {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .form-grid--five,
  .form-grid--four {
    grid-template-columns: minmax(0, 1fr);
  }

  .applicant-head {
    display: none;
  }

  .applicant-row {
    grid-template-columns: minmax(0, 1fr);
    gap: 8px;
    margin-bottom: 12px;
    border: 1px solid var(--nc-line);
    border-radius: var(--nc-radius-sm);
  }
}
</style>
