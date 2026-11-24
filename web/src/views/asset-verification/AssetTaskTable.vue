<script setup lang="ts">
import { ArrowRight } from '@element-plus/icons-vue'
import type { AssetTaskListItem } from '@/types/assetVerification'
import AssetStatusTag from './AssetStatusTag.vue'
import { formatDate, textOrDash } from './format'

withDefaults(
  defineProps<{
    items: AssetTaskListItem[]
    narrow?: boolean
    emptyDescription?: string
  }>(),
  {
    narrow: false,
    emptyDescription: '暂无核对任务',
  },
)

const emit = defineEmits<{ select: [item: AssetTaskListItem] }>()

function handleSelect(item: AssetTaskListItem): void {
  emit('select', item)
}

function locationText(item: AssetTaskListItem): string {
  const parts = [item.community, item.familyAddress]
    .map((part) => (part ?? '').trim())
    .filter(Boolean)
  return parts.length > 0 ? parts.join(' / ') : '—'
}
</script>

<template>
  <div class="task-table-wrap">
    <el-table
      v-if="!narrow"
      :data="items"
      row-key="id"
      class="task-table"
      @row-click="handleSelect"
    >
      <el-table-column label="申请人" min-width="132">
        <template #default="{ row }">
          <div class="person-cell">
            <strong>{{ textOrDash(row.archiveName) }}</strong>
            <span class="batch">{{ textOrDash(row.batchId) }}</span>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="身份证（脱敏）" min-width="150">
        <template #default="{ row }">
          <span class="mono">{{ textOrDash(row.idCardMasked) }}</span>
        </template>
      </el-table-column>

      <el-table-column label="与户主关系" min-width="108">
        <template #default="{ row }">
          <span class="relation">
            {{ textOrDash(row.relationship) }}
            <em v-if="row.isHead" class="head-badge">户主</em>
          </span>
        </template>
      </el-table-column>

      <el-table-column label="户主身份证（脱敏）" min-width="152">
        <template #default="{ row }">
          <span class="mono">{{ textOrDash(row.headIdCardMasked) }}</span>
        </template>
      </el-table-column>

      <el-table-column label="社区 / 家庭住址" min-width="188">
        <template #default="{ row }">
          <span class="location" :title="locationText(row as AssetTaskListItem)">
            {{ locationText(row as AssetTaskListItem) }}
          </span>
        </template>
      </el-table-column>

      <el-table-column label="申请日期" width="108">
        <template #default="{ row }">{{ formatDate(row.applicationDate) }}</template>
      </el-table-column>

      <el-table-column label="状态" width="136">
        <template #default="{ row }">
          <AssetStatusTag :status="row.status" :status-text="row.statusText" />
        </template>
      </el-table-column>

      <el-table-column label="联系电话（脱敏）" width="132">
        <template #default="{ row }">
          <span class="mono">{{ textOrDash(row.contactPhoneMasked) }}</span>
        </template>
      </el-table-column>

      <el-table-column label="操作" width="64" align="right">
        <template #default="{ row }">
          <el-button link type="primary" @click.stop="handleSelect(row as AssetTaskListItem)">
            查看
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <div v-else class="task-cards">
      <article
        v-for="item in items"
        :key="item.id"
        class="task-card"
        role="button"
        tabindex="0"
        @click="handleSelect(item)"
        @keydown.enter="handleSelect(item)"
      >
        <header class="card-head">
          <div class="person-cell">
            <strong>{{ textOrDash(item.archiveName) }}</strong>
            <span class="batch">{{ textOrDash(item.batchId) }}</span>
          </div>
          <AssetStatusTag :status="item.status" :status-text="item.statusText" />
        </header>

        <p class="card-id mono">{{ textOrDash(item.idCardMasked) }}</p>

        <div class="card-meta-grid">
          <div>
            <span>与户主关系</span>
            <strong>
              {{ textOrDash(item.relationship) }}
              <em v-if="item.isHead" class="head-badge">户主</em>
            </strong>
          </div>
          <div>
            <span>户主身份证</span>
            <strong class="mono">{{ textOrDash(item.headIdCardMasked) }}</strong>
          </div>
          <div>
            <span>联系电话</span>
            <strong class="mono">{{ textOrDash(item.contactPhoneMasked) }}</strong>
          </div>
          <div>
            <span>申请日期</span>
            <strong>{{ formatDate(item.applicationDate) }}</strong>
          </div>
        </div>

        <p class="card-location">{{ locationText(item) }}</p>

        <button class="card-foot" type="button" @click.stop="handleSelect(item)">
          查看核对详情
          <el-icon><ArrowRight /></el-icon>
        </button>
      </article>
    </div>
  </div>
</template>

<style scoped>
.task-table {
  width: 100%;
}

.task-table :deep(.el-table__row) {
  cursor: pointer;
}

.person-cell {
  display: flex;
  flex-direction: column;
  line-height: 1.45;
}

.person-cell strong {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.batch {
  overflow: hidden;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.04em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.relation {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}

.head-badge {
  padding: 0 4px;
  border: 1px solid rgba(171, 50, 39, 0.42);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-style: normal;
  line-height: 1.5;
}

.location {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 窄屏卡片 ─────────────────────────────────────────────────── */
.task-cards {
  display: grid;
  gap: 10px;
}

.task-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.task-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.task-card:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-head .person-cell strong {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-id {
  margin-top: 7px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin-top: 12px;
  padding-top: 11px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-meta-grid div {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.45;
}

.card-meta-grid span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.card-meta-grid strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-location {
  margin-top: 10px;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-foot {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 5px;
  width: 100%;
  margin-top: 10px;
  padding: 0;
  border: none;
  background: none;
  color: var(--nc-seal);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  cursor: pointer;
}
</style>
