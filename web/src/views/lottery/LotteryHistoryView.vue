<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowLeft, Refresh } from '@element-plus/icons-vue'
import {
  describeLotteryError,
  getDrawList,
} from '@/api/lottery'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { LotteryDraw, LotteryType } from '@/types/lottery'
import { LOTTERY_TYPE_OPTIONS, lotteryTypeLabel } from '@/types/lottery'
import {
  formatAmount,
  formatDateOnly,
  formatMoney,
  formatNumberList,
} from './display'

const PAGE_SIZE = 30

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 860px)')

const lotteryType = ref<LotteryType>('SSQ')
const page = ref(1)
const draws = ref<LotteryDraw[]>([])
const total = ref(0)
const loading = ref(false)
const loadError = ref('')

const detailVisible = ref(false)
const detailDraw = ref<LotteryDraw | null>(null)

let loadTicket = 0

const currentLabel = computed(() => lotteryTypeLabel(lotteryType.value))
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const hasPrevious = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)
const isPermissionDenied = computed(() => loadError.value.includes('LOTTERY_ACCESS'))

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getDrawList(lotteryType.value, page.value, PAGE_SIZE)
    if (ticket !== loadTicket) return
    draws.value = response.items
    total.value = response.total

    // 兼容删除数据后页码越界：回落到最后一页重新取数。
    if (draws.value.length === 0 && page.value > 1 && total.value > 0) {
      page.value = Math.max(1, Math.ceil(total.value / PAGE_SIZE))
      if (ticket === loadTicket) void load()
    }
  } catch (error) {
    if (ticket !== loadTicket) return
    draws.value = []
    total.value = 0
    loadError.value = describeLotteryError(error, '开奖记录加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
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

function openDetail(draw: LotteryDraw): void {
  detailDraw.value = draw
  detailVisible.value = true
}

watch(lotteryType, () => {
  page.value = 1
  draws.value = []
  total.value = 0
  void load()
})

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page history-page">
    <NcPageHeader
      :title="`历史开奖 · ${currentLabel}`"
      description="往期开奖记录查询与数据浏览，每页 30 期"
      kicker="彩票助手 · 历史记录"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/lottery')">返回彩票助手</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="filter-card">
      <div class="filter-body">
        <div class="filter-main">
          <el-radio-group v-model="lotteryType" :disabled="loading">
            <el-radio-button
              v-for="option in LOTTERY_TYPE_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </el-radio-button>
          </el-radio-group>

          <div class="pager">
            <el-button size="small" :disabled="!hasPrevious || loading" @click="goPrevious">
              上一页
            </el-button>
            <span class="pager-text">第 {{ page }} 页 / 共 {{ totalPages }} 页</span>
            <el-button size="small" :disabled="!hasNext || loading" @click="goNext">
              下一页
            </el-button>
          </div>
        </div>

        <p class="filter-summary">共 {{ total.toLocaleString('zh-CN') }} 期开奖数据</p>
      </div>
    </NcCard>

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

    <NcCard padding="none" class="list-card">
      <div v-loading="loading" class="list-body">
        <el-skeleton v-if="loading && draws.length === 0" :rows="6" animated />

        <template v-else-if="draws.length > 0">
          <el-table
            v-if="!isNarrow"
            :data="draws"
            row-key="drawNumber"
            class="draw-table"
            @row-click="openDetail"
          >
            <el-table-column label="期号" width="104">
              <template #default="{ row }">
                <strong class="period">{{ row.drawNumber }}</strong>
              </template>
            </el-table-column>
            <el-table-column label="日期" width="112">
              <template #default="{ row }">
                <span class="date-text">{{ formatDateOnly(row.drawDate) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="红球" min-width="212">
              <template #default="{ row }">
                <div class="ball-row">
                  <span
                    v-for="number in row.redNumbers"
                    :key="`red-${row.drawNumber}-${number}`"
                    class="ball ball--red ball--sm"
                  >
                    {{ pad(number) }}
                  </span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="蓝球" width="112">
              <template #default="{ row }">
                <div class="ball-row">
                  <span
                    v-for="number in row.blueNumbers"
                    :key="`blue-${row.drawNumber}-${number}`"
                    class="ball ball--blue ball--sm"
                  >
                    {{ pad(number) }}
                  </span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="销售额" width="132" align="right">
              <template #default="{ row }">
                <span class="amount">{{ formatAmount(row.salesAmount) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="奖池" width="142" align="right">
              <template #default="{ row }">
                <span class="amount">{{ formatAmount(row.poolMoney) }}</span>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="draw-cards">
            <article
              v-for="draw in draws"
              :key="draw.drawNumber"
              class="draw-card"
              role="button"
              tabindex="0"
              @click="openDetail(draw)"
              @keydown.enter="openDetail(draw)"
            >
              <header class="draw-card-head">
                <div>
                  <strong class="period">{{ draw.drawNumber }}</strong>
                  <span class="date-text">{{ formatDateOnly(draw.drawDate) }}</span>
                </div>
                <span class="draw-card-hint">详情 →</span>
              </header>

              <div class="ball-row">
                <span
                  v-for="number in draw.redNumbers"
                  :key="`m-red-${draw.drawNumber}-${number}`"
                  class="ball ball--red ball--sm"
                >
                  {{ pad(number) }}
                </span>
                <span
                  v-for="number in draw.blueNumbers"
                  :key="`m-blue-${draw.drawNumber}-${number}`"
                  class="ball ball--blue ball--sm"
                >
                  {{ pad(number) }}
                </span>
              </div>

              <dl class="draw-card-meta">
                <div>
                  <dt>销售额</dt>
                  <dd>{{ formatAmount(draw.salesAmount) }}</dd>
                </div>
                <div>
                  <dt>奖池</dt>
                  <dd>{{ formatAmount(draw.poolMoney) }}</dd>
                </div>
              </dl>
            </article>
          </div>
        </template>

        <el-empty
          v-else-if="!loading && !loadError"
          description="暂无开奖记录，请先在彩票助手同步历史数据"
          :image-size="96"
        />
      </div>

      <template v-if="draws.length > 0" #footer>
        <p class="list-note">
          点击任一期查看开奖详情；数据来源为已同步的官方开奖记录。
        </p>
      </template>
    </NcCard>

    <el-dialog v-model="detailVisible" title="开奖详情" width="min(92vw, 480px)">
      <template v-if="detailDraw">
        <div class="detail-head">
          <div>
            <p class="detail-eyebrow">{{ currentLabel }} · 第 {{ detailDraw.drawNumber }} 期</p>
            <strong class="detail-date">{{ formatDateOnly(detailDraw.drawDate) }}</strong>
          </div>
          <span class="nc-stamp">开奖记录</span>
        </div>

        <div class="number-row">
          <span class="number-label">红球</span>
          <div class="ball-row">
            <span
              v-for="number in detailDraw.redNumbers"
              :key="`d-red-${number}`"
              class="ball ball--red"
            >
              {{ pad(number) }}
            </span>
            <span v-if="detailDraw.redNumbers.length === 0" class="number-empty">—</span>
          </div>
        </div>

        <div class="number-row">
          <span class="number-label">蓝球</span>
          <div class="ball-row">
            <span
              v-for="number in detailDraw.blueNumbers"
              :key="`d-blue-${number}`"
              class="ball ball--blue"
            >
              {{ pad(number) }}
            </span>
            <span v-if="detailDraw.blueNumbers.length === 0" class="number-empty">—</span>
          </div>
        </div>

        <dl class="detail-meta">
          <div>
            <dt>销售额</dt>
            <dd>{{ formatMoney(detailDraw.salesAmount) }}</dd>
          </div>
          <div>
            <dt>奖池</dt>
            <dd>{{ formatMoney(detailDraw.poolMoney) }}</dd>
          </div>
          <div class="detail-meta-wide">
            <dt>号码文本</dt>
            <dd class="detail-numbers">
              {{ formatNumberList(detailDraw.redNumbers) }} +
              {{ formatNumberList(detailDraw.blueNumbers) }}
            </dd>
          </div>
        </dl>

        <p v-if="detailDraw.prizeDescription" class="detail-prize">
          {{ detailDraw.prizeDescription }}
        </p>
      </template>

      <template #footer>
        <el-button type="primary" @click="detailVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.history-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.filter-card {
  overflow: hidden;
}

.filter-body {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px 16px;
  padding: 14px 16px;
}

.filter-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
}

.pager {
  display: flex;
  align-items: center;
  gap: 10px;
}

.pager-text {
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.filter-summary {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.load-error {
  margin: 0;
}

.list-card {
  overflow: hidden;
}

.list-body {
  padding: 0 16px 16px;
}

.draw-table {
  width: 100%;
  cursor: pointer;
}

.draw-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
}

.draw-table :deep(.el-table__row) {
  cursor: pointer;
}

.draw-table :deep(.el-table__row:hover > td) {
  background-color: var(--nc-paper-soft);
}

.period {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-base);
}

.date-text {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.amount {
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.ball-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.ball {
  display: inline-grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border: 1px solid transparent;
  border-radius: 50%;
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-base);
  font-weight: 700;
  line-height: 1;
}

.ball--sm {
  width: 26px;
  height: 26px;
  font-size: var(--nc-text-xs);
}

.ball--red {
  border-color: #b71c1c;
  background-color: #e53935;
}

.ball--blue {
  border-color: #1565c0;
  background-color: #1e88e5;
}

.list-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

/* ── 窄屏卡片 ─────────────────────────────────────────────────── */

.draw-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.draw-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  cursor: pointer;
}

.draw-card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.draw-card-head > div {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.draw-card-hint {
  color: var(--nc-seal);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.draw-card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin: 0;
}

.draw-card-meta dt,
.detail-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.draw-card-meta dd,
.detail-meta dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

/* ── 详情弹窗 ─────────────────────────────────────────────────── */

.detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.detail-eyebrow {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.detail-date {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.number-row {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.number-row + .number-row {
  margin-top: 10px;
}

.number-label {
  flex: none;
  width: 34px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.number-empty {
  color: var(--nc-muted);
}

.detail-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin: 18px 0 0;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.detail-meta-wide {
  grid-column: span 2;
}

.detail-numbers {
  overflow-wrap: anywhere;
}

.detail-prize {
  margin-top: 14px;
  padding: 10px 12px;
  border-left: 2px solid var(--nc-seal);
  background-color: var(--nc-seal-wash);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.75;
  white-space: pre-line;
  overflow-wrap: anywhere;
}

@media (max-width: 767px) {
  .filter-main {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .pager {
    justify-content: space-between;
  }

  .pager-text {
    text-align: center;
  }

  .draw-card-meta {
    gap: 10px;
  }
}
</style>
