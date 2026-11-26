<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { createUser, updateUser } from '@/api/users'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { UserListItem, UserOptions, UserRoleRef } from '@/types/users'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    user: UserListItem | null
    options: UserOptions
  }>(),
  {
    user: null,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  saved: []
}>()

const isNarrow = useMediaQuery('(max-width: 720px)')

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const isEdit = computed(() => props.user !== null)
const title = computed(() => (isEdit.value ? '编辑用户' : '新建用户'))

const saving = ref(false)
const roleToAdd = ref<number | null>(null)
const selectedRoles = ref<UserRoleRef[]>([])

const form = reactive({
  username: '',
  password: '',
  fullName: '',
  phone: '',
  identityCard: '',
  position: '',
  organizationId: null as number | null,
  isActive: true,
})

const positionOptions = computed<string[]>(() => {
  const list = [...props.options.positions]
  const current = props.user?.position
  if (current && !list.includes(current)) list.unshift(current)
  return list
})

const organizationOptions = computed(() => {
  const list = [...props.options.organizations]
  const user = props.user
  if (
    user?.organizationId !== null &&
    user?.organizationId !== undefined &&
    !list.some((item) => item.id === user.organizationId)
  ) {
    list.unshift({
      id: user.organizationId,
      name: user.organizationName || `组织 #${user.organizationId}`,
    })
  }
  return list
})

const availableRoles = computed<UserRoleRef[]>(() => {
  const selectedIds = new Set(selectedRoles.value.map((role) => role.id))
  return props.options.roles.filter((role) => !selectedIds.has(role.id))
})

function resetForm(): void {
  const user = props.user
  form.username = user?.username ?? ''
  form.password = ''
  form.fullName = user?.fullName ?? ''
  form.phone = user?.phone ?? ''
  form.identityCard = user?.identityCard ?? ''
  form.position = user?.position ?? ''
  form.organizationId = user?.organizationId ?? null
  form.isActive = user?.isActive ?? true
  selectedRoles.value = user ? [...user.roles] : []
  roleToAdd.value = null
}

function handleAddRole(): void {
  const roleId = roleToAdd.value
  if (typeof roleId !== 'number') {
    ElMessage.warning('请选择要添加的角色')
    return
  }

  const role = props.options.roles.find((item) => item.id === roleId)
  if (!role) {
    ElMessage.warning('角色不存在，请刷新后重试')
    return
  }
  if (selectedRoles.value.some((item) => item.id === roleId)) {
    ElMessage.warning('该用户已拥有此角色')
    return
  }

  selectedRoles.value = [...selectedRoles.value, role]
  roleToAdd.value = null
}

function handleRemoveRole(roleId: number): void {
  selectedRoles.value = selectedRoles.value.filter((role) => role.id !== roleId)
}

async function handleSave(): Promise<void> {
  if (saving.value) return

  const username = form.username.trim()
  const password = form.password
  const fullName = form.fullName.trim()
  const phone = form.phone.trim()
  const identityCard = form.identityCard.trim()
  const roleIds = selectedRoles.value.map((role) => role.id)
  const organizationId =
    typeof form.organizationId === 'number' && Number.isFinite(form.organizationId)
      ? form.organizationId
      : null

  if (!isEdit.value) {
    if (!username) {
      ElMessage.warning('请输入用户名')
      return
    }
    if (!password) {
      ElMessage.warning('请输入密码')
      return
    }
    if (password.length < 6 || password.length > 20) {
      ElMessage.warning('密码长度须为 6–20 位')
      return
    }
  }

  if (!fullName) {
    ElMessage.warning('请输入姓名')
    return
  }
  if (!phone) {
    ElMessage.warning('请输入手机号')
    return
  }
  if (identityCard && !/^\d{17}[\dXx]$/.test(identityCard)) {
    ElMessage.warning('身份证号应为 18 位')
    return
  }

  saving.value = true
  try {
    if (props.user) {
      const result = await updateUser(props.user.id, {
        fullName,
        phone,
        identityCard,
        position: form.position,
        organizationId,
        isActive: form.isActive,
        roleIds,
      })
      ElMessage.success(result.message || '用户已更新')
    } else {
      const result = await createUser({
        username,
        password,
        fullName,
        phone,
        identityCard,
        position: form.position,
        organizationId,
        roleIds,
      })
      ElMessage.success(result.message || '用户已创建')
    }
    emit('saved')
    visible.value = false
  } catch (error) {
    ElMessage.error(getErrorMessage(error, isEdit.value ? '更新用户失败' : '创建用户失败'))
  } finally {
    saving.value = false
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) resetForm()
  },
)
</script>

<template>
  <el-drawer
    v-model="visible"
    :size="isNarrow ? '94%' : '520px'"
    :close-on-click-modal="false"
    append-to-body
  >
    <template #header>
      <div class="drawer-head">
        <p class="drawer-kicker">SYSTEM / USER</p>
        <h2 class="drawer-title">{{ title }}</h2>
      </div>
    </template>

    <el-form label-position="top" class="user-form" @submit.prevent="handleSave">
      <el-form-item label="用户名" :required="!isEdit">
        <el-input
          v-model="form.username"
          maxlength="50"
          placeholder="请输入用户名"
          :disabled="isEdit || saving"
        />
        <p v-if="isEdit" class="form-hint">用户名创建后不可修改。</p>
      </el-form-item>

      <el-form-item v-if="!isEdit" label="密码" required>
        <el-input
          v-model="form.password"
          type="password"
          maxlength="20"
          show-password
          placeholder="请输入密码（6–20 位）"
          :disabled="saving"
        />
      </el-form-item>

      <el-form-item label="姓名" required>
        <el-input
          v-model="form.fullName"
          maxlength="50"
          placeholder="请输入姓名"
          :disabled="saving"
        />
      </el-form-item>

      <el-form-item label="手机号" required>
        <el-input
          v-model="form.phone"
          maxlength="20"
          placeholder="请输入手机号"
          :disabled="saving"
        />
        <p class="form-hint">手机号为必填项，用于登录找回与通知。</p>
      </el-form-item>

      <el-form-item label="身份证号">
        <el-input
          v-model="form.identityCard"
          maxlength="18"
          placeholder="选填，18 位身份证号"
          :disabled="saving"
        />
      </el-form-item>

      <el-form-item label="职位">
        <el-select
          v-model="form.position"
          class="full-width"
          clearable
          filterable
          placeholder="请选择职位"
          :disabled="saving"
        >
          <el-option
            v-for="position in positionOptions"
            :key="position"
            :label="position"
            :value="position"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="所属组织">
        <el-select
          v-model="form.organizationId"
          class="full-width"
          clearable
          filterable
          placeholder="请选择所属组织"
          :disabled="saving"
        >
          <el-option
            v-for="organization in organizationOptions"
            :key="organization.id"
            :label="organization.name"
            :value="organization.id"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="状态">
        <el-switch
          v-model="form.isActive"
          :disabled="!isEdit || saving"
          active-text="激活"
          inactive-text="停用"
        />
        <p v-if="!isEdit" class="form-hint">新建用户默认启用，创建后可在用户列表中启停。</p>
      </el-form-item>

      <el-form-item label="分配角色">
        <div class="role-box">
          <div v-if="selectedRoles.length > 0" class="role-tags">
            <el-tag
              v-for="role in selectedRoles"
              :key="role.id"
              effect="plain"
              closable
              :disable-transitions="false"
              @close="handleRemoveRole(role.id)"
            >
              {{ role.name }}
            </el-tag>
          </div>
          <p v-else class="role-empty">暂未分配角色</p>

          <div v-if="availableRoles.length > 0" class="role-add">
            <el-select
              v-model="roleToAdd"
              class="role-add__select"
              filterable
              placeholder="选择角色"
              :disabled="saving"
            >
              <el-option
                v-for="role in availableRoles"
                :key="role.id"
                :label="`${role.name}（${role.code}）`"
                :value="role.id"
              />
            </el-select>
            <el-button :disabled="saving || roleToAdd === null" @click="handleAddRole">
              添加
            </el-button>
          </div>
          <p v-else-if="options.roles.length === 0" class="form-hint">
            暂无可分配角色，请确认角色数据已加载。
          </p>
          <p v-else class="form-hint">已分配全部可用角色。</p>
        </div>
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button :disabled="saving" @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.drawer-head {
  min-width: 0;
}

.drawer-kicker {
  margin-bottom: 4px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.18em;
}

.drawer-title {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.04em;
  color: var(--nc-ink);
}

.user-form {
  padding-bottom: 8px;
}

.full-width {
  width: 100%;
}

.form-hint {
  margin: 4px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.role-box {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
}

.role-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.role-empty {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.role-add {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}

.role-add__select {
  flex: 1;
  min-width: 0;
}

@media (max-width: 560px) {
  .role-add {
    flex-direction: column;
  }

  .role-add .el-button {
    width: 100%;
  }
}
</style>
