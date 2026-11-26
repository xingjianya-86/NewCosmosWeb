<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Refresh, Search, Tickets } from '@element-plus/icons-vue'
import {
  describeLotteryError,
  getPurchases,
  getPurchaseSummary,
  verifyPurchases,
} from '@/api/lottery'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  LotteryPurchaseFilter,
  LotteryPurchaseRecord,
  LotteryPurchaseSummary,
  LotteryType,
  LotteryVerification,
} from '@/types/lottery'
import {
  LOTTERY_PURCHASE_FILTERS,
  LOTTERY_TYPE_OPTIONS,
  lotteryPrizeLevelLabel,
  lotteryTypeLabel,
} from '@/types/lottery'
import {
  buildNumberTokens,
  formatDateOnly,
  formatDateTime,
  formatMoney,
  formatNumberList,
  verificationLines,
} from './display'

const PAGE_SIZE = 30

interface PurchaseRow extends LotteryPurchaseRecord {
  tokens: ReturnType<typeof buildNumberTokens>
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 900px)')

const lotteryType = ref<LotteryType>('SSQ')
const filter = ref<LotteryPurchaseFilter>('All')
const page = ref(1)
const records = ref<LotteryPurchaseRecord[]>([])
const total = ref(0)
const summary = ref<LotteryPurchaseSummary>({
  totalCount: 0,
  hitCount: 0,
  pendingCount: 0,
  totalPrizeAmount: 0,
  bestPrizeLevel: 0,
  bestPrizeDescription: '',
})
const loading = ref(false)
const verifying = ref(false)
const loadError = ref('')
const verification = ref<LotteryVerification | null>(null)

const detailVisible = ref(false)
const detailRecord = ref<LotteryPurchaseRecord | null>(null)

let loadTicket = 0

const currentLabel = computed(() => lotteryTypeLabel(lotteryType.value))
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const hasPrevious = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)
const isPermissionDenied = computed(() => loadError.value.includes('LOTTERY_ACCESS'))

const rows = computed<PurchaseRow[]>(() =>
  records.value.map((record) => ({
    ...record,
    tokens: buildNumberTokens(
      record.redNumbers,
      record.blueNumbers,
      record.actualRedNumbers,
      record.actualBlueNumbers,
      record.isVerified,
    ),
  })),
)

const detailTokens = computed(() => {
  const record = detailRecord.value
  if (!record) return []
  return buildNumberTokens(
    record.redNumbers,
    record.blueNumbers,
    record.actualRedNumbers,
    record.actualBlueNumbers,
    record.isVerified,
  )
})

const emptySummary = (): LotteryPurchaseSummary => ({
  totalCount: 0,
  hitCount: 0,
  pendingCount: 0,
  totalPrizeAmount: 0,
  bestPrizeLevel: 0,
  bestPrizeDescription: '',
})

function statusTagType(record: LotteryPurchaseRecord): 'success' | 'warning' | 'info' {
  if (!record.isVerified) return 'warning'
  return record.isHit ? 'success' : 'info'
}

function hitText(record: LotteryPurchaseRecord): string {
  return record.isVerified ? `红 ${record.hitRedCount} · 蓝 ${record.hitBlueCount}` : '—'
}

function prizeText(record: LotteryPurchaseRecord): string {
  if (!record.isHit) return '—'
  return record.prizeLevelText || lotteryPrizeLevelLabel(record.prizeLevel) || '已中奖'
}

function amountText(record: LotteryPurchaseRecord): string {
  if (!record.isHit) return '—'
  return record.prizeAmount > 0 ? formatMoney(record.prizeAmount) : '以官方为准'
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''

  const [summaryResult, listResult] = await Promise.allSettled([
    getPurchaseSummary(lotteryType.value),
    getPurchases(lotteryType.value, filter.value, page.value, PAGE_SIZE),
  ])

  if (ticket !== loadTicket) return

  const errors: string[] = []

  if (summaryResult.status === 'fulfilled') {
    summary.value = summaryResult.value
  } else {
    summary.value = emptySummary()
    errors.push(describeLotteryError(summaryResult.reason, '购彩汇总加载失败'))
  }

  if (listResult.status === 'fulfilled') {
    records.value = listResult.value.items
    total.value = listResult.value.total

    if (records.value.length === 0 && page.value > 1 && total.value > 0) {
      page.value = Math.max(1, Math.ceil(total.value / PAGE_SIZE))
      if (ticket === loadTicket) void load()
    }
  } else {
    records.value = []
    total.value = 0
    errors.push(describeLotteryError(listResult.reason, '购彩记录加载失败'))
  }

  loadError.value = errors[0] ?? ''
  loading.value = false
}

function goPrevious(): void {
  if (!hasPrevious.value || loading.value) return
  page.value -= 1
  void load()
}

function goNext(): void {
  if (!hasNext.value || loading.value) return
  page.value += 1
  void load()
}

function openDetail(record: LotteryPurchaseRecord): void {
  detailRecord.value = record
  detailVisible.value = true
}

async function handleVerify(): Promise<void> {
  if (verifying.value) return
  verifying.value = true
  try {
    const result = await verifyPurchases(lotteryType.value)
    verification.value = result
    await load()

    const lines = verificationLines(result)
    await ElMessageBox.alert(
      h('div', { style: 'white-space: pre-line; line-height: 1.8' }, lines.join('\n')),
      '验证完成',
      { confirmButtonText: '知道了' },
    )
  } catch (error) {
    ElMessage.error(describeLotteryError(error, '购彩记录验证失败'))
  } finally {
    verifying.value = false
  }
}

watch(lotteryType, () => {
  page.value = 1
  records.value = []
  total.value = 0
  verification.value = null
  void load()
})

watch(filter, () => {
  page.value = 1
  records.value = []
  total.value = 0
  void load()
})

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page result-page">
    <NcPageHeader
      title="中奖结果"
      description="我的购彩记录 · 命中、奖级与奖金明细"
      kicker="彩票助手 · 中奖结果"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/lottery')">返回彩票助手</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="stats-grid">
      <NcStat
        label="累计注数"
        :value="summary.totalCount.toLocaleString('zh-CN')"
        tone="ink"
        compact
        :class="{ 'is-loading': loading }"
      />
      <NcStat
        label="已中奖"
        :value="summary.hitCount.toLocaleString('zh-CN')"
        tone="seal"
        compact
        :class="{ 'is-loading': loading }"
      />
      <NcStat
        label="中奖金额"
        :value="formatMoney(summary.totalPrizeAmount)"
        tone="amber"
        compact
        :class="{ 'is-loading': loading }"
      />
      <NcStat
        label="待开奖"
        :value="summary.pendingCount.toLocaleString('zh-CN')"
        tone="pine"
        compact
        :class="{ 'is-loading': loading }"
      />
    </div>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <template v-if="isPermissionDenied">
        <span>请联系管理员分配 LOTTERY_ACCESS 权限后重新进入。</span>
      </template>
      <template v-else>
        <el-button link type="primary" @click="load()">重新加载</el-button>
      </template>
    </el-alert>

    <NcCard padding="none" class="filter-card">
      <div class="filter-body">
        <div class="filter-row">
          <div class="filter-group">
            <span class="filter-label">彩种</span>
            <el-radio-group v-model="lotteryType" :disabled="loading">
              <el-radio-button
                v-for="option in LOTTERY_TYPE_OPTIONS"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </el-radio-button>
            </el-radio-group>
          </div>

          <div class="filter-group">
            <span class="filter-label">状态</span>
            <el-radio-group v-model="filter" :disabled="loading">
              <el-radio-button
                v-for="option in LOTTERY_PURCHASE_FILTERS"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </el-radio-button>
            </el-radio-group>
          </div>

          <el-button
            type="primary"
            :icon="Search"
            :loading="verifying"
            :disabled="loading"
            @click="handleVerify"
          >
            立即验证
          </el-button>
        </div>

        <div class="filter-foot">
          <span class="filter-summary">
            共 {{ total.toLocaleString('zh-CN') }} 注 · 第 {{ page }} 页 / 共
            {{ totalPages }} 页
          </span>

          <div class="pager">
            <el-button size="small" :disabled="!hasPrevious || loading" @click="goPrevious">
              上一页
            </el-button>
            <el-button size="small" :disabled="!hasNext || loading" @click="goNext">
              下一页
            </el-button>
          </div>
        </div>
      </div>
    </NcCard>

    <NcCard
      v-if="verification"
      title="本次验证摘要"
      :subtitle="`${currentLabel} · 已对照最新开奖记录`"
      class="verification-card"
    >
      <div class="verification-grid">
        <div>
          <span class="verification-label">验证注数</span>
          <strong>{{ verification.totalVerified }}</strong>
        </div>
        <div>
          <span class="verification-label">命中的注数</span>
          <strong>{{ verification.hitCount }}</strong>
        </div>
        <div>
          <span class="verification-label">中奖金额</span>
          <strong>{{ formatMoney(verification.totalPrizeAmount) }}</strong>
        </div>
        <div>
          <span class="verification-label">最佳奖级</span>
          <strong>
            {{
              verification.bestPrizeDescription ||
              lotteryPrizeLevelLabel(verification.bestPrizeLevel) ||
              '—'
            }}
          </strong>
        </div>
      </div>
      <p v-if="verification.pendingCount > 0" class="verification-pending">
        另有 {{ verification.pendingCount }} 注待开奖，待对应开奖数据同步后即可验证。
      </p>
      <p v-if="verification.encouragementMessage" class="verification-encourage">
        {{ verification.encouragementMessage }}
      </p>
    </NcCard>

    <NcCard padding="none" class="list-card">
      <div v-loading="loading" class="list-body">
        <el-skeleton v-if="loading && records.length === 0" :rows="6" animated />

        <template v-else-if="rows.length > 0">
          <el-table
            v-if="!isNarrow"
            :data="rows"
            row-key="id"
            class="purchase-table"
            @row-click="openDetail"
          >
            <el-table-column label="号码" min-width="260">
              <template #default="{ row }">
                <div class="ball-row">
                  <span
                    v-for="(token, index) in row.tokens"
                    :key="`${row.id}-${index}`"
                    class="ball"
                    :class="[
                      token.isBlue ? 'ball--blue' : 'ball--red',
                      token.isHit ? 'is-hit' : 'is-miss',
                    ]"
                  >
                    {{ token.text }}
                  </span>
                  <span v-if="row.tokens.length === 0" class="number-empty">—</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="96">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTagType(row as PurchaseRow)" effect="plain">
                  {{ row.statusText }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column label="算法" min-width="128">
              <template #default="{ row }">
                <span class="algorithm">{{ row.algorithm || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="购彩日期" width="112">
              <template #default="{ row }">
                <span class="date-text">{{ formatDateOnly(row.purchaseDate) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="期号" width="104">
              <template #default="{ row }">
                <span class="period">{{ row.drawNumber || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="命中" width="104">
              <template #default="{ row }">
                <span class="hit-text">{{ hitText(row as PurchaseRow) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="奖级" min-width="106">
              <template #default="{ row }">
                <span :class="row.isHit ? 'prize-text' : 'muted-text'">
                  {{ prizeText(row as PurchaseRow) }}
                </span>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="purchase-cards">
            <article
              v-for="row in rows"
              :key="row.id"
              class="purchase-card"
              role="button"
              tabindex="0"
              @click="openDetail(row)"
              @keydown.enter="openDetail(row)"
            >
              <header class="purchase-card-head">
                <div class="ball-row">
                  <span
                    v-for="(token, index) in row.tokens"
                    :key="`m-${row.id}-${index}`"
                    class="ball"
                    :class="[
                      token.isBlue ? 'ball--blue' : 'ball--red',
                      token.isHit ? 'is-hit' : 'is-miss',
                    ]"
                  >
                    {{ token.text }}
                  </span>
                  <span v-if="row.tokens.length === 0" class="number-empty">—</span>
                </div>
                <el-tag size="small" :type="statusTagType(row)" effect="plain">
                  {{ row.statusText }}
                </el-tag>
              </header>

              <dl class="purchase-card-meta">
                <div>
                  <dt>算法</dt>
                  <dd>{{ row.algorithm || '—' }}</dd>
                </div>
                <div>
                  <dt>购彩日期</dt>
                  <dd>{{ formatDateOnly(row.purchaseDate) }}</dd>
                </div>
                <div>
                  <dt>期号</dt>
                  <dd>{{ row.drawNumber || '—' }}</dd>
                </div>
                <div>
                  <dt>命中</dt>
                  <dd>{{ hitText(row) }}</dd>
                </div>
                <div>
                  <dt>奖级</dt>
                  <dd :class="row.isHit ? 'prize-text' : 'muted-text'">{{ prizeText(row) }}</dd>
                </div>
              </dl>

              <span class="purchase-card-hint">查看单注详情 →</span>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty description="暂无购彩记录" :image-size="96" />
          <p class="empty-hint">生成预测或机选后开奖即可自动验证</p>
        </div>
      </div>

      <template v-if="rows.length > 0" #footer>
        <p class="list-note">
          实心红 / 蓝球表示命中开奖号码；浅色描边球表示未命中。
        </p>
      </template>
    </NcCard>

    <el-dialog v-model="detailVisible" title="购彩详情" width="min(92vw, 520px)">
      <template v-if="detailRecord">
        <div class="detail-head">
          <div>
            <p class="detail-eyebrow">
              {{ detailRecord.lotteryType ? lotteryTypeLabel(detailRecord.lotteryType) : currentLabel }} ·
              {{ detailRecord.statusText }}
            </p>
            <strong class="detail-period">
              {{ detailRecord.drawNumber ? `第 ${detailRecord.drawNumber} 期` : '尚未对应开奖期号' }}
            </strong>
          </div>
          <el-tag size="small" :type="statusTagType(detailRecord)" effect="plain">
            {{ detailRecord.statusText }}
          </el-tag>
        </div>

        <div class="detail-section">
          <span class="detail-section-label">我的号码</span>
          <div class="ball-row">
            <span
              v-for="(token, index) in detailTokens"
              :key="`detail-${index}`"
              class="ball"
              :class="[
                token.isBlue ? 'ball--blue' : 'ball--red',
                token.isHit ? 'is-hit' : 'is-miss',
              ]"
            >
              {{ token.text }}
            </span>
            <span v-if="detailTokens.length === 0" class="number-empty">—</span>
          </div>
        </div>

        <dl class="detail-meta">
          <div>
            <dt>算法</dt>
            <dd>{{ detailRecord.algorithm || '—' }}</dd>
          </div>
          <div>
            <dt>购彩日期</dt>
            <dd>{{ formatDateTime(detailRecord.purchaseDate) }}</dd>
          </div>
          <div>
            <dt>开奖期号</dt>
            <dd>{{ detailRecord.drawNumber || '—' }}</dd>
          </div>
          <div>
            <dt>命中红球 / 蓝球</dt>
            <dd>{{ hitText(detailRecord) }}</dd>
          </div>
          <div class="detail-meta-wide">
            <dt>开奖号码</dt>
            <dd class="detail-numbers">
              {{
                detailRecord.actualRedNumbers.length || detailRecord.actualBlueNumbers.length
                  ? `${formatNumberList(detailRecord.actualRedNumbers)} + ${formatNumberList(
                      detailRecord.actualBlueNumbers,
                    )}`
                  : '尚未取得开奖号码'
              }}
            </dd>
          </div>
          <div>
            <dt>奖级</dt>
            <dd>{{ prizeText(detailRecord) }}</dd>
          </div>
          <div>
            <dt>奖金</dt>
            <dd>{{ amountText(detailRecord) }}</dd>
          </div>
        </dl>

        <p v-if="detailRecord.isHit" class="detail-prize-note">
          <el-icon :size="14"><Tickets /></el-icon>
          已中奖记录将计入页头上方的「已中奖」与「中奖金额」统计。
        </p>
      </template>

      <template #footer>
        <el-button type="primary" @click="detailVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.result-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.stats-grid :deep(.nc-stat.is-loading .nc-stat__value) {
  opacity: 0.55;
}

.load-error {
  margin: 0;
}

.filter-card {
  overflow: hidden;
}

.filter-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px 16px;
}

.filter-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 16px;
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 10px;
}

.filter-label {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.filter-foot {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.filter-summary {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.pager {
  display: flex;
  align-items: center;
  gap: 8px;
}

.verification-card {
  border-color: rgba(46, 71, 56, 0.32);
  background-color: #f4f8f4;
}

.verification-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.verification-grid > div {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.verification-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.verification-grid strong {
  color: var(--nc-pine-dark);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  overflow-wrap: anywhere;
}

.verification-pending,
.verification-encourage {
  margin-top: 10px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.verification-encourage {
  padding-left: 10px;
  border-left: 2px solid var(--nc-pine-light);
  white-space: pre-line;
}

.list-card {
  overflow: hidden;
}

.list-body {
  padding: 0 16px 16px;
}

.purchase-table {
  width: 100%;
  cursor: pointer;
}

.purchase-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
}

.purchase-table :deep(.el-table__row) {
  cursor: pointer;
}

.purchase-table :deep(.el-table__row:hover > td) {
  background-color: var(--nc-paper-soft);
}

.ball-row {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
}

.ball {
  display: inline-grid;
  place-items: center;
  width: 27px;
  height: 27px;
  border: 1px solid transparent;
  border-radius: 50%;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 1;
}

/* 命中：实心红 / 蓝 + 白字；未命中：浅底描边 + 红 / 蓝字。 */
.ball--red.is-hit {
  border-color: #b71c1c;
  background-color: #e53935;
  color: #fff;
}

.ball--blue.is-hit {
  border-color: #1565c0;
  background-color: #1e88e5;
  color: #fff;
}

.ball--red.is-miss {
  border-color: #ffcdd2;
  background-color: #fff5f5;
  color: #e53935;
}

.ball--blue.is-miss {
  border-color: #bbdefb;
  background-color: #f2f8ff;
  color: #1e88e5;
}

.algorithm {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.date-text,
.period,
.hit-text {
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.prize-text {
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.muted-text {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.number-empty {
  color: var(--nc-muted);
}

.list-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.empty-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 30px 0 12px;
}

.empty-hint {
  margin-top: 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

/* ── 窄屏卡片 ─────────────────────────────────────────────────── */

.purchase-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.purchase-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  cursor: pointer;
}

.purchase-card-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}

.purchase-card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin: 0;
}

.purchase-card-meta dt,
.detail-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.purchase-card-meta dd,
.detail-meta dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  overflow-wrap: anywhere;
}

.purchase-card-hint {
  align-self: flex-end;
  color: var(--nc-seal);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

/* ── 详情弹窗 ─────────────────────────────────────────────────── */

.detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.detail-eyebrow {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.detail-period {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.detail-section {
  margin-bottom: 16px;
}

.detail-section-label {
  display: block;
  margin-bottom: 7px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.detail-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin: 0;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.detail-meta-wide {
  grid-column: span 2;
}

.detail-numbers {
  font-family: var(--nc-font-mono);
}

.detail-prize-note {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin-top: 14px;
  padding: 9px 12px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

@media (max-width: 1100px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .verification-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .result-page {
    gap: 12px;
  }

  .stats-grid {
    gap: 8px;
  }

  .filter-row {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .filter-group {
    flex-direction: column;
    align-items: stretch;
    gap: 6px;
  }

  .filter-foot {
    flex-direction: column;
    align-items: stretch;
  }

  .pager :deep(.el-button) {
    flex: 1 1 auto;
  }

  .verification-grid {
    grid-template-columns: 1fr;
    gap: 8px;
  }

  .detail-meta {
    grid-template-columns: 1fr;
  }

  .detail-meta-wide {
    grid-column: auto;
  }

  .purchase-card-meta {
    gap: 10px;
  }
}

@media (max-width: 420px) {
  .stats-grid {
    grid-template-columns: 1fr;
  }
}
</style>
