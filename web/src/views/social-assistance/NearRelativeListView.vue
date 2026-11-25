<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Delete,
  Edit,
  Plus,
  Refresh,
  Search,
  View,
} from '@element-plus/icons-vue'
import {
  deleteNearRelative,
  getNearRelative,
  listNearRelatives,
} from '@/api/nearRelatives'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  NearRelativeEntry,
  NearRelativeLink,
  NearRelativeListItem,
} from '@/types/nearRelatives'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const LIST_PATH = '/social-assistance/near-relatives'

const keyword = ref('')
const town = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<NearRelativeListItem[]>([])
const towns = ref<string[]>([])
const totalCount = ref(0)
const allCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')
let requestTicket = 0

const detailVisible = ref(false)
const detailLoading = ref(false)
const detailError = ref('')
const detail = ref<NearRelativeEntry | null>(null)

const hasFilters = computed(() => Boolean(keyword.value.trim() || town.value))
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)
const drawerSize = computed(() => (isNarrow.value ? '92%' : '660px'))

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取备案名册…'
  const all = allCount.value || totalCount.value
  return hasFilters.value
    ? `匹配 ${totalCount.value} / 共 ${all} 条备案`
    : `共 ${all} 条备案`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的备案记录' : '暂无近亲属备案记录',
)

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours(),
  )}:${pad(date.getMinutes())}`
}

function recordTime(row: NearRelativeListItem): string {
  return formatDateTime(row.updatedAt ?? row.createdAt)
}

function maskIdCard(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return '—'
  if (text.length < 10) return text
  return `${text.slice(0, 6)}****${text.slice(-4)}`
}

function amountText(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return value.toFixed(2)
}

interface DetailField {
  label: string
  value: string
}

function linkFields(link: NearRelativeLink): DetailField[] {
  const fields: DetailField[] = [
    { label: '与工作人员关系', value: link.relation || '—' },
    { label: '姓名', value: link.name || '—' },
    { label: '身份证号', value: maskIdCard(link.idCard) },
    { label: '性别', value: link.gender || '—' },
    { label: '出生年月', value: link.birthDate || '—' },
    { label: '户籍地址', value: link.hukouAddress || '—' },
    { label: '居住地址', value: link.residenceAddress || '—' },
    { label: '家庭住址', value: link.familyAddress || '—' },
    { label: '家庭人口/共同生活人数', value: link.familySize ? String(link.familySize) : '—' },
    { label: '社会救助种类/保障类别', value: link.helpType || '—' },
    { label: '月保障金（元）', value: amountText(link.monthAmount) },
    { label: '保障金额（元）', value: amountText(link.reportAmount) },
    { label: '救助起始时间', value: link.startTime || '—' },
    { label: '家庭关系', value: link.familyRelation || '—' },
    { label: '申请时间', value: link.applyTime || '—' },
    { label: '申请原因', value: link.applyReason || '—' },
    { label: '家庭主要困难', value: link.familyDifficulty || '—' },
    { label: '家庭经济情况调查情况', value: link.economyInvestigate || '—' },
    { label: '乡镇（街道）意见', value: link.townOpinion || '—' },
    { label: '动态管理记录', value: link.dynamicRecord || '—' },
  ]
  if (link.applicationId) {
    fields.unshift({ label: '关联申请 ID', value: String(link.applicationId) })
  }
  return fields
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listNearRelatives({
      town: town.value || undefined,
      keyword: keyword.value.trim() || undefined,
      page: page.value,
      pageSize: pageSize.value,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    towns.value = response.towns ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    allCount.value = response.allCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    allCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '备案名册加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleTownChange(): void {
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
  town.value = ''
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void load()
}

function goBack(): void {
  void router.push('/social-assistance')
}

function createEntry(): void {
  void router.push(`${LIST_PATH}/new`)
}

function editEntry(row: { id: number }): void {
  void router.push(`${LIST_PATH}/${row.id}/edit`)
}

async function removeEntry(row: NearRelativeListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除 [${row.staffName}] 的备案及其 ${row.linkCount} 名对象吗？此操作不可恢复。`,
      '确认删除',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  try {
    await deleteNearRelative(row.id)
    ElMessage.success('备案已删除')
    if (items.value.length === 1 && page.value > 1) {
      page.value -= 1
    }
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除备案失败'))
  }
}

async function openDetail(row: NearRelativeListItem): Promise<void> {
  detailVisible.value = true
  detailLoading.value = true
  detailError.value = ''
  detail.value = null

  try {
    detail.value = await getNearRelative(row.id)
  } catch (error) {
    detailError.value = getErrorMessage(error, '备案详情加载失败')
  } finally {
    detailLoading.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page near-relative-page">
    <NcPageHeader
      title="近亲属备案"
      kicker="Near-Relative Filing"
      description="工作人员近亲属享受社会救助的备案名册，防优亲厚友。"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回工作台</el-button>
        <el-button type="primary" :icon="Plus" @click="createEntry">新增备案</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索工作人员姓名、工作单位"
          @clear="handleSearch"
        />
        <el-select
          v-model="town"
          class="filter-town"
          clearable
          filterable
          placeholder="全部乡镇（街道）"
          @change="handleTownChange"
        >
          <el-option v-for="item in towns" :key="item" :label="item" :value="item" />
        </el-select>
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            检 索
          </el-button>
          <el-button v-if="hasFilters" text native-type="button" @click="handleReset">
            重置
          </el-button>
        </div>
      </form>
    </NcCard>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load()">重新加载</el-button>
    </el-alert>

    <NcCard title="备案名册" :subtitle="listSubtitle" padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'is-cards': isNarrow }"
      >
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <template v-else-if="items.length > 0">
          <el-table
            v-if="!isNarrow"
            :data="items"
            row-key="id"
            class="near-relative-table"
          >
            <el-table-column label="工作人员姓名" min-width="130">
              <template #default="{ row }">
                <strong class="staff-name">{{ row.staffName || '—' }}</strong>
                <span class="staff-id-card">{{ row.staffIdCardMasked || '' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="工作单位" min-width="190">
              <template #default="{ row }">
                <span class="work-unit" :title="row.workUnit">{{ row.workUnit || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="乡镇（街道）" min-width="120">
              <template #default="{ row }">
                <span class="town">{{ row.town || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="关联对象" width="100" align="right">
              <template #default="{ row }">
                <span class="link-count">{{ row.linkCount }} 名</span>
              </template>
            </el-table-column>

            <el-table-column label="备案时间" width="148">
              <template #default="{ row }">{{ recordTime(row as NearRelativeListItem) }}</template>
            </el-table-column>

            <el-table-column label="操作" width="196" align="right">
              <template #default="{ row }">
                <div class="row-actions">
                  <el-button
                    link
                    type="primary"
                    :icon="View"
                    @click="openDetail(row as NearRelativeListItem)"
                  >
                    查看
                  </el-button>
                  <el-button
                    link
                    type="primary"
                    :icon="Edit"
                    @click="editEntry(row as NearRelativeListItem)"
                  >
                    编辑
                  </el-button>
                  <el-button
                    link
                    type="danger"
                    :icon="Delete"
                    @click="removeEntry(row as NearRelativeListItem)"
                  >
                    删除
                  </el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="near-relative-cards">
            <article v-for="row in items" :key="row.id" class="filing-card">
              <header class="card-head">
                <div class="card-person">
                  <strong>{{ row.staffName || '—' }}</strong>
                  <span class="staff-id-card">{{ row.staffIdCardMasked || '' }}</span>
                </div>
                <span class="link-count">{{ row.linkCount }} 名对象</span>
              </header>

              <div class="card-meta-grid">
                <div>
                  <span>工作单位</span>
                  <strong>{{ row.workUnit || '—' }}</strong>
                </div>
                <div>
                  <span>乡镇（街道）</span>
                  <strong>{{ row.town || '—' }}</strong>
                </div>
                <div>
                  <span>职务（职级）</span>
                  <strong>{{ row.position || '—' }}</strong>
                </div>
                <div>
                  <span>备案时间</span>
                  <strong>{{ recordTime(row) }}</strong>
                </div>
              </div>

              <div class="card-actions">
                <el-button :icon="View" size="small" @click="openDetail(row)">查看详情</el-button>
                <el-button :icon="Edit" size="small" type="primary" plain @click="editEntry(row)">
                  编辑
                </el-button>
                <el-button :icon="Delete" size="small" type="danger" plain @click="removeEntry(row)">
                  删除
                </el-button>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">清除筛选</el-button>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div v-if="items.length > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :page-sizes="[10, 20, 50]"
            :total="totalCount"
            :layout="paginationLayout"
            :pager-count="isNarrow ? 5 : 7"
            :small="isNarrow"
            background
            @current-change="handlePageChange"
            @size-change="handleSizeChange"
          />
        </div>
      </template>
    </NcCard>

    <el-drawer
      v-model="detailVisible"
      title="备案详情"
      :size="drawerSize"
      destroy-on-close
      class="filing-drawer"
    >
      <div v-loading="detailLoading" class="drawer-body">
        <el-alert
          v-if="detailError"
          type="error"
          :title="detailError"
          :closable="false"
          show-icon
        />

        <template v-else-if="detail">
          <section class="drawer-section">
            <h3 class="drawer-heading">社会救助经办（工作）人员基本情况</h3>
            <el-descriptions :column="isNarrow ? 1 : 2" border size="small">
              <el-descriptions-item label="姓名">
                {{ detail.staff.staffName || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="身份证号">
                {{ maskIdCard(detail.staff.staffIdCard) }}
              </el-descriptions-item>
              <el-descriptions-item label="联系方式">
                {{ detail.staff.staffPhone || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="工作单位">
                {{ detail.staff.workUnit || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="职务（职级）">
                {{ detail.staff.position || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="乡镇（街道）">
                {{ detail.staff.town || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="备案时间">
                {{ formatDateTime(detail.staff.updatedAt ?? detail.staff.createdAt) }}
              </el-descriptions-item>
            </el-descriptions>
          </section>

          <section class="drawer-section">
            <h3 class="drawer-heading">
              近亲属享受社会救助情况
              <span class="drawer-count">{{ detail.links.length }} 名对象</span>
            </h3>

            <div v-if="detail.links.length > 0" class="drawer-links">
              <article v-for="(link, index) in detail.links" :key="link.id" class="drawer-link">
                <header class="drawer-link-head">
                  <strong>救助对象 {{ index + 1 }}</strong>
                  <span v-if="link.applicationId" class="archive-tag">
                    申请 #{{ link.applicationId }}
                  </span>
                </header>
                <dl class="field-grid">
                  <div v-for="field in linkFields(link)" :key="field.label" class="field-item">
                    <dt>{{ field.label }}</dt>
                    <dd>{{ field.value }}</dd>
                  </div>
                </dl>
              </article>
            </div>
            <el-empty v-else description="该备案暂无救助对象行" />
          </section>
        </template>
      </div>

      <template #footer>
        <div v-if="detail" class="drawer-footer">
          <el-button @click="detailVisible = false">关闭</el-button>
          <el-button
            type="primary"
            :icon="Edit"
            @click="editEntry(detail.staff)"
          >
            编辑本条备案
          </el-button>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped>
.near-relative-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.filter-card {
  overflow: hidden;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 280px;
  max-width: 460px;
}

.filter-town {
  flex: 0 0 200px;
}

.filter-actions {
  display: flex;
  flex: none;
  gap: 8px;
  margin-left: auto;
}

.load-error {
  margin: 0;
}

.list-card {
  overflow: hidden;
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.list-area {
  min-height: 220px;
}

.list-area.is-cards {
  padding: 10px;
}

.list-skeleton {
  padding: 22px;
}

.near-relative-table {
  width: 100%;
}

.staff-name {
  display: block;
  color: var(--nc-ink);
  font-size: 14px;
  font-weight: 600;
}

.staff-id-card {
  display: block;
  margin-top: 2px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.02em;
}

.work-unit,
.town {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.link-count {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.row-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 2px;
  white-space: nowrap;
}

.near-relative-cards {
  display: grid;
  gap: 10px;
}

.filing-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-person {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
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

.card-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-actions :deep(.el-button) {
  margin-left: 0;
}

.empty-wrap {
  padding: 10px 8px;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.drawer-body {
  min-height: 180px;
}

.drawer-section + .drawer-section {
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.drawer-heading {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.drawer-count {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 400;
  letter-spacing: 0.06em;
}

.drawer-links {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.drawer-link {
  padding: 12px 13px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.drawer-link-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--nc-line-faint);
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.archive-tag {
  padding: 1px 7px;
  border: 1px solid rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  white-space: nowrap;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin-top: 9px;
}

.field-item {
  min-width: 0;
  line-height: 1.5;
}

.field-item dt {
  color: var(--nc-muted);
  font-size: 10px;
  letter-spacing: 0.04em;
}

.field-item dd {
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.drawer-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

@media (max-width: 767px) {
  .filter-bar {
    padding: 12px;
  }

  .filter-search,
  .filter-town {
    flex: 1 1 100%;
    max-width: none;
  }

  .filter-actions {
    flex: 1 1 100%;
    margin-left: 0;
  }

  .filter-actions .el-button {
    flex: 1;
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }

  .field-grid {
    grid-template-columns: 1fr;
  }
}
</style>
