<script setup lang="ts">
import { computed } from 'vue'
import type {
  ChangeHistoryItem,
  ChangeManagementDetail,
  ChangeMember,
} from '@/types/changeManagement'
import { ageText, formatAmount, formatDate, formatDateTime, formatMoney, graceDaysText } from './format'

const props = defineProps<{
  detail: ChangeManagementDetail
  history: ChangeHistoryItem[]
  historyLoading: boolean
  busy?: boolean
  showClose?: boolean
}>()

const emit = defineEmits<{
  'economic-review': []
  'member-change': []
  'head-change': []
  'household-death': []
  'grace-confirm': []
  'verify-stop': []
  close: []
}>()

const householdMembers = computed(() =>
  props.detail.members.filter((member) => !member.isSupport),
)
const supportMembers = computed(() => props.detail.members.filter((member) => member.isSupport))

const monthlyFlags = computed(() => {
  const flags = props.detail.monthly
  return [
    { label: '经济复核', done: flags.economicReviewDone },
    { label: '成员变更', done: flags.memberChangeDone },
    { label: '户主变更', done: flags.headChangeDone },
    { label: '户主死亡', done: flags.headDeathDone },
  ]
})

function memberMeta(member: ChangeMember): string {
  const parts = [member.relationshipText || member.relationship, member.gender, ageText(member.age)]
  return parts.filter((part) => Boolean(part && String(part).trim() && part !== '—')).join(' · ')
}
</script>

<template>
  <div class="detail-panel">
    <header class="detail-head">
      <div class="detail-head__main">
        <div class="detail-head__title-line">
          <h2>{{ detail.applicantName || '—' }}</h2>
          <span class="chip chip--classification">{{ detail.classificationName || '未分类' }}</span>
          <span class="chip" :class="detail.isStopped ? 'chip--stopped' : 'chip--active'">
            {{ detail.statusText || detail.status }}
          </span>
        </div>
        <p class="detail-head__meta">
          <span>{{ detail.applicantIdCardMasked || '—' }}</span>
          <span>{{ detail.applicationNo || '—' }}</span>
          <span>{{ [detail.town, detail.village].filter(Boolean).join(' / ') || '—' }}</span>
        </p>
      </div>
      <el-button v-if="showClose" text circle aria-label="关闭详情" @click="emit('close')">
        <span class="close-glyph">×</span>
      </el-button>
    </header>

    <div v-if="!detail.canManage" class="permission-note">
      当前账号无「办理信息变更」权限（CHANGE_MANAGE），仅可查看档案与变更历史。
    </div>

    <section class="stat-grid">
      <div class="stat">
        <span>保障金额（元/月）</span>
        <strong>{{ formatMoney(detail.totalGuaranteeAmount) }}</strong>
        <small>
          户月 {{ formatMoney(detail.householdMonthlyGuaranteeAmount) }} · 分类施保
          {{ formatMoney(detail.classifiedSubsidyAmount) }} · 照料
          {{ formatMoney(detail.caregiverSubsidyAmount) }}
        </small>
      </div>
      <div class="stat">
        <span>家庭人数</span>
        <strong>{{ detail.familySize }} 人</strong>
        <small>共同生活成员（不含赡养人）</small>
      </div>
      <div class="stat">
        <span>人均年收入（元）</span>
        <strong>{{ formatMoney(detail.perCapitaAnnualIncome) }}</strong>
        <small>年家庭总收入 {{ formatMoney(detail.totalAnnualIncome) }}</small>
      </div>
      <div class="stat">
        <span>户口类型</span>
        <strong class="stat__text">{{ detail.hukouTypeName || '—' }}</strong>
        <small>首次审批 {{ formatDate(detail.firstApprovedAt) }}</small>
      </div>
    </section>

    <p class="address-line">{{ detail.address || '—' }}</p>

    <section class="block">
      <div class="block-head">
        <h3>本月已办理</h3>
        <span class="block-hint">按 change_type / change_reason_type 统计当前自然月</span>
      </div>
      <div class="monthly-flags">
        <span
          v-for="flag in monthlyFlags"
          :key="flag.label"
          class="flag"
          :class="{ 'is-done': flag.done }"
        >
          {{ flag.label }}{{ flag.done ? ' · 已办理' : ' · 未办理' }}
        </span>
      </div>
    </section>

    <section v-if="detail.grace" class="block grace-block">
      <div class="block-head">
        <h3>渐退期</h3>
        <span class="flag is-done">{{ graceDaysText(detail.grace?.daysLeft) }}</span>
      </div>
      <div class="grace-grid">
        <div>
          <span>渐退前原分类</span>
          <strong>{{ detail.grace?.originalClassificationName || '—' }}</strong>
        </div>
        <div>
          <span>渐退期区间</span>
          <strong>
            {{ formatDate(detail.grace?.startDate) }} 至 {{ formatDate(detail.grace?.endDate) }}
            <template v-if="detail.grace?.months">（{{ detail.grace?.months }} 个月）</template>
          </strong>
        </div>
        <div>
          <span>原保障金额</span>
          <strong>{{ formatAmount(detail.grace?.originalGuaranteeAmount) }}</strong>
        </div>
        <div>
          <span>渐退期应发</span>
          <strong>{{ formatAmount(detail.grace?.graceGrantAmount) }}</strong>
        </div>
      </div>
    </section>

    <section class="block">
      <div class="block-head">
        <h3>共同生活成员（{{ householdMembers.length }}）</h3>
        <span class="block-hint">户主：{{ detail.head.name || '—' }}</span>
      </div>

      <div v-if="householdMembers.length > 0" class="member-list">
        <article v-for="member in householdMembers" :key="member.id" class="member">
          <div class="member__main">
            <strong>
              {{ member.name || '—' }}
              <span v-if="member.isHouseholdHead" class="chip chip--head">户主</span>
            </strong>
            <span>{{ memberMeta(member) }}</span>
          </div>
          <div class="member__side">
            <span>{{ member.healthStatusText || member.healthStatus || '—' }}</span>
            <small>{{ member.idCardMasked || '—' }}</small>
          </div>
        </article>
      </div>
      <el-empty v-else :image-size="52" description="暂无共同生活成员" />

      <details v-if="supportMembers.length > 0" class="support-details">
        <summary>赡养抚养扶养人（{{ supportMembers.length }}）</summary>
        <div class="member-list">
          <article v-for="member in supportMembers" :key="member.id" class="member">
            <div class="member__main">
              <strong>{{ member.name || '—' }}</strong>
              <span>{{ memberMeta(member) }}</span>
            </div>
            <div class="member__side">
              <span>赡养抚养扶养</span>
              <small>{{ member.idCardMasked || '—' }}</small>
            </div>
          </article>
        </div>
      </details>
    </section>

    <section class="block">
      <div class="block-head">
        <h3>办理入口</h3>
        <span class="block-hint">按旧版显示条件；写操作服务端再次校验 CHANGE_MANAGE</span>
      </div>
      <div class="action-grid">
        <el-button
          class="action"
          type="primary"
          plain
          v-if="!detail.isStopped"
          @click="emit('economic-review')"
        >
          经济状况复核
        </el-button>
        <el-button
          v-if="detail.canChangeMember"
          class="action"
          :disabled="!detail.canManage || Boolean(busy)"
          @click="emit('member-change')"
        >
          家庭成员变更
        </el-button>
        <el-button
          v-if="detail.canChangeHead"
          class="action"
          :disabled="!detail.canManage || Boolean(busy)"
          @click="emit('head-change')"
        >
          户主变更
        </el-button>
        <el-button
          v-if="detail.canHouseholdDeath"
          class="action"
          type="danger"
          plain
          :disabled="!detail.canManage || Boolean(busy)"
          @click="emit('household-death')"
        >
          户主死亡变更
        </el-button>
        <el-button
          v-if="detail.grace"
          class="action"
          type="warning"
          plain
          :disabled="!detail.canManage || !detail.canGraceConfirm || Boolean(busy)"
          @click="emit('grace-confirm')"
        >
          渐退确认
        </el-button>
        <el-button
          v-if="detail.canVerifyStop"
          class="action"
          type="danger"
          :disabled="!detail.canManage || Boolean(busy)"
          :loading="Boolean(busy)"
          @click="emit('verify-stop')"
        >
          核验停止
        </el-button>
      </div>
      <p v-if="detail.familySize <= 1" class="action-note">
        单人户唯一成员即户主：家庭成员变更与户主变更不可用；户主死亡按「整户停止」办理。
      </p>
    </section>

    <section class="block">
      <div class="block-head">
        <h3>变更历史</h3>
        <span class="block-hint">最近 200 条，按变更时间倒序</span>
      </div>

      <div v-if="historyLoading" class="history-loading">
        <el-skeleton :rows="3" animated />
      </div>
      <div v-else-if="history.length > 0" class="history-list">
        <article v-for="item in history" :key="item.id" class="history-item">
          <div class="history-item__dot" aria-hidden="true"></div>
          <div class="history-item__main">
            <strong>{{ item.changeTypeDisplay || item.changeType }}</strong>
            <p>{{ item.changeReason || '—' }}</p>
          </div>
          <div class="history-item__side">
            <span>{{ formatDate(item.changeDate) }}</span>
            <small>{{ item.operatorName || '—' }}</small>
            <small>{{ formatDateTime(item.changedAt) }}</small>
          </div>
        </article>
      </div>
      <el-empty v-else :image-size="52" description="暂无变更历史" />
    </section>
  </div>
</template>

<style scoped>
.detail-panel {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.detail-head__main {
  min-width: 0;
}

.detail-head__title-line {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.detail-head__title-line h2 {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.03em;
  color: var(--nc-ink);
}

.detail-head__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 14px;
  margin-top: 6px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.close-glyph {
  font-size: 20px;
  line-height: 1;
}

.permission-note {
  padding: 9px 11px;
  border: 1px solid rgba(166, 106, 0, 0.32);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.chip {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.chip--classification {
  border-color: rgba(33, 89, 138, 0.28);
  background-color: rgba(33, 89, 138, 0.08);
  color: rgb(33, 89, 138);
}

.chip--active {
  border-color: rgba(46, 71, 56, 0.32);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.chip--stopped {
  border-color: rgba(171, 50, 39, 0.32);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.chip--head {
  margin-left: 4px;
  border-color: rgba(166, 106, 0, 0.3);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber);
  font-size: 10px;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.stat {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-left: 2px solid var(--nc-line-strong);
  background-color: var(--nc-paper-soft);
}

.stat > span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.06em;
}

.stat strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.stat strong.stat__text {
  font-size: var(--nc-text-sm);
  font-family: var(--nc-font-sans);
}

.stat small {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 10px;
  line-height: 1.45;
  text-overflow: ellipsis;
}

.address-line {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.55;
}

.block {
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.block-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 6px 10px;
  margin-bottom: 9px;
}

.block-head h3 {
  display: flex;
  align-items: center;
  gap: 7px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.block-head h3::before {
  content: '';
  width: 2px;
  height: 12px;
  background-color: var(--nc-seal);
}

.block-hint {
  color: var(--nc-muted);
  font-size: 10px;
}

.monthly-flags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.flag {
  display: inline-flex;
  padding: 1px 8px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.flag.is-done {
  border-color: rgba(166, 106, 0, 0.32);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber);
  font-weight: 600;
}

.grace-block {
  padding: 11px 12px;
  border: 1px solid rgba(166, 106, 0, 0.28);
  background-color: rgba(166, 106, 0, 0.05);
}

.grace-block .block-head {
  margin-bottom: 8px;
}

.grace-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 12px;
}

.grace-grid div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.grace-grid span {
  color: var(--nc-muted);
  font-size: 10px;
}

.grace-grid strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
}

.member-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.member {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.member__main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.member__main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.member__main span,
.member__side span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.member__side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  flex: none;
  text-align: right;
}

.member__side small {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.support-details {
  margin-top: 8px;
}

.support-details summary {
  margin-bottom: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  cursor: pointer;
}

.action-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 7px;
}

.action {
  width: 100%;
  margin-left: 0 !important;
}

.action-note {
  margin-top: 7px;
  color: var(--nc-muted);
  font-size: 10px;
  line-height: 1.5;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 7px;
  max-height: 260px;
  overflow-y: auto;
  padding-right: 2px;
}

.history-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 9px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.history-item__dot {
  flex: none;
  width: 6px;
  height: 6px;
  margin-top: 5px;
  border-radius: 50%;
  background-color: var(--nc-seal);
}

.history-item__main {
  min-width: 0;
  flex: 1;
}

.history-item__main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.history-item__main p {
  margin-top: 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
  word-break: break-all;
}

.history-item__side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 1px;
  flex: none;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.history-loading {
  padding: 6px 0;
}

@media (max-width: 600px) {
  .stat-grid {
    grid-template-columns: 1fr;
  }

  .action-grid {
    grid-template-columns: 1fr;
  }

  .grace-grid {
    grid-template-columns: 1fr;
  }
}
</style>
