<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Delete, Download, InfoFilled, Plus } from '@element-plus/icons-vue'
import {
  getApplicationLandConfirmationImports,
  getApplicationSubsidyImports,
} from '@/api/applicationImports'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcStat from '@/components/NcStat.vue'
import { formatMoney } from '../format'
import EconomicLedgerTable from './EconomicLedgerTable.vue'
import {
  LAND_STATUS_DECEASED,
  LAND_STATUS_OPTIONS,
  LAND_UNIT_PRICES,
  LAND_USAGE_BATCH_LABELS,
  LAND_USAGE_OPTIONS,
  LEDGER_GROUPS,
  computeLandConfirmationTotals,
  createLandConfirmationGroup,
  createLandConfirmationPerson,
  createLandConfirmationRecord,
  createLedgerRow,
  landRecordValue,
  landStatusShouldCount,
  landUnitPrice,
  mergeLandConfirmationImports,
  mergeSubsidyImports,
  type EditableLedgerKey,
  type LandConfirmationGroupRow,
  type LandConfirmationPersonRow,
  type LandConfirmationRecordRow,
  type LedgerConfig,
  type LedgerGroup,
} from './economy'
import type { ApplicationFormModel } from './types'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const economy = computed(() => props.form.economy)
const members = computed(() => props.form.members)
const groups = LEDGER_GROUPS

const importingSubsidies = ref(false)
const importingLand = ref(false)
const subsidyOpenSignal = ref(0)

/** 兼容后端未回填或返回 null 的明细数组；首次访问时补成空数组。 */
function ledgerRows(key: EditableLedgerKey): Record<string, unknown>[] {
  const details = props.form.economicDetails
  const current = details[key]
  if (Array.isArray(current)) return current

  const rows: Record<string, unknown>[] = []
  details[key] = rows
  return rows
}

/* ------------------------------------------------------------------ *
 * 土地确权归户表
 * ------------------------------------------------------------------ */

/** 归户表数组：兼容 null/缺字段，并补齐组内 records/persons。 */
function landGroups(): LandConfirmationGroupRow[] {
  const details = props.form.economicDetails
  let rows = details.landConfirmationGroups
  if (!Array.isArray(rows)) {
    rows = []
    details.landConfirmationGroups = rows
  }

  const groups = rows as unknown as LandConfirmationGroupRow[]
  for (const group of groups) {
    if (!Array.isArray(group.records)) group.records = []
    if (!Array.isArray(group.persons)) group.persons = []
    if (typeof group.contractorName !== 'string') group.contractorName = ''
  }
  return groups
}

/** 家庭姓名集：户主 + 共同生活成员（与旧版 LandCalculation 的 familyNames 一致）。 */
const familyLandNames = computed(() =>
  [props.form.basic.applicantName, ...members.value.map((member) => member.name)]
    .map((name) => name.trim())
    .filter(Boolean),
)

const landTotals = computed(() => computeLandConfirmationTotals(landGroups(), familyLandNames.value))

const landGroupCount = computed(() =>
  Array.isArray(props.form.economicDetails.landConfirmationGroups)
    ? props.form.economicDetails.landConfirmationGroups.length
    : 0,
)

const landSummaryMetrics = computed(() => [
  { label: '总面积', value: `${formatNumber(landTotals.value.totalArea)} 亩` },
  { label: '总份数', value: `${formatNumber(landTotals.value.totalShares, 1)} 份` },
  { label: '家庭土地', value: `${formatNumber(landTotals.value.familyArea)} 亩` },
  { label: '家庭份数', value: `${formatNumber(landTotals.value.familyShares, 1)} 份` },
  { label: '家庭人数', value: `${landTotals.value.familyPersonCount} 人` },
  { label: '总人数', value: `${landTotals.value.totalPersonCount} 人` },
])

const landAreaMetrics = computed(() => [
  { label: '自种面积', value: `${formatNumber(landTotals.value.selfFarmedArea)} 亩` },
  { label: '转包面积', value: `${formatNumber(landTotals.value.subleasedArea)} 亩` },
  { label: '承包面积', value: `${formatNumber(landTotals.value.contractedArea)} 亩` },
])

function formatNumber(value: number, digits = 2): string {
  if (!Number.isFinite(value)) return '0'
  return value.toFixed(digits)
}

/** 稳定行 key：新增/删除行时避免 Vue 复用错位的输入控件。 */
let landKeySeed = 0
const landKeys = new WeakMap<object, number>()
function landKey(row: object): number {
  let key = landKeys.get(row)
  if (key === undefined) {
    landKeySeed += 1
    key = landKeySeed
    landKeys.set(row, key)
  }
  return key
}

function groupArea(group: LandConfirmationGroupRow): number {
  return group.records.reduce((sum, record) => {
    const measured = Number(record.measuredArea)
    return sum + (Number.isFinite(measured) && measured > 0 ? measured : Number(record.landArea) || 0)
  }, 0)
}

/** 旧版口径：人员面积 = 组总面积 ÷ 组有效份数 × 该人员份数。 */
function syncLandPersonAreas(): void {
  for (const group of landGroups()) {
    const area = groupArea(group)
    const shares = group.persons.reduce(
      (sum, person) =>
        sum + (landStatusShouldCount(person.landStatus) ? numberOrZero(person.sharesCount) : 0),
      0,
    )
    for (const person of group.persons) {
      person.totalLandArea =
        shares > 0 && landStatusShouldCount(person.landStatus)
          ? Math.round((area / shares) * numberOrZero(person.sharesCount) * 100) / 100
          : 0
    }
  }
}

/** 空组按旧版口径补种户主 + 共同生活成员；已有人员的组保持原样。 */
function seedLandPersons(group: LandConfirmationGroupRow): void {
  if (group.persons.length > 0) return

  const seen = new Set<string>()
  const addPerson = (name: string, idCard: string): void => {
    const trimmed = (name ?? '').trim()
    if (!trimmed) return
    const key = `${trimmed}|${(idCard ?? '').trim()}`
    if (seen.has(key)) return
    seen.add(key)
    group.persons.push({
      ...createLandConfirmationPerson(),
      name: trimmed,
      idCard: (idCard ?? '').trim(),
    })
  }

  addPerson(props.form.basic.applicantName, props.form.basic.applicantIdCard)
  for (const member of members.value) addPerson(member.name, member.idCard)
}

function addLandGroup(): void {
  const group = createLandConfirmationGroup(props.form.basic.applicantName)
  seedLandPersons(group)
  landGroups().push(group)
  syncLandPersonAreas()
}

function removeLandGroup(group: LandConfirmationGroupRow): void {
  const groups = landGroups()
  const index = groups.indexOf(group)
  if (index >= 0) groups.splice(index, 1)
  syncLandPersonAreas()
}

function addLandRecord(group: LandConfirmationGroupRow): void {
  group.records.push(createLandConfirmationRecord(group.contractorName))
  syncLandPersonAreas()
}

function removeLandRecord(group: LandConfirmationGroupRow, record: LandConfirmationRecordRow): void {
  const index = group.records.indexOf(record)
  if (index >= 0) group.records.splice(index, 1)
  syncLandPersonAreas()
}

function numberOrZero(value: number | null | undefined): number {
  return value === null || value === undefined || !Number.isFinite(value) ? 0 : value
}

function changeLandUsage(record: LandConfirmationRecordRow, usage: string): void {
  record.landUsage = usage
  record.unitPrice = landUnitPrice(usage)
}

/** 全自种 / 全转包 / 全承包：批量写用途并按默认单价重算。 */
function batchSetLandUsage(usage: string): void {
  const price = LAND_UNIT_PRICES[usage] ?? 0
  for (const group of landGroups()) {
    for (const record of group.records) {
      record.landUsage = usage
      record.unitPrice = price
    }
  }
}

function addLandPerson(group: LandConfirmationGroupRow): void {
  group.persons.push(createLandConfirmationPerson())
  syncLandPersonAreas()
}

function removeLandPerson(group: LandConfirmationGroupRow, person: LandConfirmationPersonRow): void {
  const index = group.persons.indexOf(person)
  if (index < 0) return
  group.persons.splice(index, 1)
  const name = (person.name ?? '').trim()
  if (name) {
    for (const remaining of group.persons) {
      if ((remaining.landInheritTo ?? '').trim() === name) remaining.landInheritTo = ''
    }
  }
  syncLandPersonAreas()
}

function changeLandStatus(person: LandConfirmationPersonRow, status: string): void {
  person.landStatus = status
  if (status !== LAND_STATUS_DECEASED) person.landInheritTo = ''
  syncLandPersonAreas()
}

function changeLandShares(person: LandConfirmationPersonRow, value: unknown): void {
  person.sharesCount = numberOrZero(value as number | null | undefined)
  syncLandPersonAreas()
}

function changeLandInherit(person: LandConfirmationPersonRow, value: unknown): void {
  person.landInheritTo = typeof value === 'string' ? value : ''
}

/** 死亡继承的继承人候选：本组其他人员姓名（去重、去空）。 */
function inheritCandidates(group: LandConfirmationGroupRow, person: LandConfirmationPersonRow): string[] {
  const names = new Set<string>()
  for (const candidate of group.persons) {
    const name = (candidate.name ?? '').trim()
    if (name && name !== (person.name ?? '').trim()) names.add(name)
  }
  return Array.from(names)
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
  const ledgerRowsCount = group.ledgers.reduce((sum, ledger) => sum + ledgerRows(ledger.key).length, 0)
  return group.key === 'land' ? ledgerRowsCount + landGroupCount.value : ledgerRowsCount
}

function groupTone(group: LedgerGroup): 'income' | 'expense' | 'asset' {
  if (group.key === 'expense') return 'expense'
  if (group.key === 'property') return 'asset'
  return 'income'
}

function importLabelFor(key: EditableLedgerKey): string {
  return key === 'subsidies' ? '导入农业补贴' : ''
}

function importingFor(key: EditableLedgerKey): boolean {
  return key === 'subsidies' ? importingSubsidies.value : false
}

function openSignalFor(key: EditableLedgerKey): number {
  return key === 'subsidies' ? subsidyOpenSignal.value : 0
}

function handleImport(key: EditableLedgerKey): void {
  if (key === 'subsidies') void importSubsidies()
}

/** 旧版导入依赖申请已落库（按申请人/成员查库），未保存时先提示保存草稿。 */
function ensureSaved(): boolean {
  if (props.form.id > 0) return true
  ElMessage.warning('请先保存草稿，再导入台账数据')
  return false
}

async function importSubsidies(): Promise<void> {
  if (!ensureSaved()) return

  importingSubsidies.value = true
  try {
    const records = await getApplicationSubsidyImports(props.form.id)
    if (records.length === 0) {
      ElMessage.info('未找到农业补贴数据')
      return
    }

    const result = mergeSubsidyImports(ledgerRows('subsidies'), records)
    subsidyOpenSignal.value += 1

    if (result.added > 0) {
      const amountOnly = records.filter(
        (record) => numberOrZero(record.area) === 0 && numberOrZero(record.amount) > 0,
      ).length
      const converted =
        amountOnly > 0 ? `（金额型 ${amountOnly} 条已按标准单价折算面积）` : ''
      const skipped = result.skipped > 0 ? `，跳过重复 ${result.skipped} 条` : ''
      ElMessage.success(`已导入 ${result.added} 条农业补贴${converted}${skipped}`)
    } else {
      ElMessage.info(`未新增补贴记录，${result.skipped} 条均已在台账中`)
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '导入农业补贴失败'))
  } finally {
    importingSubsidies.value = false
  }
}

async function importLandConfirmation(): Promise<void> {
  if (!ensureSaved()) return

  importingLand.value = true
  try {
    const records = await getApplicationLandConfirmationImports(props.form.id)
    if (records.length === 0) {
      ElMessage.info('未找到土地确权台账数据')
      return
    }

    const groups = landGroups()
    const result = mergeLandConfirmationImports(groups, records)

    // 导入后按旧版口径给空人员组补种户主 + 共同生活成员，避免家庭份额为 0。
    for (const group of groups) seedLandPersons(group)
    syncLandPersonAreas()

    if (result.added > 0) {
      const skipped = result.skipped > 0 ? `，跳过重复 ${result.skipped} 条` : ''
      ElMessage.success(
        `已导入 ${result.added} 条土地确权记录（${result.memberCount} 户）${skipped}`,
      )
    } else {
      ElMessage.info(`未新增土地确权记录，${result.skipped} 条均已在归户表中`)
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '导入土地确权台账失败'))
  } finally {
    importingLand.value = false
  }
}
</script>

<template>
  <div class="step-economy">
    <NcCard
      v-for="group in groups"
      :key="group.key"
      :title="group.title"
      :subtitle="group.subtitle"
    >
      <template #actions>
        <template v-if="group.key === 'land'">
          <el-button size="small" :icon="Plus" :disabled="busy" @click="addLandGroup">
            新增组
          </el-button>
          <el-button
            size="small"
            type="primary"
            plain
            :icon="Download"
            :loading="importingLand"
            :disabled="busy"
            @click="importLandConfirmation"
          >
            导入土地确权
          </el-button>
        </template>
        <span class="section-meta">{{ groupCount(group) }} 条明细</span>
      </template>

      <div class="ledger-stack">
        <section v-if="group.key === 'land'" class="land-sheet">
          <header class="land-sheet__head">
            <div class="land-sheet__heading">
              <h3 class="land-sheet__title">土地确权归户表</h3>
              <p class="land-sheet__hint">
                按承包方（户主）分组维护地块与人员；份额、家庭面积与土地收入在本页即时汇总，
                保存后由服务端按 Core 口径重算落库。
              </p>
            </div>
          </header>

          <p v-if="landGroupCount === 0" class="land-sheet__empty">
            暂无土地确权归户表，可点击“导入土地确权”或“新增组”。
          </p>

          <article
            v-for="landGroup in landGroups()"
            :key="landKey(landGroup)"
            class="land-group"
          >
            <header class="land-group__head">
              <div class="land-group__identity">
                <span class="land-group__badge">承包方</span>
                <el-input
                  v-model="landGroup.contractorName"
                  class="land-group__name"
                  size="small"
                  placeholder="户主 / 承包方姓名"
                  :disabled="busy"
                />
                <span class="land-group__area">
                  面积合计 {{ formatNumber(groupArea(landGroup)) }} 亩
                </span>
              </div>
              <el-button
                size="small"
                text
                type="danger"
                :icon="Delete"
                :disabled="busy"
                @click="removeLandGroup(landGroup)"
              >
                删除组
              </el-button>
            </header>

            <section class="land-block">
              <header class="land-block__head">
                <h4 class="land-block__title">
                  地块记录
                  <span>{{ landGroup.records.length }} 块</span>
                </h4>
                <el-button
                  size="small"
                  text
                  :icon="Plus"
                  :disabled="busy"
                  @click="addLandRecord(landGroup)"
                >
                  新增地块
                </el-button>
              </header>

              <div class="land-batch">
                <span class="land-batch__label">批量用途</span>
                <el-button
                  v-for="option in LAND_USAGE_OPTIONS"
                  :key="option.value"
                  size="small"
                  plain
                  :disabled="busy || landGroup.records.length === 0"
                  @click="batchSetLandUsage(option.value)"
                >
                  {{ LAND_USAGE_BATCH_LABELS[option.value] }}
                </el-button>
              </div>

              <p v-if="landGroup.records.length === 0" class="land-block__empty">
                暂无地块，点击“新增地块”录入。
              </p>
              <div v-else class="land-record-grid">
                <div
                  v-for="record in landGroup.records"
                  :key="landKey(record)"
                  class="land-record"
                >
                  <div class="land-record__head">
                    <el-input
                      v-model="record.landPlotInfo"
                      size="small"
                      placeholder="地块名称 / 位置"
                      :disabled="busy"
                    />
                    <el-button
                      size="small"
                      text
                      type="danger"
                      :icon="Delete"
                      :disabled="busy"
                      @click="removeLandRecord(landGroup, record)"
                    />
                  </div>

                  <div class="land-record__row">
                    <label class="land-field">
                      <span class="land-field__label">面积</span>
                      <el-input-number
                        :model-value="record.landArea"
                        class="land-field__control"
                        size="small"
                        :min="0"
                        :precision="2"
                        :step="0.1"
                        :controls="false"
                        :disabled="busy"
                        @update:model-value="
                          record.landArea = numberOrZero($event);
                          syncLandPersonAreas()
                        "
                      />
                      <em class="land-field__unit">亩</em>
                    </label>
                    <el-select
                      :model-value="record.landUsage"
                      class="land-field__select"
                      size="small"
                      :disabled="busy"
                      @change="changeLandUsage(record, $event)"
                    >
                      <el-option
                        v-for="option in LAND_USAGE_OPTIONS"
                        :key="option.value"
                        :label="option.label"
                        :value="option.value"
                      />
                    </el-select>
                  </div>

                  <div class="land-record__row">
                    <label class="land-field">
                      <span class="land-field__label">单价</span>
                      <el-input-number
                        :model-value="record.unitPrice"
                        class="land-field__control"
                        size="small"
                        :min="0"
                        :precision="2"
                        :step="10"
                        :controls="false"
                        :disabled="busy"
                        @update:model-value="record.unitPrice = numberOrZero($event)"
                      />
                      <em class="land-field__unit">元/亩</em>
                    </label>
                    <span class="land-record__value">
                      = ¥{{ formatMoney(landRecordValue(record)) }}
                    </span>
                  </div>
                </div>
              </div>
            </section>

            <section class="land-block">
              <header class="land-block__head">
                <h4 class="land-block__title">
                  人员表
                  <span>{{ landGroup.persons.length }} 人</span>
                </h4>
                <el-button
                  size="small"
                  text
                  :icon="Plus"
                  :disabled="busy"
                  @click="addLandPerson(landGroup)"
                >
                  新增人员
                </el-button>
              </header>

              <p v-if="landGroup.persons.length === 0" class="land-block__empty">
                暂无人员，可新增或导入后自动补齐。
              </p>
              <div v-else class="land-person-grid">
                <div
                  v-for="person in landGroup.persons"
                  :key="landKey(person)"
                  class="land-person"
                >
                  <div class="land-person__head">
                    <el-input
                      v-model="person.name"
                      size="small"
                      placeholder="姓名"
                      :disabled="busy"
                    />
                    <el-button
                      size="small"
                      text
                      type="danger"
                      :icon="Delete"
                      :disabled="busy"
                      @click="removeLandPerson(landGroup, person)"
                    />
                  </div>

                  <el-input
                    v-model="person.idCard"
                    size="small"
                    placeholder="身份证号"
                    :disabled="busy"
                  />

                  <div class="land-person__row">
                    <el-select
                      :model-value="person.landStatus || '存活享有'"
                      class="land-person__status"
                      size="small"
                      :disabled="busy"
                      @change="changeLandStatus(person, $event)"
                    >
                      <el-option
                        v-for="option in LAND_STATUS_OPTIONS"
                        :key="option.value"
                        :label="option.label"
                        :value="option.value"
                      />
                    </el-select>
                    <label class="land-field land-field--compact">
                      <el-input-number
                        :model-value="person.sharesCount"
                        class="land-field__control"
                        size="small"
                        :min="0"
                        :precision="1"
                        :step="1"
                        :controls="false"
                        :disabled="busy"
                        @update:model-value="changeLandShares(person, $event)"
                      />
                      <em class="land-field__unit">份</em>
                    </label>
                  </div>

                  <label class="land-field">
                    <span class="land-field__label">面积（自动）</span>
                    <el-input-number
                      :model-value="person.totalLandArea"
                      class="land-field__control"
                      size="small"
                      :min="0"
                      :precision="2"
                      :step="0.1"
                      :controls="false"
                      :disabled="true"
                    />
                    <em class="land-field__unit">亩</em>
                  </label>

                  <label
                    v-if="person.landStatus === LAND_STATUS_DECEASED"
                    class="land-field"
                  >
                    <span class="land-field__label">继承</span>
                    <el-select
                      :model-value="person.landInheritTo"
                      class="land-field__select"
                      size="small"
                      clearable
                      placeholder="选择继承人"
                      :disabled="busy"
                      @change="changeLandInherit(person, $event)"
                    >
                      <el-option
                        v-for="name in inheritCandidates(landGroup, person)"
                        :key="name"
                        :label="name"
                        :value="name"
                      />
                    </el-select>
                  </label>
                </div>
              </div>
            </section>
          </article>

          <section class="land-summary">
            <header class="land-summary__head">
              <h4>家庭土地面积汇总</h4>
              <p>家庭占比 = 本户有效份额 ÷ 全组有效份额；死亡继承份额按指定继承人计入。</p>
            </header>

            <div class="land-metrics">
              <div
                v-for="metric in landSummaryMetrics"
                :key="metric.label"
                class="land-metric"
              >
                <span>{{ metric.label }}</span>
                <strong>{{ metric.value }}</strong>
              </div>
            </div>

            <div class="land-area-grid">
              <div
                v-for="metric in landAreaMetrics"
                :key="metric.label"
                class="land-area"
              >
                <span>{{ metric.label }}</span>
                <strong>{{ metric.value }}</strong>
              </div>
            </div>

            <p class="land-income">
              土地收入合计
              <strong>¥{{ formatMoney(landTotals.landIncome) }}</strong>
            </p>
          </section>
        </section>

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
          :import-label="importLabelFor(ledger.key)"
          :importing="importingFor(ledger.key)"
          :open-signal="openSignalFor(ledger.key)"
          @add="addLedgerRow(ledger)"
          @remove="removeLedgerRow(ledger, $event)"
          @import="handleImport(ledger.key)"
        />
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

/* ── 土地确权归户表 ── */

.land-sheet {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 12px 13px 14px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.land-sheet__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
}

.land-sheet__heading {
  min-width: 0;
}

.land-sheet__title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
}

.land-sheet__hint {
  margin: 4px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.land-sheet__empty {
  margin: 0;
  padding: 14px 13px;
  border: 1px dashed var(--nc-line);
  background-color: var(--nc-paper-2);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.land-group {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 11px 12px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.land-group__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.land-group__identity {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.land-group__badge {
  flex: none;
  padding: 1px 7px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.land-group__name {
  max-width: 220px;
}

.land-group__area {
  flex: none;
  color: var(--nc-amber-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.land-block {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.land-block__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.land-block__title {
  display: flex;
  align-items: baseline;
  gap: 7px;
  color: var(--nc-amber-dark);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-sm);
  font-weight: 700;
}

.land-block__title span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 400;
}

.land-block__empty {
  margin: 0;
  padding: 9px 11px;
  border: 1px dashed var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.land-batch {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.land-batch__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.land-record-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(236px, 1fr));
  gap: 8px;
}

.land-record,
.land-person {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  padding: 8px 9px 9px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
}

.land-record__head,
.land-person__head {
  display: flex;
  align-items: center;
  gap: 4px;
}

.land-record__head :deep(.el-input),
.land-person__head :deep(.el-input) {
  flex: 1;
  min-width: 0;
}

.land-record__row,
.land-person__row {
  display: flex;
  align-items: center;
  gap: 6px;
}

.land-field {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 4px;
  min-width: 0;
}

.land-field__label {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.land-field__control {
  width: 100%;
  min-width: 0;
}

.land-field__unit {
  flex: none;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-style: normal;
  white-space: nowrap;
}

.land-field__select {
  flex: 1.2;
  min-width: 0;
}

.land-field--compact {
  flex: 0 0 74px;
}

.land-person__status {
  flex: 1;
  min-width: 0;
}

.land-record__value {
  flex: none;
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.land-person-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(216px, 1fr));
  gap: 8px;
}

.land-record :deep(.el-input__wrapper),
.land-person :deep(.el-input__wrapper),
.land-record :deep(.el-select__wrapper),
.land-person :deep(.el-select__wrapper),
.land-field :deep(.el-input__wrapper),
.land-field :deep(.el-select__wrapper),
.land-person__status :deep(.el-select__wrapper) {
  min-height: 28px;
  padding: 0 7px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  box-shadow: 0 0 0 1px var(--nc-line) inset;
}

.land-record :deep(.el-input-number),
.land-person :deep(.el-input-number),
.land-field :deep(.el-input-number) {
  width: 100%;
}

.land-record :deep(.el-input-number .el-input__inner),
.land-person :deep(.el-input-number .el-input__inner),
.land-field :deep(.el-input-number .el-input__inner) {
  font-family: var(--nc-font-mono);
  text-align: left;
}

.land-summary {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 2px;
  padding: 12px 13px 13px;
  border: 1px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-amber-soft);
}

.land-summary__head h4 {
  color: var(--nc-amber-dark);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  text-align: center;
}

.land-summary__head p {
  margin: 4px 0 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
  text-align: center;
}

.land-metrics {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 8px;
}

.land-area-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding-top: 9px;
  border-top: 1px solid var(--nc-amber);
}

.land-metric,
.land-area {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  min-width: 0;
}

.land-metric span,
.land-area span {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.land-metric strong,
.land-area strong {
  color: var(--nc-amber-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  white-space: nowrap;
}

.land-income {
  margin: 2px 0 0;
  padding-top: 9px;
  border-top: 1px solid var(--nc-amber);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.land-income strong {
  margin-left: 6px;
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
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

  .land-metrics {
    grid-template-columns: repeat(3, minmax(0, 1fr));
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

  .land-sheet__head,
  .land-group__head {
    flex-direction: column;
    align-items: stretch;
  }

  .land-group__identity {
    flex-wrap: wrap;
  }

  .land-group__name {
    flex: 1;
    max-width: none;
  }

  .land-record-grid,
  .land-person-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .land-metrics {
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

  .land-metrics,
  .land-area-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
