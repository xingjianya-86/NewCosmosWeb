<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import {
  getRolePermissions,
  listPermissionDefinitions,
  saveRolePermissions,
} from '@/api/roles'
import NcCard from '@/components/NcCard.vue'
import type { PermissionDefinition, RoleItem } from '@/types/roles'

interface PermissionGroup {
  module: string
  items: PermissionDefinition[]
}

const props = withDefaults(
  defineProps<{
    roles: RoleItem[]
    canManage?: boolean
    loading?: boolean
  }>(),
  {
    canManage: false,
    loading: false,
  },
)

const definitions = ref<PermissionDefinition[]>([])
const selectedRoleId = ref<number | null>(null)
const checkedCodes = ref<string[]>([])

const loadingDefinitions = ref(false)
const loadingPermissions = ref(false)
const saving = ref(false)

const definitionsError = ref('')
const permissionsError = ref('')

let permissionTicket = 0

const checkedSet = computed(() => new Set(checkedCodes.value))

const selectedRole = computed<RoleItem | null>(
  () => props.roles.find((role) => role.id === selectedRoleId.value) ?? null,
)

const groups = computed<PermissionGroup[]>(() => {
  const grouped = new Map<string, PermissionDefinition[]>()

  for (const definition of definitions.value) {
    const bucket = grouped.get(definition.module) ?? []
    bucket.push(definition)
    grouped.set(definition.module, bucket)
  }

  return Array.from(grouped, ([module, items]) => ({
    module,
    items: [...items].sort((a, b) => a.sortOrder - b.sortOrder),
  })).sort((a, b) => {
    const minA = Math.min(...a.items.map((item) => item.sortOrder))
    const minB = Math.min(...b.items.map((item) => item.sortOrder))
    return minA - minB
  })
})

function isChecked(code: string): boolean {
  return checkedSet.value.has(code)
}

function checkedInGroup(group: PermissionGroup): number {
  return group.items.filter((item) => checkedSet.value.has(item.code)).length
}

function isGroupAllChecked(group: PermissionGroup): boolean {
  return group.items.length > 0 && checkedInGroup(group) === group.items.length
}

function isGroupIndeterminate(group: PermissionGroup): boolean {
  const count = checkedInGroup(group)
  return count > 0 && count < group.items.length
}

function togglePermission(code: string): void {
  if (!props.canManage) return

  const next = new Set(checkedCodes.value)
  if (next.has(code)) next.delete(code)
  else next.add(code)
  checkedCodes.value = [...next]
}

function toggleGroup(group: PermissionGroup): void {
  if (!props.canManage) return

  const next = new Set(checkedCodes.value)
  const shouldCheckAll = !isGroupAllChecked(group)

  for (const item of group.items) {
    if (shouldCheckAll) next.add(item.code)
    else next.delete(item.code)
  }

  checkedCodes.value = [...next]
}

function tooltipText(item: PermissionDefinition): string {
  return `${item.code} · ${item.description || '暂无说明'}`
}

async function loadDefinitions(): Promise<void> {
  loadingDefinitions.value = true
  definitionsError.value = ''

  try {
    definitions.value = await listPermissionDefinitions()
  } catch (error) {
    definitions.value = []
    definitionsError.value = getErrorMessage(error, '权限定义加载失败')
  } finally {
    loadingDefinitions.value = false
  }
}

async function loadRolePermissions(): Promise<void> {
  const roleId = selectedRoleId.value
  const ticket = ++permissionTicket

  if (roleId === null) {
    checkedCodes.value = []
    permissionsError.value = ''
    loadingPermissions.value = false
    return
  }

  loadingPermissions.value = true
  permissionsError.value = ''

  try {
    const result = await getRolePermissions(roleId)
    if (ticket !== permissionTicket) return
    checkedCodes.value = [...new Set(result.permissions)]
  } catch (error) {
    if (ticket !== permissionTicket) return
    checkedCodes.value = []
    permissionsError.value = getErrorMessage(error, '角色权限加载失败')
  } finally {
    if (ticket === permissionTicket) loadingPermissions.value = false
  }
}

function ensureSelectedRole(): void {
  const exists = props.roles.some((role) => role.id === selectedRoleId.value)
  if (!exists) {
    selectedRoleId.value = props.roles[0]?.id ?? null
  }
}

async function handleSave(): Promise<void> {
  const roleId = selectedRoleId.value
  if (roleId === null || !props.canManage || saving.value) return

  saving.value = true
  try {
    const result = await saveRolePermissions(roleId, checkedCodes.value)
    ElMessage.success(result.message || '权限配置已保存')
    await Promise.all([loadDefinitions(), loadRolePermissions()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存权限配置失败'))
  } finally {
    saving.value = false
  }
}

watch(selectedRoleId, (roleId) => {
  if (roleId === null) {
    checkedCodes.value = []
    permissionsError.value = ''
    loadingPermissions.value = false
    return
  }
  void loadRolePermissions()
})

watch(
  () => props.roles,
  () => {
    ensureSelectedRole()
  },
)

onMounted(() => {
  void loadDefinitions()
  ensureSelectedRole()
})
</script>

<template>
  <NcCard title="权限配置" subtitle="按角色分配系统功能权限，分组勾选后保存">
    <template #actions>
      <el-tag v-if="!canManage" size="small" effect="plain" type="info">只读</el-tag>
    </template>

    <el-skeleton v-if="loading" :rows="6" animated />

    <el-empty
      v-else-if="roles.length === 0"
      description="暂无角色数据，暂无可配置的权限"
      :image-size="80"
    />

    <div v-else class="matrix">
      <div class="role-picker">
        <span class="role-picker__label">选择角色</span>
        <el-select
          v-model="selectedRoleId"
          class="role-picker__select"
          placeholder="请选择角色"
          aria-label="选择权限配置角色"
        >
          <el-option
            v-for="role in roles"
            :key="role.id"
            :label="`${role.name}（${role.code}）`"
            :value="role.id"
          />
        </el-select>
        <el-tag v-if="selectedRole" size="small" effect="plain" type="info">
          {{ selectedRole.userCount }} 个用户
        </el-tag>
      </div>

      <el-alert
        v-if="!canManage"
        type="info"
        title="当前账号没有角色管理权限，权限配置为只读。"
        :closable="false"
        show-icon
      />

      <el-alert
        v-if="definitionsError"
        type="error"
        :title="definitionsError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadDefinitions">重新加载</el-button>
      </el-alert>

      <el-alert
        v-else-if="permissionsError"
        type="error"
        :title="permissionsError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadRolePermissions">重新加载</el-button>
      </el-alert>

      <el-skeleton v-if="loadingDefinitions" :rows="6" animated />

      <el-empty
        v-else-if="groups.length === 0 && !definitionsError"
        description="暂无可分配的权限定义"
        :image-size="80"
      />

      <div v-else-if="groups.length > 0" v-loading="loadingPermissions" class="permission-groups">
        <section v-for="group in groups" :key="group.module" class="permission-group">
          <header class="permission-group__head">
            <div class="permission-group__title">
              <h3>{{ group.module }}</h3>
              <span class="permission-group__count">
                {{ checkedInGroup(group) }} / {{ group.items.length }}
              </span>
            </div>
            <el-checkbox
              :model-value="isGroupAllChecked(group)"
              :indeterminate="isGroupIndeterminate(group)"
              :disabled="!canManage"
              @change="toggleGroup(group)"
            >
              全选
            </el-checkbox>
          </header>

          <div class="permission-list">
            <el-tooltip
              v-for="item in group.items"
              :key="item.code"
              :content="tooltipText(item)"
              placement="top"
              :show-after="350"
            >
              <el-checkbox
                :model-value="isChecked(item.code)"
                :disabled="!canManage"
                class="permission-item"
                @change="togglePermission(item.code)"
              >
                {{ item.name }}
              </el-checkbox>
            </el-tooltip>
          </div>
        </section>

        <div v-if="canManage" class="save-bar">
          <span class="save-bar__count">
            已选择 {{ checkedCodes.length }} / {{ definitions.length }} 项权限
          </span>
          <el-button
            type="primary"
            :loading="saving"
            :disabled="selectedRoleId === null || loadingPermissions || loadingDefinitions"
            @click="handleSave"
          >
            保存权限
          </el-button>
        </div>
      </div>
    </div>
  </NcCard>
</template>

<style scoped>
.matrix {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.role-picker {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.role-picker__label {
  color: var(--nc-ink-2);
  font-weight: 600;
}

.role-picker__select {
  width: min(340px, 100%);
}

.permission-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.permission-group {
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.permission-group__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 14px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
}

.permission-group__title {
  display: flex;
  align-items: baseline;
  gap: 10px;
  min-width: 0;
}

.permission-group__title h3 {
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.04em;
  color: var(--nc-ink);
}

.permission-group__count {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.permission-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 18px;
  padding: 12px 14px;
}

.permission-item {
  margin-right: 0;
}

.save-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line);
}

.save-bar__count {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

@media (max-width: 640px) {
  .role-picker__select {
    width: 100%;
  }

  .permission-group__head {
    align-items: flex-start;
    flex-direction: column;
  }

  .permission-list {
    gap: 2px 14px;
  }

  .save-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .save-bar .el-button {
    width: 100%;
  }
}
</style>
