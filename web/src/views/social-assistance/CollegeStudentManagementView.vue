<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, CircleCheck, Delete, Edit, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  createCollegeStudent,
  deleteCollegeStudent,
  listCollegeStudents,
  listGraduatingCollegeStudents,
  searchEligibleCollegeMembers,
  updateCollegeStudent,
} from '@/api/collegeStudents'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import {
  buildEnrollmentYearOptions,
  buildGraduationBannerText,
  COLLEGE_EDUCATION_LEVELS,
  COLLEGE_SCHOOL_DURATIONS,
  COLLEGE_STATUS_OPTIONS,
  COLLEGE_STUDENT_STATUS,
} from '@/types/collegeStudents'
import type {
  CollegeStudentFormRequest,
  CollegeStudentListItem,
  CollegeStudentStatus,
  EligibleCollegeMember,
} from '@/types/collegeStudents'

type TabKey = 0 | 1 | 2 | 3
type AssociatedFilter = 'all' | 'Studying' | 'Graduated' | 'other'
type ChipTone = 'studying' | 'graduated' | 'noteligible' | 'nohigher' | 'muted'

interface TabDefinition {
  key: TabKey
  label: string
  count: number
}

interface FilterDefinition {
  value: AssociatedFilter
  label: string
  count: number
}

const EMPTY_TEXT: Record<TabKey, string> = {
  0: '未找到符合条件（18-23周岁且未关联）的家庭成员',
  1: '暂无已关联大学生档案',
  2: '暂无不在范围内的人员',
  3: '今年暂无毕业大学生',
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const activeTab = ref<TabKey>(0)
const associatedFilter = ref<AssociatedFilter>('all')
const keyword = ref('')
const page = ref(1)
const pageSize = ref(10)

const eligibleMembers = ref<EligibleCollegeMember[]>([])
const students = ref<CollegeStudentListItem[]>([])
const graduatingStudents = ref<CollegeStudentListItem[]>([])
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

/* ───────────────────────────── 分栏与统计 ───────────────────────────── */

/** 与旧版 LoadAssociatedAndNotInScopeAsync 同一拆分口径（Core 已计算好三个标志）。 */
function isNotInScope(row: CollegeStudentListItem): boolean {
  return (
    row.status === COLLEGE_STUDENT_STATUS.NotEligible ||
    row.isBeyondJobSeekingPeriod ||
    row.isHouseholdStopped
  )
}

const associatedStudents = computed(() => students.value.filter((row) => !isNotInScope(row)))
const notInScopeStudents = computed(() => students.value.filter(isNotInScope))

const associatedFilterOptions = computed<FilterDefinition[]>(() => {
  const list = associatedStudents.value
  return [
    { value: 'all', label: '全部', count: list.length },
    {
      value: 'Studying',
      label: '在读',
      count: list.filter((row) => row.status === COLLEGE_STUDENT_STATUS.Studying).length,
    },
    {
      value: 'Graduated',
      label: '已毕业',
      count: list.filter((row) => row.status === COLLEGE_STUDENT_STATUS.Graduated).length,
    },
    {
      value: 'other',
      label: '其他',
      count: list.filter(
        (row) =>
          row.status !== COLLEGE_STUDENT_STATUS.Studying &&
          row.status !== COLLEGE_STUDENT_STATUS.Graduated,
      ).length,
    },
  ]
})

const filteredAssociated = computed(() => {
  switch (associatedFilter.value) {
    case 'Studying':
      return associatedStudents.value.filter(
        (row) => row.status === COLLEGE_STUDENT_STATUS.Studying,
      )
    case 'Graduated':
      return associatedStudents.value.filter(
        (row) => row.status === COLLEGE_STUDENT_STATUS.Graduated,
      )
    case 'other':
      return associatedStudents.value.filter(
        (row) =>
          row.status !== COLLEGE_STUDENT_STATUS.Studying &&
          row.status !== COLLEGE_STUDENT_STATUS.Graduated,
      )
    default:
      return associatedStudents.value
  }
})

const tabs = computed<TabDefinition[]>(() => [
  { key: 0, label: '可关联', count: eligibleMembers.value.length },
  { key: 1, label: '已关联', count: associatedStudents.value.length },
  { key: 2, label: '已退出', count: notInScopeStudents.value.length },
  { key: 3, label: '今年毕业', count: graduatingStudents.value.length },
])

const currentStudents = computed<CollegeStudentListItem[]>(() => {
  switch (activeTab.value) {
    case 1:
      return filteredAssociated.value
    case 2:
      return notInScopeStudents.value
    case 3:
      return graduatingStudents.value
    default:
      return []
  }
})

const currentTotal = computed(() =>
  activeTab.value === 0 ? eligibleMembers.value.length : currentStudents.value.length,
)

const pagedStudents = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return currentStudents.value.slice(start, start + pageSize.value)
})

const pagedMembers = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return eligibleMembers.value.slice(start, start + pageSize.value)
})

const displayTotalPages = computed(() =>
  Math.max(Math.ceil(currentTotal.value / pageSize.value), 1),
)
const emptyText = computed(() => EMPTY_TEXT[activeTab.value])
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next',
)
const hasKeyword = computed(() => keyword.value.trim().length > 0)

const graduationReminderText = computed(() =>
  buildGraduationBannerText(graduatingStudents.value.length),
)

/* ───────────────────────────── 加载 ───────────────────────────── */

async function loadAll(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const [eligible, all, graduating] = await Promise.all([
      searchEligibleCollegeMembers(keyword.value.trim() || undefined),
      listCollegeStudents(),
      listGraduatingCollegeStudents(),
    ])
    if (ticket !== requestTicket) return

    eligibleMembers.value = eligible
    students.value = all
    graduatingStudents.value = graduating

    const maxPage = Math.max(Math.ceil(currentTotal.value / pageSize.value), 1)
    if (page.value > maxPage) page.value = maxPage
  } catch (error) {
    if (ticket !== requestTicket) return
    eligibleMembers.value = []
    students.value = []
    graduatingStudents.value = []
    loadError.value = getErrorMessage(error, '大学生数据加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

/** Tab0 搜索只重载可关联成员（与旧版 SearchMembersCommand 一致）。 */
async function searchMembers(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const list = await searchEligibleCollegeMembers(keyword.value.trim() || undefined)
    if (ticket !== requestTicket) return
    eligibleMembers.value = list
    page.value = 1
  } catch (error) {
    if (ticket !== requestTicket) return
    loadError.value = getErrorMessage(error, '可关联成员搜索失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleSearch(): void {
  void searchMembers()
}

function handleResetSearch(): void {
  keyword.value = ''
  void searchMembers()
}

function switchTab(key: TabKey): void {
  if (activeTab.value === key) return
  activeTab.value = key
  loadError.value = ''
}

function goTab(key: TabKey): void {
  activeTab.value = key
  loadError.value = ''
}

function handlePageChange(value: number): void {
  page.value = value
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
}

/* ───────────────────────────── 展示辅助 ───────────────────────────── */

function dash(value: string): string {
  return value && value.trim() ? value : '—'
}

function yearText(value: number | null): string {
  return value ? String(value) : '—'
}

function statusTone(row: CollegeStudentListItem): ChipTone {
  switch (row.status) {
    case COLLEGE_STUDENT_STATUS.Studying:
      return 'studying'
    case COLLEGE_STUDENT_STATUS.Graduated:
      return 'graduated'
    case COLLEGE_STUDENT_STATUS.NotEligible:
      return 'noteligible'
    case COLLEGE_STUDENT_STATUS.NoHigherEducation:
      return 'nohigher'
    default:
      return 'muted'
  }
}

function reasonTags(row: CollegeStudentListItem): string[] {
  const tags: string[] = []
  if (row.isHouseholdStopped) tags.push('保障已终止')
  if (row.isBeyondJobSeekingPeriod) tags.push('已超择业期')
  return tags
}

/* ───────────────────────────── 编辑表单 ───────────────────────────── */

const editorVisible = ref(false)
const editorTitle = ref('')
const saving = ref(false)
const editingId = ref(0)
const enrollmentYearOptions = buildEnrollmentYearOptions()

const form = reactive<CollegeStudentFormRequest>({
  applicationId: 0,
  familyMemberId: 0,
  studentName: '',
  idCard: '',
  educationLevel: '',
  schoolName: '',
  schoolDuration: null,
  enrollmentYear: null,
  status: COLLEGE_STUDENT_STATUS.Studying,
  remark: '',
})

const isNoHigherEducation = computed(
  () => form.status === COLLEGE_STUDENT_STATUS.NoHigherEducation,
)
const isSchoolInfoRequired = computed(() => !isNoHigherEducation.value)

function normalizeStatus(value: string): CollegeStudentStatus {
  const matched = COLLEGE_STATUS_OPTIONS.find((option) => option.value === value)
  return matched?.value ?? COLLEGE_STUDENT_STATUS.Studying
}

function resetForm(): void {
  form.applicationId = 0
  form.familyMemberId = 0
  form.studentName = ''
  form.idCard = ''
  form.educationLevel = ''
  form.schoolName = ''
  form.schoolDuration = null
  form.enrollmentYear = null
  form.status = COLLEGE_STUDENT_STATUS.Studying
  form.remark = ''
}

/** 从可关联成员打开「关联大学生档案」表单（对应旧版 AddStudent）。 */
function openCreate(member: EligibleCollegeMember): void {
  resetForm()
  editingId.value = 0
  form.applicationId = member.applicationId
  form.familyMemberId = member.id
  form.studentName = member.name
  form.idCard = member.idCard
  form.enrollmentYear = new Date().getFullYear()
  editorTitle.value = '关联大学生档案'
  editorVisible.value = true
}

/** 编辑已有档案；mode=graduate 时预置状态为已毕业。 */
function openEdit(row: CollegeStudentListItem, mode: 'edit' | 'graduate' = 'edit'): void {
  editingId.value = row.id
  form.applicationId = row.applicationId
  form.familyMemberId = row.familyMemberId
  form.studentName = row.studentName
  form.idCard = row.idCard
  form.educationLevel = row.educationLevel
  form.schoolName = row.schoolName
  form.schoolDuration = row.schoolDuration
  form.enrollmentYear = row.enrollmentYear
  form.status =
    mode === 'graduate' ? COLLEGE_STUDENT_STATUS.Graduated : normalizeStatus(row.status)
  form.remark = row.remark
  editorTitle.value = mode === 'graduate' ? '更新毕业状态' : '编辑大学生档案'
  editorVisible.value = true
}

/** 与旧版 SaveStudentAsync 同序校验。 */
function validateForm(): string {
  if (!form.studentName.trim()) return '请输入大学生姓名'

  if (!isNoHigherEducation.value) {
    if (!form.schoolName.trim()) return '请输入学校名称'
    if (!form.educationLevel) return '请选择学历层次'
    if (form.schoolDuration === null) return '请选择学年制'
    if (form.enrollmentYear === null) return '请选择入学年份'
  }

  return ''
}

async function saveStudent(): Promise<void> {
  const validation = validateForm()
  if (validation) {
    ElMessage.warning(validation)
    return
  }

  const payload: CollegeStudentFormRequest = {
    applicationId: form.applicationId,
    familyMemberId: form.familyMemberId,
    studentName: form.studentName.trim(),
    idCard: form.idCard.trim(),
    educationLevel: form.educationLevel,
    schoolName: form.schoolName.trim(),
    schoolDuration: form.schoolDuration,
    enrollmentYear: form.enrollmentYear,
    status: form.status,
    remark: form.remark.trim(),
  }

  saving.value = true
  try {
    if (editingId.value > 0) {
      await updateCollegeStudent(editingId.value, payload)
      ElMessage.success('大学生档案已更新')
    } else {
      await createCollegeStudent(payload)
      ElMessage.success('大学生档案已保存')
    }
    editorVisible.value = false
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存失败'))
  } finally {
    saving.value = false
  }
}

async function handleDelete(row: CollegeStudentListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认删除 ${row.studentName} 的大学生档案？此操作不可恢复。`,
      '删除确认',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteCollegeStudent(row.id)
    ElMessage.success('大学生档案已删除')
    await loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  }
}

watch(activeTab, () => {
  page.value = 1
})

watch(associatedFilter, () => {
  page.value = 1
})

onMounted(() => {
  void loadAll()
})
</script>

<template>
  <div class="nc-page college-page">
    <NcPageHeader
      title="大学生管理"
      description="18-23周岁家庭成员的大学生档案关联与毕业管理"
      kicker="低收入人口救助帮扶 · 大学生管理"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/social-assistance')">
          返回低收入人口救助帮扶
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadAll">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="可关联成员" :value="eligibleMembers.length" tone="ink" compact />
      <NcStat label="已关联档案" :value="associatedStudents.length" tone="seal" compact />
      <NcStat label="已退出档案" :value="notInScopeStudents.length" tone="pine" compact />
      <NcStat label="今年毕业" :value="graduatingStudents.length" tone="amber" compact />
    </div>

    <el-alert
      v-if="graduatingStudents.length > 0"
      class="graduation-reminder"
      type="warning"
      title="年度毕业提醒"
      :closable="false"
      show-icon
    >
      <div class="reminder-body">
        <span>{{ graduationReminderText }}</span>
        <el-button link type="primary" @click="goTab(3)">查看今年毕业</el-button>
      </div>
    </el-alert>

    <NcCard padding="none" class="filter-card">
      <div class="tab-grid" role="tablist" aria-label="大学生档案分栏">
        <button
          v-for="tab in tabs"
          :key="tab.key"
          type="button"
          role="tab"
          class="tab-item"
          :class="{ 'is-active': activeTab === tab.key }"
          :aria-selected="activeTab === tab.key"
          @click="switchTab(tab.key)"
        >
          <span class="tab-item__count">{{ tab.count }}</span>
          <span class="tab-item__label">{{ tab.label }}</span>
        </button>
      </div>

      <form v-if="activeTab === 0" class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="输入姓名或身份证号搜索"
          @clear="handleSearch"
        />
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            搜索
          </el-button>
          <el-button v-if="hasKeyword" text native-type="button" @click="handleResetSearch">
            重置
          </el-button>
        </div>
      </form>

      <div v-if="activeTab === 1" class="sub-filter" role="group" aria-label="按状态筛选已关联档案">
        <button
          v-for="option in associatedFilterOptions"
          :key="option.value"
          type="button"
          class="sub-filter__item"
          :class="{ 'is-active': associatedFilter === option.value }"
          @click="associatedFilter = option.value"
        >
          {{ option.label }}
          <span class="sub-filter__count">{{ option.count }}</span>
        </button>
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
      <el-button link type="primary" @click="loadAll">重新加载</el-button>
    </el-alert>

    <NcCard padding="none" class="list-card">
      <div v-loading="loading && currentTotal > 0" class="list-area">
        <el-skeleton v-if="loading && currentTotal === 0" :rows="5" animated class="list-skeleton" />

        <!-- Tab0：可关联成员 -->
        <template v-else-if="activeTab === 0">
          <template v-if="eligibleMembers.length > 0">
            <el-table
              v-if="!isNarrow"
              :data="pagedMembers"
              row-key="id"
              class="data-table"
            >
              <el-table-column label="姓名" min-width="130">
                <template #default="{ row }">
                  <div class="person-cell">
                    <strong>{{ row.name || '—' }}</strong>
                    <span v-if="row.memberCategory" class="chip chip--info">
                      {{ row.memberCategory }}
                    </span>
                  </div>
                </template>
              </el-table-column>

              <el-table-column label="身份证号" min-width="175">
                <template #default="{ row }">
                  <span class="mono">{{ dash(row.idCard) }}</span>
                </template>
              </el-table-column>

              <el-table-column label="性别 / 年龄" width="110">
                <template #default="{ row }">{{ dash(row.genderAgeDisplay) }}</template>
              </el-table-column>

              <el-table-column label="户主" min-width="100">
                <template #default="{ row }">{{ dash(row.householdHeadName) }}</template>
              </el-table-column>

              <el-table-column label="电话" min-width="125">
                <template #default="{ row }">
                  <span class="mono">{{ dash(row.phone) }}</span>
                </template>
              </el-table-column>

              <el-table-column label="所在" min-width="150">
                <template #default="{ row }">
                  <span class="ellipsis" :title="row.homeLocationDisplay || '—'">
                    {{ dash(row.homeLocationDisplay) }}
                  </span>
                </template>
              </el-table-column>

              <el-table-column label="操作" width="88" align="right" fixed="right">
                <template #default="{ row }">
                  <el-button
                    size="small"
                    type="primary"
                    @click="openCreate(row as EligibleCollegeMember)"
                  >
                    关联
                  </el-button>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="member-cards">
              <article
                v-for="member in pagedMembers"
                :key="member.id"
                class="member-card"
              >
                <header class="card-head">
                  <div class="card-person">
                    <strong>{{ member.name || '—' }}</strong>
                    <span v-if="member.memberCategory" class="chip chip--info">
                      {{ member.memberCategory }}
                    </span>
                  </div>
                  <el-button size="small" type="primary" @click="openCreate(member)">
                    关联
                  </el-button>
                </header>

                <p class="card-line mono">{{ dash(member.idCard) }}</p>
                <p class="card-line">{{ dash(member.genderAgeDisplay) }}</p>
                <p class="card-line">
                  户主: {{ dash(member.householdHeadName) }}
                  <span class="line-gap" />
                  电话: {{ dash(member.phone) }}
                </p>
                <p class="card-line">所在: {{ dash(member.homeLocationDisplay) }}</p>
              </article>
            </div>
          </template>
        </template>

        <!-- Tab1-3：已关联 / 已退出 / 今年毕业 -->
        <template v-else>
          <template v-if="currentStudents.length > 0">
            <el-table
              v-if="!isNarrow"
              :data="pagedStudents"
              row-key="id"
              class="data-table"
            >
              <el-table-column label="姓名" min-width="120">
                <template #default="{ row }">
                  <div class="person-cell">
                    <strong>{{ row.studentName || '—' }}</strong>
                    <span v-if="row.educationLevel" class="chip chip--info">
                      {{ row.educationLevel }}
                    </span>
                  </div>
                </template>
              </el-table-column>

              <el-table-column label="学校" min-width="180">
                <template #default="{ row }">
                  <span class="ellipsis" :title="row.schoolName || '—'">
                    {{ dash(row.schoolName) }}
                  </span>
                </template>
              </el-table-column>

              <el-table-column label="学制" width="90">
                <template #default="{ row }">
                  {{ row.schoolDurationDisplay || '—' }}
                </template>
              </el-table-column>

              <el-table-column v-if="activeTab !== 2" label="入学" width="78">
                <template #default="{ row }">{{ yearText(row.enrollmentYear) }}</template>
              </el-table-column>

              <el-table-column label="毕业" width="78">
                <template #default="{ row }">{{ yearText(row.graduationYear) }}</template>
              </el-table-column>

              <el-table-column label="状态" min-width="160">
                <template #default="{ row }">
                  <div class="status-cell">
                    <span class="chip" :class="`chip--${statusTone(row as CollegeStudentListItem)}`">
                      {{ row.statusDisplay || '—' }}
                    </span>
                    <span
                      v-for="tag in reasonTags(row as CollegeStudentListItem)"
                      :key="tag"
                      class="chip chip--muted"
                    >
                      {{ tag }}
                    </span>
                  </div>
                </template>
              </el-table-column>

              <el-table-column label="备注" min-width="140">
                <template #default="{ row }">
                  <span class="ellipsis" :title="row.remark || '—'">
                    {{ dash(row.remark) }}
                  </span>
                </template>
              </el-table-column>

              <el-table-column label="操作" width="180" align="right" fixed="right">
                <template #default="{ row }">
                  <div class="row-actions">
                    <el-button
                      v-if="activeTab === 3"
                      size="small"
                      type="warning"
                      @click="openEdit(row as CollegeStudentListItem, 'graduate')"
                    >
                      更新毕业状态
                    </el-button>
                    <el-button
                      size="small"
                      type="primary"
                      @click="openEdit(row as CollegeStudentListItem)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="activeTab === 1"
                      size="small"
                      type="danger"
                      plain
                      @click="handleDelete(row as CollegeStudentListItem)"
                    >
                      删除
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="student-cards">
              <article
                v-for="row in pagedStudents"
                :key="row.id"
                class="student-card"
              >
                <header class="card-head">
                  <div class="card-person">
                    <strong>{{ row.studentName || '—' }}</strong>
                    <span v-if="row.educationLevel" class="chip chip--info">
                      {{ row.educationLevel }}
                    </span>
                  </div>
                  <span class="chip" :class="`chip--${statusTone(row)}`">
                    {{ row.statusDisplay || '—' }}
                  </span>
                </header>

                <p class="card-school">{{ dash(row.schoolName) }}</p>

                <p class="card-line">
                  {{ row.schoolDurationDisplay || '—' }}
                  <span v-if="activeTab !== 2" class="line-gap" />
                  <span v-if="activeTab !== 2">入学 {{ yearText(row.enrollmentYear) }}</span>
                  <span class="line-gap" />
                  毕业 {{ yearText(row.graduationYear) }}
                </p>

                <div v-if="reasonTags(row).length > 0" class="reason-chips">
                  <span v-for="tag in reasonTags(row)" :key="tag" class="chip chip--muted">
                    {{ tag }}
                  </span>
                </div>

                <p v-if="row.remark" class="card-remark">备注：{{ row.remark }}</p>

                <footer class="card-actions">
                  <el-button
                    v-if="activeTab === 3"
                    size="small"
                    type="warning"
                    :icon="CircleCheck"
                    @click="openEdit(row, 'graduate')"
                  >
                    更新毕业状态
                  </el-button>
                  <el-button size="small" type="primary" :icon="Edit" @click="openEdit(row)">
                    编辑
                  </el-button>
                  <el-button
                    v-if="activeTab === 1"
                    size="small"
                    type="danger"
                    plain
                    :icon="Delete"
                    @click="handleDelete(row)"
                  >
                    删除
                  </el-button>
                </footer>
              </article>
            </div>
          </template>
        </template>

        <div v-if="!loading && !loadError && currentTotal === 0" class="empty-wrap">
          <el-empty :description="emptyText">
            <el-button
              v-if="activeTab === 0 && hasKeyword"
              :icon="Refresh"
              @click="handleResetSearch"
            >
              清除搜索
            </el-button>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div v-if="currentTotal > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ currentTotal }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :page-sizes="[10, 20, 50]"
            :total="currentTotal"
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

    <!-- 关联 / 编辑 / 更新毕业状态 表单（对应旧版覆盖层弹窗） -->
    <el-dialog
      v-model="editorVisible"
      :title="editorTitle"
      :width="isNarrow ? '94%' : '640px'"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-position="top" class="editor-form" @submit.prevent="saveStudent">
        <div class="form-grid">
          <el-form-item label="大学生姓名" required>
            <el-input v-model="form.studentName" maxlength="100" placeholder="请输入姓名" />
          </el-form-item>
          <el-form-item label="身份证号">
            <el-input v-model="form.idCard" maxlength="18" placeholder="选填" />
          </el-form-item>
        </div>

        <template v-if="isSchoolInfoRequired">
          <div class="form-grid">
            <el-form-item label="学校名称" required>
              <el-input v-model="form.schoolName" maxlength="200" placeholder="请输入学校名称" />
            </el-form-item>
            <el-form-item label="学历层次" required>
              <el-select v-model="form.educationLevel" placeholder="请选择" style="width: 100%">
                <el-option
                  v-for="level in COLLEGE_EDUCATION_LEVELS"
                  :key="level"
                  :label="level"
                  :value="level"
                />
              </el-select>
            </el-form-item>
          </div>

          <div class="form-grid">
            <el-form-item label="学年制" required>
              <el-select v-model="form.schoolDuration" placeholder="请选择" style="width: 100%">
                <el-option
                  v-for="item in COLLEGE_SCHOOL_DURATIONS"
                  :key="item.value"
                  :label="item.label"
                  :value="item.value"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="入学年份" required>
              <el-select v-model="form.enrollmentYear" placeholder="请选择" style="width: 100%">
                <el-option
                  v-for="year in enrollmentYearOptions"
                  :key="year"
                  :label="`${year} 年`"
                  :value="year"
                />
              </el-select>
            </el-form-item>
          </div>
        </template>

        <el-form-item label="状态">
          <el-select v-model="form.status" style="width: 100%">
            <el-option
              v-for="option in COLLEGE_STATUS_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="备注">
          <el-input
            v-model="form.remark"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="选填"
          />
        </el-form-item>

        <p v-if="!isSchoolInfoRequired" class="form-hint">
          状态为「未收到高等教育」时无需填写学校信息。
        </p>
      </el-form>

      <template #footer>
        <el-button @click="editorVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="saveStudent">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.college-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.graduation-reminder {
  margin: 0;
}

.reminder-body {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  line-height: 1.6;
}

.filter-card,
.list-card {
  overflow: hidden;
}

.tab-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.tab-item {
  display: inline-flex;
  align-items: baseline;
  justify-content: center;
  gap: 6px;
  padding: 9px 10px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-family: inherit;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.tab-item:hover {
  border-color: var(--nc-line-strong);
}

.tab-item.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.tab-item.is-active .tab-item__label {
  color: var(--nc-ink);
  font-weight: 700;
}

.tab-item__count {
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  line-height: 1;
}

.tab-item__label {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 260px;
  max-width: 440px;
}

.filter-actions {
  display: flex;
  flex: none;
  gap: 8px;
  margin-left: auto;
}

.sub-filter {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 0 16px 14px;
}

.sub-filter__item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 11px;
  border: 1px solid var(--nc-line);
  border-radius: 999px;
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: inherit;
  font-size: var(--nc-text-xs);
  cursor: pointer;
}

.sub-filter__item.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-ink);
  font-weight: 700;
}

.sub-filter__count {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 700;
}

.load-error {
  margin: 0;
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.list-area {
  min-height: 220px;
  padding: 10px;
}

.list-skeleton {
  padding: 12px;
}

.data-table {
  width: 100%;
}

.person-cell {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.person-cell strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.status-cell {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
}

.row-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
}

.row-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.chip {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-xs);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.03em;
  white-space: nowrap;
}

.chip--info {
  border-color: rgba(46, 71, 56, 0.25);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.chip--studying {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.chip--graduated {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
}

.chip--noteligible {
  border-color: rgba(171, 50, 39, 0.35);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.chip--nohigher {
  border-color: rgba(150, 100, 20, 0.35);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.chip--muted {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.ellipsis {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.member-cards,
.student-cards {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 10px;
}

.member-card,
.student-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.card-person {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-line {
  margin: 6px 0 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.5;
}

.card-school {
  margin: 8px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.5;
}

.line-gap {
  display: inline-block;
  width: 12px;
}

.reason-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.card-remark {
  margin: 8px 0 0;
  padding-top: 8px;
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.card-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 10px;
}

.card-actions :deep(.el-button + .el-button) {
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

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 14px;
}

.form-hint {
  margin: -4px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

@media (max-width: 1023px) {
  .summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .summary {
    gap: 8px;
  }

  .tab-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    padding: 10px 12px;
  }

  .filter-bar {
    padding: 12px;
  }

  .filter-search {
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

  .sub-filter {
    padding: 0 12px 12px;
  }

  .list-area {
    padding: 10px;
  }

  .card-head {
    flex-direction: column;
    align-items: stretch;
  }

  .card-actions {
    justify-content: flex-start;
  }

  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }
}

@media (max-width: 420px) {
  .summary {
    grid-template-columns: 1fr;
  }
}
</style>
