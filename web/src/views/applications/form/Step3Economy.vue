<script setup lang="ts">
import { computed } from 'vue'
import { InfoFilled } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcStat from '@/components/NcStat.vue'
import { formatMoney } from '../format'
import EconomicLedgerTable from './EconomicLedgerTable.vue'
import {
  LEDGER_GROUPS,
  createLedgerRow,
  type EditableLedgerKey,
  type LedgerConfig,
  type LedgerGroup,
} from './economy'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const economy = computed(() => props.form.economy)
const members = computed(() => props.form.members)
const groups = LEDGER_GROUPS

/** 兼容后端未回填或返回 null 的明细数组；首次访问时补成空数组。 */
function ledgerRows(key: EditableLedgerKey): Record<string, unknown>[] {
  const details = props.form.economicDetails
  const current = details[key]
  if (Array.isArray(current)) return current

  const rows: Record<string, unknown>[] = []
  details[key] = rows
  return rows
}

function addLedgerRow(ledger: LedgerConfig): void {
  ledgerRows(ledger.key).push(createLedgerRow(ledger))
}

function removeLedgerRow(ledger: LedgerConfig, row: Record<string, unknown>): void {
  const rows = ledgerRows(ledger.key)
  const index = rows.indexOf(row)
  if (index >= 0) rows.splice(index, 1)
}

function groupCount(group: LedgerGroup): number {
  return group.ledgers.reduce((sum, ledger) => sum + ledgerRows(ledger.key).length, 0)
}

const detailCount = computed(() =>
  groups.reduce((sum, group) => sum + groupCount(group), 0),
)

const landConfirmationCount = computed(() => {
  const rows = props.form.economicDetails.landConfirmationGroups
  return Array.isArray(rows) ? rows.length : 0
})

function groupTone(group: LedgerGroup): 'income' | 'expense' | 'asset' {
  if (group.key === 'expense') return 'expense'
  if (group.key === 'assets') return 'asset'
  return 'income'
}
</script>

<template>
  <div class="step-economy">
    <div class="ledger-intro">
      <div class="ledger-intro__main">
        <strong>按明细台账录入经济状况</strong>
        <p>
          各收入、支出与财产分项逐条登记；成员类台账可直接从第 2 步家庭成员中选择。
          合计与人均不在此填写，保存后由服务端按 Core 口径重算。
        </p>
      </div>
      <div class="ledger-intro__meta">
        <span>已录明细</span>
        <strong>{{ detailCount }}</strong>
        <span>条</span>
      </div>
    </div>

    <NcCard
      v-for="group in groups"
      :key="group.key"
      :title="group.title"
      :subtitle="group.subtitle"
    >
      <template #actions>
        <span class="section-meta">{{ groupCount(group) }} 条明细</span>
      </template>

      <div class="ledger-stack">
        <EconomicLedgerTable
          v-for="ledger in group.ledgers"
          :key="ledger.key"
          :title="ledger.title"
          :hint="ledger.hint"
          :rows="ledgerRows(ledger.key)"
          :columns="ledger.columns"
          :members="members"
          :busy="busy"
          :default-open="ledger.defaultOpen"
          :add-label="ledger.addLabel ?? '新增一行'"
          :tone="groupTone(group)"
          @add="addLedgerRow(ledger)"
          @remove="removeLedgerRow(ledger, $event)"
        />

        <p v-if="group.key === 'assets' && landConfirmationCount > 0" class="aside-note">
          已载入 {{ landConfirmationCount }} 组土地确权数据，暂不在本页编辑，保存时按原数据回写。
        </p>
      </div>
    </NcCard>

    <NcCard
      title="收入核算"
      subtitle="合计与人均为只读值。保存后由服务端按 Core 口径重算，明细优先。"
    >
      <template #actions>
        <span class="section-meta">只读 · 服务端回写</span>
      </template>

      <div class="summary-grid">
        <NcStat
          label="年收入合计"
          :value="formatMoney(economy.totalAnnualIncome)"
          hint="元 / 年"
          tone="ink"
          compact
        />
        <NcStat
          label="月均收入"
          :value="formatMoney(economy.totalFamilyIncome)"
          hint="元 / 月"
          tone="ink"
          compact
        />
        <NcStat
          label="年人均收入"
          :value="formatMoney(economy.perCapitaAnnualIncome)"
          hint="元 / 年"
          tone="amber"
          compact
        />
        <NcStat
          label="月人均收入"
          :value="formatMoney(economy.perCapitaIncome)"
          hint="元 / 月"
          tone="seal"
          compact
        />
      </div>

      <p class="recalc-note">
        <el-icon><InfoFilled /></el-icon>
        <span>保存后由服务端按 Core 口径重算，前端不重写收入公式。</span>
      </p>
    </NcCard>
  </div>
</template>

<style scoped>
.step-economy {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.section-meta {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
  white-space: nowrap;
}

.ledger-intro {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 13px 16px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-seal);
  background-color: var(--nc-paper-2);
}

.ledger-intro__main {
  min-width: 0;
}

.ledger-intro__main strong {
  display: block;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
}

.ledger-intro__main p {
  margin: 4px 0 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.ledger-intro__meta {
  display: flex;
  flex: none;
  align-items: baseline;
  gap: 5px;
  padding-left: 16px;
  border-left: 1px solid var(--nc-line-faint);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.06em;
}

.ledger-intro__meta strong {
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0;
}

.ledger-stack {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.aside-note {
  margin: 0;
  padding: 9px 12px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.recalc-note {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin: 12px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.recalc-note .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

@media (max-width: 1099px) {
  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .ledger-intro {
    align-items: flex-start;
    flex-direction: column;
    gap: 10px;
    padding: 12px 13px;
  }

  .ledger-intro__meta {
    padding-left: 0;
    border-left: 0;
  }

  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .section-meta {
    font-size: 10px;
    letter-spacing: 0.06em;
  }
}

@media (max-width: 479px) {
  .summary-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
