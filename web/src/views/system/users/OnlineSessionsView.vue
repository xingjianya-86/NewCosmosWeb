<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Refresh, SwitchButton } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { kickAllSessions, kickSession, listOnlineSessions } from '@/api/sessions'
import { getMyPermissions } from '@/api/users'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { OnlineSession } from '@/types/sessions'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const sessions = ref<OnlineSession[]>([])
const loading = ref(false)
const error = ref('')

const permissions = ref<string[]>([])
const isSuperAdmin = ref(false)
const permissionsLoaded = ref(false)
const permissionsError = ref('')

const kickingId = ref('')
const kickingAll = ref(false)

const canKick = computed(
  () =>
    permissionsLoaded.value &&
    (isSuperAdmin.value || permissions.value.includes('USER_MANAGE_STATUS')),
)

const onlineCount = computed(() => sessions.value.filter((item) => item.isOnline).length)
const hasSessions = computed(() => sessions.value.length > 0)

function userText(row: OnlineSession): string {
  return row.fullName ? `${row.fullName}（${row.username}）` : row.username
}

function deviceText(row: OnlineSession): string {
  return `${row.deviceName || '未知设备'}（${row.deviceKind || '未知端'}）`
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function formatDateTime(value: string): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours(),
  )}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

function goBack(): void {
  void router.push('/system/users')
}

async function loadPermissions(): Promise<void> {
  try {
    const data = await getMyPermissions()
    permissions.value = data.permissions ?? []
    isSuperAdmin.value = data.isSuperAdmin
    permissionsLoaded.value = true
    permissionsError.value = ''
  } catch (loadError) {
    permissionsLoaded.value = false
    permissionsError.value = getErrorMessage(loadError, '操作权限加载失败，踢下线功能已禁用')
  }
}

async function loadSessions(): Promise<void> {
  if (loading.value) return
  loading.value = true
  error.value = ''
  try {
    const data = await listOnlineSessions()
    sessions.value = data.items ?? []
  } catch (loadError) {
    error.value = getErrorMessage(loadError, '在线会话加载失败')
  } finally {
    loading.value = false
  }
}

async function kickOne(row: OnlineSession): Promise<void> {
  if (!canKick.value) {
    ElMessage.warning('当前账号缺少 USER_MANAGE_STATUS 权限，无法强制下线')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定要结束 ${userText(row)} 在「${deviceText(row)}」上的会话吗？该设备会在下一次心跳（约 60 秒内）退出登录。`,
      '强制下线',
      { confirmButtonText: '确定下线', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  kickingId.value = row.sessionId
  try {
    const result = await kickSession(row.sessionId)
    ElMessage.success(result.message || '已强制下线')
    await loadSessions()
  } catch (kickError) {
    ElMessage.error(getErrorMessage(kickError, '强制下线失败'))
  } finally {
    kickingId.value = ''
  }
}

async function kickAll(): Promise<void> {
  if (!canKick.value || !hasSessions.value) return

  try {
    await ElMessageBox.confirm(
      `确定要结束当前管理范围内的全部活动会话吗？（列表显示 ${sessions.value.length} 个）\n您自己的当前会话会保留。`,
      '全部踢下线',
      { confirmButtonText: '全部下线', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  kickingAll.value = true
  try {
    const result = await kickAllSessions()
    ElMessage.success(result.message || `已结束 ${result.count} 个会话`)
    await loadSessions()
  } catch (kickError) {
    ElMessage.error(getErrorMessage(kickError, '全部踢下线失败'))
  } finally {
    kickingAll.value = false
  }
}

onMounted(() => {
  void loadPermissions()
  void loadSessions()
})
</script>

<template>
  <div class="nc-page sessions-page">
    <NcPageHeader
      title="在线会话"
      description="强制下线约 60 秒内生效"
      kicker="系统 · 用户管理"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回用户管理</el-button>
        <el-button
          v-if="canKick"
          type="danger"
          plain
          :icon="SwitchButton"
          :loading="kickingAll"
          :disabled="!hasSessions || kickingId !== ''"
          @click="kickAll"
        >
          全部踢下线
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadSessions">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="permissionsError"
      type="warning"
      :title="permissionsError"
      :closable="false"
      show-icon
    />
    <el-alert
      v-else-if="permissionsLoaded && !canKick"
      type="info"
      title="当前账号缺少 USER_MANAGE_STATUS 权限，只能查看在线会话。"
      :closable="false"
      show-icon
    />

    <el-alert v-if="error" type="error" :title="error" :closable="false" show-icon>
      <el-button link type="primary" @click="loadSessions">重试</el-button>
    </el-alert>

    <NcCard padding="none" class="session-card">
      <div class="session-summary">
        <div>
          <strong>共 {{ sessions.length }} 个活动会话</strong>
          <span class="muted">其中 {{ onlineCount }} 个在线</span>
        </div>
        <span class="muted session-summary__hint">
          被强制下线的设备会在下一次心跳（约 60 秒内）自动退出登录。
        </span>
      </div>

      <el-table
        v-if="!isNarrow"
        v-loading="loading"
        :data="sessions"
        class="session-table"
        row-key="sessionId"
      >
        <el-table-column label="用户" min-width="150">
          <template #default="{ row }">
            <span class="cell-strong">{{ userText(row as OnlineSession) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="组织" min-width="130">
          <template #default="{ row }">
            <span>{{ row.organizationName || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="角色" min-width="110">
          <template #default="{ row }">
            <span>{{ row.roleName || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="设备" min-width="200">
          <template #default="{ row }">
            <div class="device-cell">
              <span>{{ deviceText(row as OnlineSession) }}</span>
              <small v-if="row.appVersion">版本 {{ row.appVersion }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="登录时间" min-width="160">
          <template #default="{ row }">
            <span>{{ formatDateTime(row.loginAt) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="最近活跃" min-width="160">
          <template #default="{ row }">
            <span>{{ formatDateTime(row.lastSeenAt) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="88" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isOnline ? 'success' : 'info'" size="small">
              {{ row.isOnline ? '在线' : '离线' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="112" align="center" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="canKick"
              link
              type="danger"
              :loading="kickingId === row.sessionId"
              @click="kickOne(row as OnlineSession)"
            >
              踢下线
            </el-button>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>

        <template #empty>
          <div class="empty-state">
            <el-empty description="当前没有活动会话" :image-size="80" />
            <p class="muted">各端登录后会自动出现在这里</p>
          </div>
        </template>
      </el-table>

      <div v-else v-loading="loading" class="session-cards">
        <el-empty v-if="sessions.length === 0" description="当前没有活动会话" :image-size="80">
          <p class="muted">各端登录后会自动出现在这里</p>
        </el-empty>

        <article v-for="row in sessions" :key="row.sessionId" class="session-item">
          <header class="session-item__head">
            <div class="session-item__user">
              <strong>{{ userText(row) }}</strong>
              <small>{{ row.organizationName || '未分配组织' }}</small>
            </div>
            <el-tag :type="row.isOnline ? 'success' : 'info'" size="small">
              {{ row.isOnline ? '在线' : '离线' }}
            </el-tag>
          </header>

          <dl class="session-item__fields">
            <div>
              <dt>角色</dt>
              <dd>{{ row.roleName || '—' }}</dd>
            </div>
            <div>
              <dt>设备</dt>
              <dd>{{ deviceText(row) }}</dd>
            </div>
            <div>
              <dt>登录时间</dt>
              <dd>{{ formatDateTime(row.loginAt) }}</dd>
            </div>
            <div>
              <dt>最近活跃</dt>
              <dd>{{ formatDateTime(row.lastSeenAt) }}</dd>
            </div>
          </dl>

          <div v-if="canKick" class="session-item__actions">
            <el-button
              type="danger"
              plain
              :loading="kickingId === row.sessionId"
              @click="kickOne(row)"
            >
              踢下线
            </el-button>
          </div>
        </article>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.sessions-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.session-card {
  overflow: hidden;
}

.session-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 16px;
  padding: 14px 18px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.session-summary strong {
  margin-right: 10px;
  color: var(--nc-ink);
}

.session-summary__hint {
  font-size: var(--nc-text-xs);
}

.session-table {
  width: 100%;
}

.cell-strong {
  font-weight: 600;
  color: var(--nc-ink);
}

.device-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.device-cell span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.device-cell small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.empty-state {
  padding: 24px 0;
}

.empty-state .muted {
  margin-top: -6px;
  text-align: center;
}

.session-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
}

.session-item {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.session-item__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.session-item__user {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.session-item__user strong {
  color: var(--nc-ink);
}

.session-item__user small {
  color: var(--nc-muted);
}

.session-item__fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 12px;
  margin: 0;
}

.session-item__fields div {
  min-width: 0;
}

.session-item__fields dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.session-item__fields dd {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  overflow-wrap: anywhere;
}

.session-item__actions {
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 767px) {
  .session-summary {
    padding: 12px 14px;
  }

  .session-item__fields {
    grid-template-columns: 1fr;
  }
}
</style>
