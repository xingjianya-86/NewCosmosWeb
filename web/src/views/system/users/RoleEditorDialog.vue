<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { createRole, updateRole } from '@/api/roles'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { DATA_SCOPE_OPTIONS } from '@/types/roles'
import type { RoleItem, RoleSaveInput } from '@/types/roles'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    role: RoleItem | null
  }>(),
  {
    role: null,
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

const isEdit = computed(() => props.role !== null)
const title = computed(() => (isEdit.value ? '编辑角色' : '创建角色'))

const saving = ref(false)

const form = reactive({
  name: '',
  code: '',
  description: '',
  level: 10,
  dataScope: 'SELF',
})

function initForm(): void {
  const role = props.role
  form.name = role?.name ?? ''
  form.code = role?.code ?? ''
  form.description = role?.description ?? ''
  form.level = role?.level ?? 10
  form.dataScope = role?.dataScope || 'SELF'
}

function handleCodeInput(value: string): void {
  form.code = value.toUpperCase()
}

async function handleSave(): Promise<void> {
  if (saving.value) return

  const name = form.name.trim()
  const code = form.code.trim().toUpperCase()

  if (!name) {
    ElMessage.warning('请输入角色名称')
    return
  }
  if (!isEdit.value && !code) {
    ElMessage.warning('请输入角色代码')
    return
  }
  if (!Number.isFinite(form.level) || form.level < 0) {
    ElMessage.warning('请输入有效的角色等级')
    return
  }
  if (!DATA_SCOPE_OPTIONS.some((option) => option.value === form.dataScope)) {
    ElMessage.warning('请选择数据范围')
    return
  }

  const payload: RoleSaveInput = {
    name,
    description: form.description.trim(),
    level: form.level,
    dataScope: form.dataScope,
  }

  saving.value = true
  try {
    if (props.role) {
      const result = await updateRole(props.role.id, payload)
      ElMessage.success(result.message || '角色已更新')
    } else {
      payload.code = code
      const result = await createRole(payload)
      ElMessage.success(result.message || '角色已创建')
    }
    emit('saved')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, isEdit.value ? '更新角色失败' : '创建角色失败'))
  } finally {
    saving.value = false
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) initForm()
  },
)
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    :width="isNarrow ? '94%' : '540px'"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-form label-position="top" @submit.prevent="handleSave">
      <el-form-item label="角色名称" required>
        <el-input
          v-model="form.name"
          maxlength="50"
          placeholder="如 乡镇级管理员"
          :disabled="saving"
        />
      </el-form-item>

      <el-form-item label="角色代码" :required="!isEdit">
        <el-input
          :model-value="form.code"
          maxlength="50"
          placeholder="如 TOWN_ADMIN（自动转为大写）"
          :disabled="isEdit || saving"
          @input="handleCodeInput"
        />
        <p class="form-hint">
          {{ isEdit ? '角色代码创建后不可修改。' : '仅使用大写字母、数字与下划线，保存时自动转大写。' }}
        </p>
      </el-form-item>

      <el-form-item label="描述">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="3"
          maxlength="500"
          show-word-limit
          placeholder="选填，说明该角色的职责范围"
          :disabled="saving"
        />
      </el-form-item>

      <div class="form-grid">
        <el-form-item label="等级">
          <el-input-number
            v-model="form.level"
            :min="0"
            :max="999"
            :step="1"
            controls-position="right"
            class="full-width"
            :disabled="saving"
          />
        </el-form-item>

        <el-form-item label="数据范围" required>
          <el-select v-model="form.dataScope" class="full-width" :disabled="saving">
            <el-option
              v-for="option in DATA_SCOPE_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
      </div>

      <p class="form-hint">
        等级数值越小权限越高；数据范围决定该角色可访问的业务数据边界。
      </p>
    </el-form>

    <template #footer>
      <el-button :disabled="saving" @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 14px;
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

@media (max-width: 640px) {
  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
