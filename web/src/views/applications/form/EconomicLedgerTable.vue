<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ArrowDown, Delete, Download, Plus } from '@element-plus/icons-vue'
import { maskIdCard } from '../format'
import type { LedgerColumn } from './economy'
import type { ApplicationFormMember } from './types'

const props = withDefaults(
  defineProps<{
    title: string
    hint?: string
    rows: Record<string, unknown>[]
    columns: LedgerColumn[]
    members?: ApplicationFormMember[]
    busy?: boolean
    defaultOpen?: boolean
    addLabel?: string
    tone?: 'income' | 'expense' | 'asset'
    /** 标题区导入按钮文案；留空不显示按钮（对应旧版分组内的「导入」）。 */
    importLabel?: string
    /** 导入请求进行中。 */
    importing?: boolean
    /** 递增信号：变化时展开台账，让导入结果可见。 */
    openSignal?: number
  }>(),
  {
    hint: '',
    members: () => [],
    busy: false,
    defaultOpen: false,
    addLabel: '新增一行',
    tone: 'income',
    importLabel: '',
    importing: false,
    openSignal: 0,
  },
)

const emit = defineEmits<{
  (event: 'add'): void
  (event: 'remove', row: Record<string, unknown>): void
  (event: 'import'): void
}>()

const expanded = ref(props.defaultOpen)

watch(
  () => props.openSignal,
  (value, previous) => {
    if (value !== previous) expanded.value = true
  },
)

/* 行 key：新增/删除行时避免 Vue 复用错位的输入控件。 */
let keySeed = 0
const rowKeys = new WeakMap<object, number>()
function rowKey(row: object): number {
  let key = rowKeys.get(row)
  if (key === undefined) {
    keySeed += 1
    key = keySeed
    rowKeys.set(row, key)
  }
  return key
}

interface MemberOption {
  key: string
  label: string
  member: ApplicationFormMember | null
}

const UNMATCHED_MEMBER = '__unmatched_member__'

const memberOptions = computed<MemberOption[]>(() =>
  props.members.map((member, index) => ({
    key: member.idCard ? `id:${member.idCard}` : `row:${index}:${member.name}`,
    label: `${member.name || '未命名成员'}${
      member.idCard ? ` · ${maskIdCard(member.idCard)}` : ''
    }`,
    member,
  })),
)

function textValue(row: Record<string, unknown>, key: string): string {
  const value = row[key]
  if (value === null || value === undefined) return ''
  return typeof value === 'string' ? value : String(value)
}

/** 文本列显示值：旧数据里的英文枚举 key 按列映射为中文。 */
function displayText(row: Record<string, unknown>, column: LedgerColumn): string {
  const raw = textValue(row, column.key)
  if (!raw || !column.labels) return raw
  return column.labels[raw] ?? column.labels[raw.toUpperCase()] ?? raw
}

function numberValue(row: Record<string, unknown>, key: string): number | undefined {
  const value = row[key]
  if (value === null || value === undefined || value === '') return undefined
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function setText(
  row: Record<string, unknown>,
  column: LedgerColumn,
  value: string | undefined,
): void {
  row[column.key] = value ?? ''
  column.onChange?.(row, value)
}

function setNumber(
  row: Record<string, unknown>,
  column: LedgerColumn,
  value: number | null | undefined,
): void {
  const empty = value === null || value === undefined || !Number.isFinite(value)
  row[column.key] = empty ? (column.nullable ? null : 0) : value
  column.onChange?.(row, value)
}

function setSelect(
  row: Record<string, unknown>,
  column: LedgerColumn,
  value: string | undefined,
): void {
  row[column.key] = value ?? ''
  column.onChange?.(row, value)
}

function setBoolean(row: Record<string, unknown>, key: string, value: unknown): void {
  row[key] = Boolean(value)
}

function rowMember(
  row: Record<string, unknown>,
  column: LedgerColumn,
): ApplicationFormMember | undefined {
  if (column.memberIdCardKey) {
    const idCard = textValue(row, column.memberIdCardKey)
    if (idCard) {
      const hit = memberOptions.value.find((entry) => entry.member?.idCard === idCard)
      if (hit?.member) return hit.member
    }
  }

  if (column.memberIdKey) {
    const memberId = numberValue(row, column.memberIdKey)
    if (memberId) {
      const hit = memberOptions.value.find((entry) => entry.member?.id === memberId)
      if (hit?.member) return hit.member
    }
  }

  const name = textValue(row, column.key)
  if (!name) return undefined
  return memberOptions.value.find((entry) => entry.member?.name === name)?.member ?? undefined
}

function memberValue(row: Record<string, unknown>, column: LedgerColumn): string {
  const hit = rowMember(row, column)
  if (hit) {
    const entry = memberOptions.value.find((option) => option.member === hit)
    if (entry) return entry.key
  }
  return textValue(row, column.key) ? UNMATCHED_MEMBER : ''
}

function memberOptionsForRow(
  row: Record<string, unknown>,
  column: LedgerColumn,
): MemberOption[] {
  if (rowMember(row, column)) return memberOptions.value
  const name = textValue(row, column.key)
  if (!name) return memberOptions.value
  return [
    ...memberOptions.value,
    { key: UNMATCHED_MEMBER, label: `${name}（未关联成员）`, member: null },
  ]
}

function handleMemberChange(
  row: Record<string, unknown>,
  column: LedgerColumn,
  value: string | undefined,
): void {
  const key = value ?? ''
  if (key === UNMATCHED_MEMBER) return

  const entry = memberOptions.value.find((option) => option.key === key)

  if (!entry?.member) {
    row[column.key] = ''
    if (column.memberIdCardKey) row[column.memberIdCardKey] = ''
    if (column.memberIdKey) row[column.memberIdKey] = 0
    if (column.memberAgeKey) row[column.memberAgeKey] = 0
    return
  }

  const selected = entry.member
  row[column.key] = selected.name
  if (column.memberIdCardKey) row[column.memberIdCardKey] = selected.idCard
  if (column.memberIdKey) row[column.memberIdKey] = selected.id
  if (column.memberAgeKey) row[column.memberAgeKey] = selected.age ?? 0
}

function cellStyle(column: LedgerColumn): Record<string, string> {
  return { minWidth: `${column.width ?? 140}px` }
}

function stepFor(column: LedgerColumn): number {
  if (column.step) return column.step
  return column.type === 'money' ? 100 : 1
}

function addRow(): void {
  expanded.value = true
  emit('add')
}
</script>

<template>
  <section class="ledger" :class="[`ledger--${tone}`, { 'is-open': expanded }]">
    <header class="ledger__head">
      <button
        type="button"
        class="ledger__toggle"
        :aria-expanded="expanded"
        @click="expanded = !expanded"
      >
        <el-icon class="ledger__chevron" aria-hidden="true"><ArrowDown /></el-icon>
        <span class="ledger__title">{{ title }}</span>
        <span class="ledger__count">{{ rows.length }} 条</span>
      </button>

      <div class="ledger__actions">
        <el-button
          v-if="importLabel"
          size="small"
          text
          :icon="Download"
          :loading="importing"
          :disabled="busy"
          @click="emit('import')"
        >
          {{ importLabel }}
        </el-button>
        <el-button
          size="small"
          text
          :icon="Plus"
          :disabled="busy"
          @click="addRow"
        >
          {{ addLabel }}
        </el-button>
      </div>
    </header>

    <div v-if="expanded" class="ledger__body">
      <p v-if="hint" class="ledger__hint">{{ hint }}</p>

      <div v-if="rows.length === 0" class="ledger__empty">
        <span>暂无明细，点击新增</span>
        <el-button size="small" plain :icon="Plus" :disabled="busy" @click="addRow">
          {{ addLabel }}
        </el-button>
      </div>

      <div v-else class="ledger__scroll">
        <table class="ledger-table">
          <thead>
            <tr>
              <th class="col-index">序号</th>
              <th
                v-for="column in columns"
                :key="column.key"
                :style="cellStyle(column)"
                scope="col"
              >
                <span>{{ column.label }}</span>
                <em v-if="column.unit" class="col-unit">{{ column.unit }}</em>
              </th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(row, index) in rows" :key="rowKey(row)">
              <td class="col-index">{{ index + 1 }}</td>

              <td
                v-for="column in columns"
                :key="column.key"
                :style="cellStyle(column)"
              >
                <el-select
                  v-if="column.options"
                  class="cell-control"
                  size="small"
                  filterable
                  clearable
                  :model-value="textValue(row, column.key)"
                  :disabled="busy || column.readonly"
                  :placeholder="column.placeholder"
                  @update:model-value="setSelect(row, column, $event)"
                >
                  <el-option
                    v-for="option in column.options"
                    :key="option.value"
                    :label="option.label"
                    :value="option.value"
                  />
                </el-select>

                <el-select
                  v-else-if="column.type === 'member'"
                  class="cell-control"
                  size="small"
                  filterable
                  clearable
                  :model-value="memberValue(row, column)"
                  :disabled="busy"
                  :placeholder="members.length ? '选择成员' : '请先在第 2 步添加成员'"
                  @update:model-value="handleMemberChange(row, column, $event)"
                >
                  <el-option
                    v-for="option in memberOptionsForRow(row, column)"
                    :key="option.key"
                    :label="option.label"
                    :value="option.key"
                  />
                </el-select>

                <el-switch
                  v-else-if="column.type === 'switch'"
                  size="small"
                  :model-value="Boolean(row[column.key])"
                  :disabled="busy"
                  @update:model-value="setBoolean(row, column.key, $event)"
                />

                <el-input-number
                  v-else-if="column.type === 'number' || column.type === 'money'"
                  class="cell-control"
                  size="small"
                  :model-value="numberValue(row, column.key)"
                  :min="column.min ?? 0"
                  :max="column.max"
                  :precision="column.precision"
                  :step="stepFor(column)"
                  :controls="false"
                  :disabled="busy || column.readonly"
                  :placeholder="column.placeholder ?? '0'"
                  :value-on-clear="column.nullable ? null : 0"
                  @update:model-value="setNumber(row, column, $event)"
                />

                <el-input
                  v-else
                  class="cell-control"
                  size="small"
                  clearable
                  :model-value="displayText(row, column)"
                  :placeholder="column.placeholder"
                  :disabled="busy || column.readonly"
                  @update:model-value="setText(row, column, $event)"
                />
              </td>

              <td class="col-actions">
                <el-button
                  size="small"
                  text
                  type="danger"
                  :icon="Delete"
                  :disabled="busy"
                  @click="emit('remove', row)"
                >
                  删除
                </el-button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>

<style scoped>
.ledger {
  --ledger-accent: var(--nc-seal);

  min-width: 0;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--ledger-accent);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.ledger--expense {
  --ledger-accent: var(--nc-amber);
}

.ledger--asset {
  --ledger-accent: var(--nc-pine);
}

.ledger.is-open {
  box-shadow: var(--nc-shadow-xs);
}

.ledger__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-height: 42px;
  padding: 5px 8px 5px 10px;
}

.ledger__toggle {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 8px;
  min-width: 0;
  padding: 4px 2px;
  border: 0;
  background: transparent;
  color: var(--nc-ink-2);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
}

.ledger__chevron {
  flex: none;
  color: var(--nc-muted);
  transition: transform var(--nc-transition);
}

.ledger.is-open .ledger__chevron {
  transform: rotate(180deg);
}

.ledger__title {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ledger__count {
  flex: none;
  padding: 1px 7px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.ledger__actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 4px;
}

.ledger__body {
  padding: 0 10px 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.ledger__hint {
  margin: 8px 0 9px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.ledger__empty {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-top: 9px;
  padding: 14px 13px;
  border: 1px dashed var(--nc-line);
  background-color: var(--nc-paper-2);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.ledger__scroll {
  margin-top: 9px;
  overflow-x: auto;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
  scrollbar-width: thin;
}

.ledger-table {
  width: max-content;
  min-width: 100%;
  border-collapse: collapse;
  table-layout: auto;
}

.ledger-table th,
.ledger-table td {
  padding: 6px 7px;
  border-right: 1px solid var(--nc-line-faint);
  border-bottom: 1px solid var(--nc-line-faint);
  vertical-align: middle;
}

.ledger-table th:last-child,
.ledger-table td:last-child {
  border-right: 0;
}

.ledger-table tbody tr:last-child td {
  border-bottom: 0;
}

.ledger-table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.03em;
  text-align: left;
  white-space: nowrap;
}

.ledger-table tbody td {
  background-color: var(--nc-paper-soft);
  font-size: var(--nc-text-sm);
}

.ledger-table tbody tr:hover td {
  background-color: var(--nc-paper-2);
}

.col-index {
  width: 52px;
  min-width: 52px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  text-align: center;
}

.col-unit {
  margin-left: 4px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-style: normal;
  font-weight: 400;
  letter-spacing: 0;
}

.col-actions {
  position: sticky;
  right: 0;
  z-index: 2;
  width: 88px;
  min-width: 88px;
  background-color: var(--nc-paper-soft) !important;
  border-left: 1px solid var(--nc-line);
  box-shadow: -4px 0 6px rgba(23, 19, 16, 0.04);
  text-align: center;
  white-space: nowrap;
}

.ledger-table thead th.col-actions {
  background-color: var(--nc-paper-3) !important;
}

.cell-control {
  width: 100%;
}

.cell-control :deep(.el-input__wrapper),
.cell-control :deep(.el-select__wrapper) {
  min-height: 28px;
  padding: 0 7px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  box-shadow: 0 0 0 1px var(--nc-line) inset;
}

.cell-control :deep(.el-input__wrapper.is-focus),
.cell-control :deep(.el-select__wrapper.is-focused) {
  box-shadow: 0 0 0 1px var(--nc-seal) inset;
}

.cell-control :deep(.el-input__inner) {
  color: var(--nc-ink);
  font-size: 12px;
}

.cell-control :deep(.el-input-number) {
  width: 100%;
}

.cell-control :deep(.el-input-number .el-input__inner) {
  font-family: var(--nc-font-mono);
  text-align: left;
}

@media (max-width: 767px) {
  .ledger__head {
    flex-wrap: wrap;
    gap: 4px;
  }

  .ledger__actions {
    margin-left: auto;
  }

  .ledger__body {
    padding: 0 7px 8px;
  }

  .ledger__empty {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
