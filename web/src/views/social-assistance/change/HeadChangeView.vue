<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft } from '@element-plus/icons-vue'
import { getChangeManagementDetail, submitHeadChange } from '@/api/changeManagement'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { ChangeManagementDetail, ChangeOperationResult } from '@/types/changeManagement'
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
const loading = ref(false)
const loadError = ref('')
const selectedMemberId = ref<number | null>(null)
const changeReason = ref('')
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

const canSubmit = computed(
  () =>
    Boolean(detail.value) &&
    Boolean(detail.value?.canManage) &&
    Boolean(detail.value?.canChangeHead) &&
    selectedMemberId.value !== null &&
    changeReason.value.trim().length > 0 &&
    !submitting.value,
)

const selectedMember = computed(
  () => eligibleMembers.value.find((member) => member.id === selectedMemberId.value) ?? null,
)

async function load(): Promise<void> {
  if (applicationId.value <= 0) {
    loadError.value = '申请 ID 无效'
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    detail.value = await getChangeManagementDetail(applicationId.value)
  } catch (error) {
    detail.value = null
    loadError.value = getErrorMessage(error, '户主变更数据加载失败')
  } finally {
    loading.value = false
  }
}

async function submit(): Promise<void> {
  const current = detail.value
  const newHead = selectedMember.value
  if (!current || !newHead) {
    ElMessage.warning('请选择新户主')
    return
  }

  if (!changeReason.value.trim()) {
    ElMessage.warning('请填写变更原因')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确认将户主从 ${current.head.name || current.applicantName} 变更为 ${newHead.name}？提交后旧档案停止，并以新户主建立新档案（草稿）。`,
      '确认户主变更',
      { type: 'warning', confirmButtonText: '确认变更', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  submitting.value = true
  try {
    result.value = await submitHeadChange(current.applicationId, {
      newHeadMemberId: newHead.id,
      changeReason: changeReason.value.trim(),
    })
    resultVisible.value = true
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '户主变更失败'))
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
  <div class="nc-page head-change-page">
    <NcPageHeader
      title="户主变更"
      description="仅变更户主，其他信息不变。提交后由 Core 停旧建新，新档案继承成员、经济与分类数据。"
      kicker="保障对象动态管理 · 户主变更"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回列表</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load()">重新加载</el-button>
    </el-alert>

    <el-skeleton v-if="loading" :rows="8" animated />

    <template v-else-if="detail">
      <el-alert
        v-if="!detail.canManage"
        type="warning"
        :closable="false"
        show-icon
        title="当前账号无「办理信息变更」权限（CHANGE_MANAGE），不能提交户主变更。"
      />
      <el-alert
        v-else-if="!detail.canChangeHead"
        type="warning"
        :closable="false"
        show-icon
        title="当前档案不满足户主变更条件（已停保或单人户）。"
      />

      <NcCard title="当前户主" padding="md">
        <div class="current-head">
          <div class="current-head__avatar">{{ (detail.head.name || '—').slice(0, 1) }}</div>
          <div class="current-head__main">
            <strong>{{ detail.head.name || '—' }}</strong>
            <span>{{ detail.head.idCardMasked || '—' }}</span>
            <small>{{ detail.applicantPhone || '—' }}</small>
          </div>
          <div class="current-head__tags">
            <span class="chip">{{ detail.classificationName || '未分类' }}</span>
            <span class="chip chip--plain">{{ detail.hukouTypeName || '—' }}</span>
          </div>
        </div>
        <div class="current-head__grid">
          <div>
            <span>保障金额（元/月）</span>
            <strong>{{ formatMoney(detail.totalGuaranteeAmount) }}</strong>
          </div>
          <div>
            <span>家庭人数</span>
            <strong>{{ detail.familySize }} 人</strong>
          </div>
          <div>
            <span>人均年收入（元）</span>
            <strong>{{ formatMoney(detail.perCapitaAnnualIncome) }}</strong>
          </div>
          <div>
            <span>乡镇 / 村</span>
            <strong>{{ [detail.town, detail.village].filter(Boolean).join(' / ') || '—' }}</strong>
          </div>
        </div>
      </NcCard>

      <NcCard title="请选择新户主" subtitle="仅显示本户非户主、非赡养抚养扶养人的共同生活成员" padding="md">
        <div v-if="eligibleMembers.length > 0" class="candidate-grid">
          <button
            v-for="member in eligibleMembers"
            :key="member.id"
            type="button"
            class="candidate"
            :class="{ 'is-selected': selectedMemberId === member.id }"
            @click="selectedMemberId = member.id"
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
        <el-empty v-else :image-size="60" description="暂无可选成员（单人户不能办理户主变更）" />
      </NcCard>

      <NcCard title="变更原因" subtitle="必填，写入变更记录与档案告知书" padding="md">
        <el-input
          v-model="changeReason"
          type="textarea"
          :rows="4"
          maxlength="500"
          show-word-limit
          placeholder="请详细描述变更原因…"
        />
      </NcCard>

      <div class="footer-bar">
        <span v-if="selectedMember" class="footer-note">
          新户主：{{ selectedMember?.name }}（{{ selectedMember?.relationshipText || selectedMember?.relationship }}）
        </span>
        <span v-else class="footer-note">尚未选择新户主</span>
        <div class="footer-actions">
          <el-button @click="goBack">取消</el-button>
          <el-button type="primary" :disabled="!canSubmit" :loading="submitting" @click="submit">
            确认变更
          </el-button>
        </div>
      </div>
    </template>

    <ChangeResultDialog
      v-model="resultVisible"
      :result="result"
      title="户主变更结果"
      @done="handleResultDone"
    />
  </div>
</template>

<style scoped>
.head-change-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.current-head {
  display: flex;
  align-items: center;
  gap: 13px;
}

.current-head__avatar {
  display: grid;
  flex: none;
  place-items: center;
  width: 48px;
  height: 48px;
  border: 1px solid rgba(46, 71, 56, 0.25);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-family: var(--nc-font-serif);
  font-size: 22px;
  font-weight: 700;
}

.current-head__main {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.current-head__main strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
}

.current-head__main span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.current-head__main small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.current-head__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-left: auto;
}

.chip {
  display: inline-flex;
  padding: 1px 8px;
  border: 1px solid rgba(46, 71, 56, 0.3);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.chip--plain {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: var(--nc-ink-2);
}

.current-head__grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  margin-top: 13px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.current-head__grid div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.current-head__grid span {
  color: var(--nc-muted);
  font-size: 10px;
}

.current-head__grid strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
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
  .current-head {
    flex-wrap: wrap;
  }

  .current-head__tags {
    width: 100%;
    margin-left: 0;
  }

  .current-head__grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
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
