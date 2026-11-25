<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft } from '@element-plus/icons-vue'
import {
  getChangeManagementDetail,
  getChangeManagementFilters,
  submitHouseholdDeath,
} from '@/api/changeManagement'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type {
  ChangeManagementDetail,
  ChangeManagementFilters,
  ChangeOperationResult,
} from '@/types/changeManagement'
import ChangeResultDialog from './ChangeResultDialog.vue'
import { ageText, formatMoney } from './format'

const LIST_PATH = '/social-assistance/change-management'

const route = useRoute()
const router = useRouter()

const applicationId = computed(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0
})

const detail = ref<ChangeManagementDetail | null>(null)
const filters = ref<ChangeManagementFilters | null>(null)
const loading = ref(false)
const loadError = ref('')

const deathDateTime = ref('')
const deathReasonCode = ref('')
const deathReasonName = ref('')
const otherDeathReason = ref('')
const deathCertificateNo = ref('')
const selectedNewHeadId = ref<number | null>(null)
const submitting = ref(false)
const result = ref<ChangeOperationResult | null>(null)
const resultVisible = ref(false)

const eligibleMembers = computed(() => {
  if (!detail.value) return []
  return detail.value.members.filter(
    (member) =>
      !member.isSupport &&
      !member.isHouseholdHead &&
      member.id !== detail.value?.head.memberId,
  )
})

const isSinglePersonHousehold = computed(() => eligibleMembers.value.length === 0)
const isOtherReason = computed(
  () => deathReasonCode.value === 'Other' || deathReasonName.value === '其他',
)
const submitButtonText = computed(() =>
  isSinglePersonHousehold.value ? '确认整户停止' : '停旧建新并去规划新档案',
)

const canSubmit = computed(
  () =>
    Boolean(detail.value) &&
    Boolean(detail.value?.canManage) &&
    Boolean(detail.value?.canHouseholdDeath) &&
    Boolean(deathDateTime.value) &&
    Boolean(deathReasonCode.value) &&
    (!isOtherReason.value || otherDeathReason.value.trim().length > 0) &&
    (isSinglePersonHousehold.value || selectedNewHeadId.value !== null) &&
    !submitting.value,
)

const selectedNewHead = computed(
  () => eligibleMembers.value.find((member) => member.id === selectedNewHeadId.value) ?? null,
)

function disableFutureDate(date: Date): boolean {
  return date.getTime() > Date.now()
}

function deathReasonChanged(code: string): void {
  const option = filters.value?.deathReasons.find((item) => item.code === code)
  deathReasonName.value = option?.name ?? ''
  if (code !== 'Other') {
    otherDeathReason.value = ''
  }
}

async function load(): Promise<void> {
  if (applicationId.value <= 0) {
    loadError.value = '申请 ID 无效'
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    const [detailResponse, filterResponse] = await Promise.all([
      getChangeManagementDetail(applicationId.value),
      getChangeManagementFilters(),
    ])
    detail.value = detailResponse
    filters.value = filterResponse
  } catch (error) {
    loadError.value = getErrorMessage(error, '户主死亡变更数据加载失败')
  } finally {
    loading.value = false
  }
}

async function submit(): Promise<void> {
  const current = detail.value
  if (!current) return

  if (!deathDateTime.value) {
    ElMessage.warning('请选择死亡日期与时间')
    return
  }

  if (new Date(deathDateTime.value).getTime() > Date.now()) {
    ElMessage.warning('死亡时间不能晚于当前时间')
    return
  }

  if (!deathReasonCode.value || !deathReasonName.value) {
    ElMessage.warning('请选择死亡原因')
    return
  }

  if (isOtherReason.value && !otherDeathReason.value.trim()) {
    ElMessage.warning('死亡原因为「其他」时，请填写补充说明')
    return
  }

  if (!isSinglePersonHousehold.value && !selectedNewHead.value) {
    ElMessage.warning('请选择新户主')
    return
  }

  const reason = isOtherReason.value
    ? `其他:${otherDeathReason.value.trim()}`
    : deathReasonName.value

  const confirmText = isSinglePersonHousehold.value
    ? `该户为单人户。确认登记 ${current.head.name || current.applicantName} 死亡并整户停止保障？本次不建立新档案。`
    : `确认登记原户主 ${current.head.name || current.applicantName} 死亡，并由 ${selectedNewHead.value?.name} 接任新户主？旧档案停止，新档案以草稿建立。`

  try {
    await ElMessageBox.confirm(confirmText, '户主死亡变更', {
      type: 'warning',
      confirmButtonText: isSinglePersonHousehold.value ? '确认整户停止' : '确认变更',
      cancelButtonText: '取消',
    })
  } catch {
    return
  }

  const datePart = deathDateTime.value.slice(0, 10)
  const timePart = deathDateTime.value.length >= 16 ? deathDateTime.value.slice(11, 16) : ''

  submitting.value = true
  try {
    result.value = await submitHouseholdDeath(current.applicationId, {
      deceasedHeadMemberId: current.head.memberId > 0 ? current.head.memberId : null,
      newHeadMemberId: isSinglePersonHousehold.value ? null : selectedNewHead.value?.id ?? null,
      deathDate: datePart,
      deathTime: timePart,
      deathReason: reason,
      deathCertificateNo: deathCertificateNo.value.trim(),
    })
    resultVisible.value = true
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '户主死亡变更失败'))
  } finally {
    submitting.value = false
  }
}

function handleResultDone(): void {
  void router.push(LIST_PATH)
}

function goBack(): void {
  void router.push(LIST_PATH)
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page death-change-page">
    <NcPageHeader
      title="户主死亡变更"
      description="登记死亡信息并选择新户主。多人口户停旧建新；单人户整户停止、不建立新档案。"
      kicker="保障对象动态管理 · 户主死亡"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回列表</el-button>
      </template>
    </NcPageHeader>

    <el-alert v-if="loadError" type="error" :title="loadError" :closable="false" show-icon>
      <el-button link type="primary" @click="load()">重新加载</el-button>
    </el-alert>

    <el-skeleton v-if="loading" :rows="9" animated />

    <template v-else-if="detail">
      <el-alert
        v-if="!detail.canManage"
        type="warning"
        :closable="false"
        show-icon
        title="当前账号无「办理信息变更」权限（CHANGE_MANAGE），不能提交户主死亡变更。"
      />
      <el-alert
        v-else-if="!detail.canHouseholdDeath"
        type="warning"
        :closable="false"
        show-icon
        title="当前档案已停保，不能再办理户主死亡变更。"
      />

      <NcCard title="原户主信息（只读）" padding="md">
        <div class="original-grid">
          <div>
            <span>姓名</span>
            <strong>{{ detail.head.name || detail.applicantName || '—' }}</strong>
          </div>
          <div>
            <span>身份证号</span>
            <strong class="mono">{{ detail.head.idCardMasked || detail.applicantIdCardMasked || '—' }}</strong>
          </div>
          <div>
            <span>家庭人口</span>
            <strong>{{ detail.familySize }} 人</strong>
          </div>
          <div>
            <span>当前保障</span>
            <strong>{{ formatMoney(detail.totalGuaranteeAmount) }} 元/月</strong>
          </div>
          <div>
            <span>保障分类</span>
            <strong>{{ detail.classificationName || '—' }}</strong>
          </div>
          <div>
            <span>乡镇 / 村</span>
            <strong>{{ [detail.town, detail.village].filter(Boolean).join(' / ') || '—' }}</strong>
          </div>
        </div>
      </NcCard>

      <NcCard title="死亡信息" subtitle="死亡日期与时间为必填项，不能晚于当前时间" padding="md">
        <div class="form-grid">
          <label class="field field--wide">
            <span>死亡日期与时间 *</span>
            <el-date-picker
              v-model="deathDateTime"
              type="datetime"
              value-format="YYYY-MM-DD HH:mm"
              format="YYYY-MM-DD HH:mm"
              placeholder="请选择死亡日期与确切时间"
              :disabled-date="disableFutureDate"
            />
          </label>
          <label class="field">
            <span>死亡原因 *</span>
            <el-select v-model="deathReasonCode" placeholder="请选择" @change="deathReasonChanged">
              <el-option
                v-for="option in filters?.deathReasons ?? []"
                :key="option.code"
                :label="option.name"
                :value="option.code"
              />
            </el-select>
          </label>
          <label class="field">
            <span>死亡证明编号</span>
            <el-input v-model="deathCertificateNo" maxlength="60" placeholder="选填" />
          </label>
          <label v-if="isOtherReason" class="field field--wide">
            <span>其他原因说明 *</span>
            <el-input v-model="otherDeathReason" maxlength="200" placeholder="请补充说明" />
          </label>
        </div>
      </NcCard>

      <el-alert
        v-if="isSinglePersonHousehold"
        type="warning"
        :closable="false"
        show-icon
        title="该户为单人户（户内唯一成员即户主）"
        description="户主死亡后无继承人接任，将整户停止保障，不建立新档案。"
      />

      <NcCard
        v-else
        title="请选择新户主"
        subtitle="仅显示本户非户主、非赡养抚养扶养人的共同生活成员"
        padding="md"
      >
        <div v-if="eligibleMembers.length > 0" class="candidate-grid">
          <button
            v-for="member in eligibleMembers"
            :key="member.id"
            type="button"
            class="candidate"
            :class="{ 'is-selected': selectedNewHeadId === member.id }"
            @click="selectedNewHeadId = member.id"
          >
            <span class="candidate__badge">{{ member.name.slice(0, 1) || '—' }}</span>
            <span class="candidate__main">
              <strong>{{ member.name }}</strong>
              <small>
                {{ member.relationshipText || member.relationship }} · {{ member.gender }} ·
                {{ ageText(member.age) }}
              </small>
              <small>{{ member.idCardMasked }}</small>
            </span>
            <span class="candidate__health">{{ member.healthStatusText || member.healthStatus || '—' }}</span>
          </button>
        </div>
        <el-empty v-else :image-size="60" description="暂无可选成员" />
      </NcCard>

      <div class="footer-bar">
        <span v-if="isSinglePersonHousehold" class="footer-note">
          单人户：提交后整户停止，不建立新档案。
        </span>
        <span v-else-if="selectedNewHead" class="footer-note">
          新户主：{{ selectedNewHead?.name }}（{{ selectedNewHead?.relationshipText || selectedNewHead?.relationship }}）
        </span>
        <span v-else class="footer-note">尚未选择新户主</span>
        <div class="footer-actions">
          <el-button @click="goBack">返回</el-button>
          <el-button type="primary" :disabled="!canSubmit" :loading="submitting" @click="submit">
            {{ submitButtonText }}
          </el-button>
        </div>
      </div>
    </template>

    <ChangeResultDialog
      v-model="resultVisible"
      :result="result"
      title="户主死亡变更结果"
      @done="handleResultDone"
    />
  </div>
</template>

<style scoped>
.death-change-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.original-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px 16px;
}

.original-grid div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.original-grid span {
  color: var(--nc-muted);
  font-size: 10px;
}

.original-grid strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.original-grid strong.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 14px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.field > span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.field--wide {
  grid-column: 1 / -1;
}

.field :deep(.el-date-editor),
.field :deep(.el-select) {
  width: 100%;
}

.candidate-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 9px;
}

.candidate {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 11px 12px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: inherit;
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.candidate:hover {
  border-color: var(--nc-line-strong);
}

.candidate.is-selected {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.candidate__badge {
  display: grid;
  flex: none;
  place-items: center;
  width: 34px;
  height: 34px;
  border: 1px solid rgba(46, 71, 56, 0.25);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-family: var(--nc-font-serif);
  font-size: 15px;
  font-weight: 700;
}

.candidate__main {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.candidate__main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.candidate__main small {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 10px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.candidate__health {
  flex: none;
  margin-left: auto;
  color: var(--nc-muted);
  font-size: 10px;
}

.footer-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 13px 16px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-2);
}

.footer-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.footer-actions {
  display: flex;
  gap: 8px;
}

@media (max-width: 760px) {
  .original-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .form-grid {
    grid-template-columns: 1fr;
  }

  .candidate-grid {
    grid-template-columns: 1fr;
  }

  .footer-actions {
    width: 100%;
  }

  .footer-actions .el-button {
    flex: 1;
  }
}
</style>
