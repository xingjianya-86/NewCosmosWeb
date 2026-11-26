<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Monitor, Plus, Search } from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import { deleteRole, listRoles } from '@/api/roles'
import {
  deleteUser,
  getMyPermissions,
  getUserOptions,
  getUserStats,
  listUsers,
  resetUserPassword,
  toggleUserStatus,
} from '@/api/users'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { dataScopeLabel } from '@/types/roles'
import type { RoleItem } from '@/types/roles'
import type { MyPermissions, UserListItem, UserOptions, UserStats } from '@/types/users'
import PermissionMatrix from './PermissionMatrix.vue'
import RoleEditorDialog from './RoleEditorDialog.vue'
import UserEditorDrawer from './UserEditorDrawer.vue'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 900px)')

const activeTab = ref('users')

/* ───────────────────────────── 页头统计 ───────────────────────────── */

const stats = ref<UserStats>({ activeUsers: 0, roleCount: 0 })
const statsLoading = ref(false)
const statsError = ref('')

const activeUsersValue = computed(() => {
  if (statsError.value) return '—'
  if (statsLoading.value) return '…'
  return stats.value.activeUsers
})

const roleCountValue = computed(() => {
  if (statsError.value) return '—'
  if (statsLoading.value) return '…'
  return stats.value.roleCount
})

const activeUsersHint = computed(() => statsError.value || '当前启用的账号数')
const roleCountHint = computed(() => statsError.value || '系统内角色总数')

/* ───────────────────────────── 基础数据 ───────────────────────────── */

const userOptions = ref<UserOptions>({ roles: [], organizations: [], positions: [] })
const optionsLoading = ref(false)
const optionsError = ref('')

const myPermissions = ref<MyPermissions | null>(null)
const permissionsLoading = ref(false)
const permissionsError = ref('')

const roles = ref<RoleItem[]>([])
const rolesLoading = ref(false)
const rolesError = ref('')

const permissionSet = computed(() => new Set(myPermissions.value?.permissions ?? []))

function hasPermission(code: string): boolean {
  if (myPermissions.value?.isSuperAdmin) return true
  return permissionSet.value.has(code)
}

const canCreateUser = computed(() => hasPermission('USER_CREATE'))
const canEditUser = computed(() => hasPermission('USER_EDIT'))
const canDeleteUser = computed(() => hasPermission('USER_DELETE'))
const canResetPassword = computed(() => hasPermission('USER_RESET_PASSWORD'))
const canManageStatus = computed(() => hasPermission('USER_MANAGE_STATUS'))
const canManageRoles = computed(() => hasPermission('ROLE_MANAGE'))
const canDeleteRoles = computed(() => hasPermission('ROLE_DELETE'))

const hasAnyUserAction = computed(
  () =>
    canEditUser.value ||
    canDeleteUser.value ||
    canResetPassword.value ||
    canManageStatus.value,
)

const editorOptions = computed<UserOptions>(() => ({
  ...userOptions.value,
  roles:
    roles.value.length > 0
      ? roles.value.map((role) => ({ id: role.id, name: role.name, code: role.code }))
      : userOptions.value.roles,
}))

const roleFilterOptions = computed(() => editorOptions.value.roles)

/* ───────────────────────────── 用户列表 ───────────────────────────── */

const users = ref<UserListItem[]>([])
const usersLoading = ref(false)
const usersError = ref('')
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(20)

const filters = reactive<{
  keyword: string
  status: 'all' | 'active' | 'inactive'
  role: string
}>({
  keyword: '',
  status: 'all',
  role: '',
})

let usersTicket = 0

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

const hasActiveFilters = computed(
  () =>
    Boolean(filters.keyword.trim()) || filters.status !== 'all' || Boolean(filters.role),
)

const userEmptyText = computed(() =>
  hasActiveFilters.value ? '没有符合条件的用户' : '暂无用户数据',
)

const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

/* ───────────────────────────── 加载 ───────────────────────────── */

async function loadStats(): Promise<void> {
  statsLoading.value = true
  statsError.value = ''

  try {
    stats.value = await getUserStats()
  } catch (error) {
    statsError.value = getErrorMessage(error, '统计数据加载失败')
  } finally {
    statsLoading.value = false
  }
}

async function loadOptions(): Promise<void> {
  optionsLoading.value = true
  optionsError.value = ''

  try {
    userOptions.value = await getUserOptions()
  } catch (error) {
    userOptions.value = { roles: [], organizations: [], positions: [] }
    optionsError.value = getErrorMessage(error, '用户选项加载失败')
  } finally {
    optionsLoading.value = false
  }
}

async function loadMyPermissions(): Promise<void> {
  permissionsLoading.value = true
  permissionsError.value = ''

  try {
    myPermissions.value = await getMyPermissions()
  } catch (error) {
    myPermissions.value = null
    permissionsError.value = getErrorMessage(error, '权限信息加载失败')
  } finally {
    permissionsLoading.value = false
  }
}

async function loadRoles(): Promise<void> {
  rolesLoading.value = true
  rolesError.value = ''

  try {
    roles.value = await listRoles()
  } catch (error) {
    roles.value = []
    rolesError.value = getErrorMessage(error, '角色列表加载失败')
  } finally {
    rolesLoading.value = false
  }
}

async function loadUsers(): Promise<void> {
  const ticket = ++usersTicket
  usersLoading.value = true
  usersError.value = ''

  try {
    const page = await listUsers({
      keyword: filters.keyword,
      status: filters.status,
      role: filters.role || undefined,
      page: pageIndex.value,
      pageSize: pageSize.value,
    })

    if (ticket !== usersTicket) return

    if (page.items.length === 0 && page.total > 0 && pageIndex.value > 1) {
      pageIndex.value = Math.max(1, Math.ceil(page.total / pageSize.value))
      void loadUsers()
      return
    }

    users.value = page.items
    total.value = page.total
  } catch (error) {
    if (ticket !== usersTicket) return
    users.value = []
    total.value = 0
    usersError.value = getErrorMessage(error, '用户列表加载失败')
  } finally {
    if (ticket === usersTicket) usersLoading.value = false
  }
}

function handleSearch(): void {
  pageIndex.value = 1
  void loadUsers()
}

function handleReset(): void {
  filters.keyword = ''
  filters.status = 'all'
  filters.role = ''
  pageIndex.value = 1
  void loadUsers()
}

function handlePageChange(page: number): void {
  pageIndex.value = page
  void loadUsers()
}

function handlePageSizeChange(size: number): void {
  pageSize.value = size
  pageIndex.value = 1
  void loadUsers()
}

/* ───────────────────────────── 用户操作 ───────────────────────────── */

const userDrawerVisible = ref(false)
const editingUser = ref<UserListItem | null>(null)

function openCreateUser(): void {
  editingUser.value = null
  userDrawerVisible.value = true
}

function openEditUser(user: UserListItem): void {
  if (!user.canBeManaged) return
  editingUser.value = user
  userDrawerVisible.value = true
}

function handleUserSaved(): void {
  void loadUsers()
  void loadStats()
}

async function handleDeleteUser(user: UserListItem): Promise<void> {
  if (!user.canBeManaged) return

  try {
    await ElMessageBox.confirm(
      `确定要删除用户「${user.username}」吗？\n\n` +
        '删除后该用户将立即停用并被强制下线（全部会话被踢出）。\n' +
        '用户资料保留为停用状态，可由管理员重新启用恢复。',
      '删除用户',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const result = await deleteUser(user.id)
    ElMessage.success(result.message || '用户已删除')
    await Promise.all([loadUsers(), loadStats()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除用户失败'))
  }
}

const resetDialog = reactive({
  visible: false,
  user: null as UserListItem | null,
  password: '',
  confirmPassword: '',
  saving: false,
})

function openResetPassword(user: UserListItem): void {
  if (!user.canBeManaged) return
  resetDialog.user = user
  resetDialog.password = ''
  resetDialog.confirmPassword = ''
  resetDialog.saving = false
  resetDialog.visible = true
}

async function handleResetPassword(): Promise<void> {
  const user = resetDialog.user
  if (!user || resetDialog.saving) return

  if (resetDialog.password.length < 6 || resetDialog.password.length > 20) {
    ElMessage.warning('密码长度须为 6–20 位')
    return
  }
  if (resetDialog.password !== resetDialog.confirmPassword) {
    ElMessage.warning('两次输入的密码不一致，请重新输入')
    return
  }

  resetDialog.saving = true
  try {
    const result = await resetUserPassword(user.id, resetDialog.password)
    ElMessage.success(result.message || '密码已重置')
    resetDialog.visible = false
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '重置密码失败'))
  } finally {
    resetDialog.saving = false
  }
}

async function handleToggleStatus(user: UserListItem): Promise<void> {
  if (!user.canBeManaged) return

  const actionText = user.isActive ? '停用' : '启用'
  const extra = user.isActive ? '\n停用后该用户的在线会话将被强制下线。' : ''

  try {
    await ElMessageBox.confirm(
      `确定要${actionText}用户「${user.username}」吗？${extra}`,
      '确认操作',
      { confirmButtonText: '确定', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const result = await toggleUserStatus(user.id, !user.isActive)
    ElMessage.success(result.message || `用户已${actionText}`)
    await Promise.all([loadUsers(), loadStats()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, `切换用户状态失败`))
  }
}

/* ───────────────────────────── 角色操作 ───────────────────────────── */

const roleDialogVisible = ref(false)
const editingRole = ref<RoleItem | null>(null)

function openCreateRole(): void {
  editingRole.value = null
  roleDialogVisible.value = true
}

function openEditRole(role: RoleItem): void {
  editingRole.value = role
  roleDialogVisible.value = true
}

function handleRoleSaved(): void {
  roleDialogVisible.value = false
  void loadRoles()
  void loadStats()
}

async function handleDeleteRole(role: RoleItem): Promise<void> {
  if (role.code === 'SUPER_ADMIN') return

  try {
    await ElMessageBox.confirm(
      `确定要删除角色「${role.name}」吗？删除后该角色将从用户授权中移除，且不可恢复。`,
      '删除角色',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const result = await deleteRole(role.id)
    ElMessage.success(result.message || '角色已删除')
    await Promise.all([loadRoles(), loadStats()])
  } catch (error) {
    const message =
      error instanceof ApiError && error.status === 409
        ? '该角色正在被用户使用，无法删除'
        : getErrorMessage(error, '删除角色失败')
    ElMessage.error(message)
  }
}

/* ───────────────────────────── 其它 ───────────────────────────── */

function goOnlineSessions(): void {
  void router.push('/system/users/online')
}

function roleNames(user: UserListItem): string {
  return user.roles.length > 0 ? user.roles.map((role) => role.name).join('、') : '—'
}

onMounted(() => {
  void Promise.all([
    loadStats(),
    loadOptions(),
    loadMyPermissions(),
    loadRoles(),
    loadUsers(),
  ])
})
</script>

<template>
  <div class="nc-page users-page">
    <NcPageHeader
      title="用户与权限管理"
      description="管理系统用户、角色与权限分配"
      kicker="系统 · 用户权限"
    >
      <template #actions>
        <el-button v-if="activeTab === 'users'" :icon="Monitor" @click="goOnlineSessions">
          在线会话
        </el-button>
        <el-button
          v-if="activeTab === 'users' && canCreateUser"
          type="primary"
          :icon="Plus"
          @click="openCreateUser"
        >
          创建用户
        </el-button>
        <el-button
          v-if="activeTab === 'roles' && canManageRoles"
          type="primary"
          :icon="Plus"
          @click="openCreateRole"
        >
          创建角色
        </el-button>
      </template>
    </NcPageHeader>

    <div class="stats-row">
      <NcStat
        label="活跃用户（人）"
        :value="activeUsersValue"
        :hint="activeUsersHint"
        tone="seal"
      />
      <NcStat
        label="角色总数（个）"
        :value="roleCountValue"
        :hint="roleCountHint"
        tone="pine"
      />
    </div>

    <el-alert
      v-if="permissionsError"
      class="page-alert"
      type="error"
      :title="permissionsError"
      :closable="false"
      show-icon
    >
      <el-button
        link
        type="primary"
        :loading="permissionsLoading"
        @click="loadMyPermissions"
      >
        重新加载
      </el-button>
    </el-alert>

    <el-alert
      v-if="optionsError"
      class="page-alert"
      type="warning"
      :title="optionsError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :loading="optionsLoading" @click="loadOptions">
        重新加载
      </el-button>
    </el-alert>

    <el-tabs v-model="activeTab" class="user-tabs">
      <!-- ── 用户管理 ── -->
      <el-tab-pane label="用户管理" name="users">
        <div class="tab-panel">
          <NcCard padding="none" class="filter-card">
            <form class="filter-bar" @submit.prevent="handleSearch">
              <el-input
                v-model="filters.keyword"
                class="filter-search"
                clearable
                :prefix-icon="Search"
                placeholder="用户名 / 姓名 / 手机号"
                aria-label="搜索用户"
                @clear="handleSearch"
              />

              <el-select
                v-model="filters.status"
                class="filter-select"
                aria-label="按状态筛选"
              >
                <el-option label="全部状态" value="all" />
                <el-option label="激活" value="active" />
                <el-option label="停用" value="inactive" />
              </el-select>

              <el-select v-model="filters.role" class="filter-select" aria-label="按角色筛选">
                <el-option label="全部角色" value="" />
                <el-option
                  v-for="role in roleFilterOptions"
                  :key="role.code"
                  :label="role.name"
                  :value="role.code"
                />
              </el-select>

              <div class="filter-actions">
                <el-button
                  type="primary"
                  :icon="Search"
                  native-type="submit"
                  :loading="usersLoading"
                >
                  搜索
                </el-button>
                <el-button native-type="button" @click="handleReset">重置</el-button>
              </div>
            </form>
          </NcCard>

          <el-alert
            v-if="usersError"
            class="page-alert"
            type="error"
            :title="usersError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadUsers">重新加载</el-button>
          </el-alert>

          <NcCard padding="none" class="list-card">
            <div class="list-body" v-loading="usersLoading && users.length > 0">
              <el-skeleton
                v-if="usersLoading && users.length === 0"
                :rows="5"
                animated
                class="list-skeleton"
              />

              <div v-else-if="!usersError && users.length === 0" class="empty-wrap">
                <el-empty :description="userEmptyText" :image-size="80">
                  <el-button
                    v-if="canCreateUser && !hasActiveFilters"
                    type="primary"
                    plain
                    :icon="Plus"
                    @click="openCreateUser"
                  >
                    创建用户
                  </el-button>
                </el-empty>
              </div>

              <template v-else-if="users.length > 0">
                <div v-if="isNarrow" class="user-cards">
                  <article v-for="user in users" :key="user.id" class="user-card">
                    <header class="user-card__head">
                      <div class="user-card__identity">
                        <strong>{{ user.username }}</strong>
                        <span>{{ user.fullName || '—' }}</span>
                      </div>
                      <el-tag
                        size="small"
                        effect="plain"
                        :type="user.isActive ? 'success' : 'info'"
                      >
                        {{ user.isActive ? '激活' : '停用' }}
                      </el-tag>
                    </header>

                    <dl class="user-card__meta">
                      <div>
                        <dt>手机号</dt>
                        <dd>{{ user.phone || '—' }}</dd>
                      </div>
                      <div>
                        <dt>所属组织</dt>
                        <dd>{{ user.organizationName || '—' }}</dd>
                      </div>
                      <div class="wide">
                        <dt>角色</dt>
                        <dd>{{ roleNames(user) }}</dd>
                      </div>
                    </dl>

                    <p v-if="!user.canBeManaged" class="user-card__locked">
                      无权管理该用户（超出数据范围）
                    </p>

                    <footer v-if="hasAnyUserAction" class="user-card__actions">
                      <el-button
                        v-if="canEditUser"
                        size="small"
                        type="primary"
                        plain
                        :disabled="!user.canBeManaged"
                        @click="openEditUser(user)"
                      >
                        编辑
                      </el-button>
                      <el-button
                        v-if="canDeleteUser"
                        size="small"
                        type="danger"
                        plain
                        :disabled="!user.canBeManaged"
                        @click="handleDeleteUser(user)"
                      >
                        删除
                      </el-button>
                      <el-button
                        v-if="canResetPassword"
                        size="small"
                        type="warning"
                        plain
                        :disabled="!user.canBeManaged"
                        @click="openResetPassword(user)"
                      >
                        重置密码
                      </el-button>
                      <el-button
                        v-if="canManageStatus"
                        size="small"
                        type="info"
                        plain
                        :disabled="!user.canBeManaged"
                        @click="handleToggleStatus(user)"
                      >
                        {{ user.isActive ? '停用' : '启用' }}
                      </el-button>
                    </footer>
                  </article>
                </div>

                <el-table v-else :data="users" row-key="id" class="user-table">
                  <el-table-column label="序号" width="64" align="center">
                    <template #default="{ $index }">
                      {{ (pageIndex - 1) * pageSize + $index + 1 }}
                    </template>
                  </el-table-column>

                  <el-table-column label="用户名" min-width="130">
                    <template #default="{ row }">
                      <span class="cell-strong">{{ row.username }}</span>
                    </template>
                  </el-table-column>

                  <el-table-column label="姓名" min-width="100">
                    <template #default="{ row }">{{ row.fullName || '—' }}</template>
                  </el-table-column>

                  <el-table-column label="手机号" min-width="126">
                    <template #default="{ row }">
                      <span class="mono">{{ row.phone || '—' }}</span>
                    </template>
                  </el-table-column>

                  <el-table-column label="所属组织" min-width="150" show-overflow-tooltip>
                    <template #default="{ row }">{{ row.organizationName || '—' }}</template>
                  </el-table-column>

                  <el-table-column label="状态" width="90" align="center">
                    <template #default="{ row }">
                      <el-tag
                        size="small"
                        effect="plain"
                        :type="row.isActive ? 'success' : 'info'"
                      >
                        {{ row.isActive ? '激活' : '停用' }}
                      </el-tag>
                    </template>
                  </el-table-column>

                  <el-table-column label="操作" width="286" align="right" fixed="right">
                    <template #default="{ row }">
                      <span v-if="!hasAnyUserAction" class="cell-muted">—</span>
                      <template v-else>
                        <el-button
                          v-if="canEditUser"
                          link
                          type="primary"
                          :disabled="!row.canBeManaged"
                          :title="row.canBeManaged ? '' : '无权管理该用户（超出数据范围）'"
                          @click="openEditUser(row as UserListItem)"
                        >
                          编辑
                        </el-button>
                        <el-button
                          v-if="canDeleteUser"
                          link
                          type="danger"
                          :disabled="!row.canBeManaged"
                          :title="row.canBeManaged ? '' : '无权管理该用户（超出数据范围）'"
                          @click="handleDeleteUser(row as UserListItem)"
                        >
                          删除
                        </el-button>
                        <el-button
                          v-if="canResetPassword"
                          link
                          type="warning"
                          :disabled="!row.canBeManaged"
                          :title="row.canBeManaged ? '' : '无权管理该用户（超出数据范围）'"
                          @click="openResetPassword(row as UserListItem)"
                        >
                          重置密码
                        </el-button>
                        <el-button
                          v-if="canManageStatus"
                          link
                          type="info"
                          :disabled="!row.canBeManaged"
                          :title="row.canBeManaged ? '' : '无权管理该用户（超出数据范围）'"
                          @click="handleToggleStatus(row as UserListItem)"
                        >
                          {{ row.isActive ? '停用' : '启用' }}
                        </el-button>
                      </template>
                    </template>
                  </el-table-column>
                </el-table>
              </template>
            </div>

            <div v-if="total > 0" class="table-footer">
              <span class="footer-note">
                第 {{ pageIndex }} / {{ totalPages }} 页 · 共 {{ total }} 条
              </span>
              <el-pagination
                :current-page="pageIndex"
                :page-size="pageSize"
                :page-sizes="[10, 20, 50, 100]"
                :total="total"
                :layout="paginationLayout"
                :pager-count="isNarrow ? 5 : 7"
                :small="isNarrow"
                background
                @current-change="handlePageChange"
                @size-change="handlePageSizeChange"
              />
            </div>
          </NcCard>
        </div>
      </el-tab-pane>

      <!-- ── 角色管理 ── -->
      <el-tab-pane label="角色管理" name="roles">
        <div class="tab-panel">
          <el-alert
            v-if="rolesError"
            class="page-alert"
            type="error"
            :title="rolesError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadRoles">重新加载</el-button>
          </el-alert>

          <NcCard
            v-else
            title="角色列表"
            subtitle="管理系统角色、等级与数据范围"
            padding="md"
          >
            <el-skeleton v-if="rolesLoading && roles.length === 0" :rows="4" animated />

            <div v-else-if="roles.length === 0" class="empty-wrap">
              <el-empty description="暂无角色数据" :image-size="80">
                <el-button
                  v-if="canManageRoles"
                  type="primary"
                  plain
                  :icon="Plus"
                  @click="openCreateRole"
                >
                  创建角色
                </el-button>
              </el-empty>
            </div>

            <div v-else v-loading="rolesLoading" class="role-grid">
              <article v-for="role in roles" :key="role.id" class="role-card">
                <header class="role-card__head">
                  <div class="role-card__identity">
                    <h3>{{ role.name }}</h3>
                    <span class="mono">{{ role.code }}</span>
                  </div>
                  <el-tag size="small" effect="plain">等级 {{ role.level }}</el-tag>
                </header>

                <p class="role-card__desc">{{ role.description || '暂无描述' }}</p>

                <div class="role-card__meta">
                  <el-tag size="small" effect="plain" type="info">
                    {{ dataScopeLabel(role.dataScope) }}
                  </el-tag>
                  <span>{{ role.userCount }} 个用户</span>
                </div>

                <footer
                  v-if="canManageRoles || canDeleteRoles"
                  class="role-card__actions"
                >
                  <el-button
                    v-if="canManageRoles"
                    size="small"
                    :icon="Edit"
                    @click="openEditRole(role)"
                  >
                    编辑
                  </el-button>
                  <el-button
                    v-if="canDeleteRoles"
                    size="small"
                    type="danger"
                    plain
                    :icon="Delete"
                    :disabled="role.code === 'SUPER_ADMIN'"
                    :title="role.code === 'SUPER_ADMIN' ? '超级管理员角色不可删除' : ''"
                    @click="handleDeleteRole(role)"
                  >
                    删除
                  </el-button>
                </footer>
              </article>
            </div>
          </NcCard>
        </div>
      </el-tab-pane>

      <!-- ── 权限配置 ── -->
      <el-tab-pane label="权限配置" name="permissions">
        <div class="tab-panel">
          <el-alert
            v-if="rolesError"
            class="page-alert"
            type="error"
            :title="rolesError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadRoles">重新加载</el-button>
          </el-alert>

          <PermissionMatrix
            :roles="roles"
            :can-manage="canManageRoles"
            :loading="rolesLoading && roles.length === 0"
          />
        </div>
      </el-tab-pane>
    </el-tabs>

    <UserEditorDrawer
      v-model="userDrawerVisible"
      :user="editingUser"
      :options="editorOptions"
      @saved="handleUserSaved"
    />

    <RoleEditorDialog
      v-model="roleDialogVisible"
      :role="editingRole"
      @saved="handleRoleSaved"
    />

    <el-dialog
      v-model="resetDialog.visible"
      title="重置密码"
      :width="isNarrow ? '94%' : '440px'"
      :close-on-click-modal="false"
      append-to-body
    >
      <p class="reset-hint">
        为用户「{{ resetDialog.user?.username || '' }}」设置新密码。
      </p>
      <el-form label-position="top" @submit.prevent="handleResetPassword">
        <el-form-item label="新密码" required>
          <el-input
            v-model="resetDialog.password"
            type="password"
            show-password
            maxlength="20"
            placeholder="请输入新密码（6–20 位）"
            :disabled="resetDialog.saving"
          />
        </el-form-item>
        <el-form-item label="确认新密码" required>
          <el-input
            v-model="resetDialog.confirmPassword"
            type="password"
            show-password
            maxlength="20"
            placeholder="请再次输入新密码"
            :disabled="resetDialog.saving"
            @keyup.enter="handleResetPassword"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button :disabled="resetDialog.saving" @click="resetDialog.visible = false">
          取消
        </el-button>
        <el-button
          type="primary"
          :loading="resetDialog.saving"
          @click="handleResetPassword"
        >
          确定重置
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.users-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.stats-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 280px));
  gap: 12px;
}

.page-alert {
  margin: 0;
}

/* ── Tabs ─────────────────────────────────────────────────────── */

.user-tabs :deep(.el-tabs__header) {
  margin-bottom: 0;
  padding: 4px 14px 0;
  border: 1px solid var(--nc-line);
  border-bottom: none;
  border-radius: var(--nc-radius-sm) var(--nc-radius-sm) 0 0;
  background-color: var(--nc-paper-2);
}

.user-tabs :deep(.el-tabs__nav-wrap::after) {
  height: 1px;
  background-color: var(--nc-line);
}

.user-tabs :deep(.el-tabs__item) {
  height: 42px;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.04em;
  color: var(--nc-muted-2);
}

.user-tabs :deep(.el-tabs__item.is-active) {
  color: var(--nc-seal);
}

.user-tabs :deep(.el-tabs__active-bar) {
  height: 2px;
  background-color: var(--nc-seal);
}

.user-tabs :deep(.el-tabs__content) {
  padding-top: 15px;
  overflow: visible;
}

.tab-panel {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

/* ── 筛选 ─────────────────────────────────────────────────────── */

.filter-card,
.list-card {
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
  flex: 1 1 240px;
  max-width: 360px;
}

.filter-select {
  width: 150px;
}

.filter-actions {
  display: flex;
  gap: 8px;
  margin-left: auto;
}

/* ── 列表 ─────────────────────────────────────────────────────── */

.list-body {
  min-height: 220px;
}

.list-skeleton {
  padding: 18px;
}

.empty-wrap {
  padding: 30px 16px;
}

.user-table {
  width: 100%;
}

.cell-strong {
  color: var(--nc-ink);
  font-weight: 700;
}

.cell-muted {
  color: var(--nc-muted);
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.user-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
}

.user-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 13px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.user-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.user-card__identity {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.user-card__identity strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.user-card__identity span {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.user-card__meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 12px;
  margin: 0;
}

.user-card__meta > div {
  min-width: 0;
}

.user-card__meta > div.wide {
  grid-column: 1 / -1;
}

.user-card__meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.user-card__meta dd {
  margin: 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.user-card__locked {
  margin: 0;
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
}

.user-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--nc-line-faint);
}

.table-footer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.footer-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

/* ── 角色 ─────────────────────────────────────────────────────── */

.role-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(290px, 1fr));
  gap: 12px;
}

.role-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 15px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.role-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.role-card__identity {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.role-card__identity h3 {
  overflow: hidden;
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.role-card__identity span {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.role-card__desc {
  min-height: 2.6em;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.role-card__meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.role-card__actions {
  display: flex;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

/* ── 重置密码 ─────────────────────────────────────────────────── */

.reset-hint {
  margin-bottom: 12px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

/* ── 窄屏 ─────────────────────────────────────────────────────── */

@media (max-width: 900px) {
  .stats-row {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 760px) {
  .filter-search,
  .filter-select {
    flex: 1 1 100%;
    width: 100%;
    max-width: none;
  }

  .filter-actions {
    width: 100%;
    margin-left: 0;
  }

  .filter-actions .el-button {
    flex: 1;
  }

  .table-footer {
    justify-content: center;
  }

  .footer-note {
    width: 100%;
    text-align: center;
  }

  .role-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 480px) {
  .stats-row {
    grid-template-columns: minmax(0, 1fr);
  }

  .user-card__meta {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
