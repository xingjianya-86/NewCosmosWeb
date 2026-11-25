<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Camera, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { recognizeTempReliefIdCard } from '@/api/tempRelief'
import type { TempReliefOcrResponse } from '@/types/tempRelief'

const MAX_OCR_BYTES = 10 * 1024 * 1024

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    applicationId?: number | null
    applicantName?: string
  }>(),
  {
    applicationId: null,
    applicantName: '',
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'recognized', result: TempReliefOcrResponse): void
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const fileInput = ref<HTMLInputElement | null>(null)
const selectedFile = ref<File | null>(null)
const previewUrl = ref('')
const recognizing = ref(false)
const result = ref<TempReliefOcrResponse | null>(null)
const errorText = ref('')

function releasePreview(): void {
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = ''
  }
}

function chooseFile(): void {
  if (recognizing.value) return
  fileInput.value?.click()
}

function handleFilePicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file) return

  if (!file.type.startsWith('image/')) {
    ElMessage.warning('仅支持图片文件')
    return
  }
  if (file.size > MAX_OCR_BYTES) {
    ElMessage.warning('身份证图片不能超过 10MB')
    return
  }

  releasePreview()
  selectedFile.value = file
  previewUrl.value = URL.createObjectURL(file)
  result.value = null
  errorText.value = ''
}

async function recognize(): Promise<void> {
  const file = selectedFile.value
  if (!file) {
    ElMessage.warning('请先选择身份证图片')
    return
  }

  recognizing.value = true
  errorText.value = ''
  try {
    const response = await recognizeTempReliefIdCard(file, {
      id: props.applicationId ?? undefined,
      category: 'ID_CARD',
    })
    result.value = response
    if (!response.name && !response.idCard && !response.address) {
      errorText.value = '识别完成，但未取到姓名、身份证号或住址，请重试或手动填写。'
    }
  } catch (error) {
    result.value = null
    errorText.value = getErrorMessage(error, '身份证识别失败')
  } finally {
    recognizing.value = false
  }
}

function applyResult(): void {
  if (!result.value) {
    ElMessage.warning('请先完成身份证识别')
    return
  }
  emit('recognized', result.value)
  visible.value = false
}

function reset(): void {
  releasePreview()
  selectedFile.value = null
  result.value = null
  errorText.value = ''
  recognizing.value = false
}

watch(
  () => props.modelValue,
  (open) => {
    if (!open) reset()
  },
)

onBeforeUnmount(releasePreview)
</script>

<template>
  <el-dialog
    v-model="visible"
    title="识别身份证"
    width="min(720px, 94vw)"
    append-to-body
    class="temp-relief-ocr"
  >
    <p class="ocr-subject">
      {{ applicantName ? `当前申请人：${applicantName}` : '识别结果将回填申请人姓名、身份证号与住址' }}
    </p>
    <p class="ocr-tip">
      仅自动回填空值；与现有内容不一致时会再次确认，不直接覆盖已修改内容。
    </p>

    <input
      ref="fileInput"
      type="file"
      accept="image/*"
      class="ocr-file"
      @change="handleFilePicked"
    />

    <div class="ocr-body">
      <div class="ocr-upload" :class="{ 'is-empty': !previewUrl }">
        <el-image v-if="previewUrl" :src="previewUrl" fit="contain" class="ocr-preview" />
        <div v-else class="ocr-placeholder">
          <el-icon :size="26"><Camera /></el-icon>
          <span>选择身份证正面照片</span>
        </div>
        <div class="ocr-upload__actions">
          <el-button size="small" :icon="Camera" :disabled="recognizing" @click="chooseFile">
            {{ selectedFile ? '重新选择' : '选择图片' }}
          </el-button>
          <span v-if="selectedFile" class="ocr-file-name">{{ selectedFile.name }}</span>
        </div>
      </div>

      <div class="ocr-result">
        <div class="ocr-result__head">
          <span>识别结果</span>
          <el-button
            size="small"
            type="primary"
            :icon="Refresh"
            :loading="recognizing"
            :disabled="!selectedFile"
            @click="recognize"
          >
            开始识别
          </el-button>
        </div>

        <el-alert
          v-if="errorText"
          class="ocr-error"
          type="error"
          :title="errorText"
          :closable="false"
          show-icon
        />

        <dl v-if="result" class="ocr-fields">
          <div>
            <dt>姓名</dt>
            <dd>{{ result.name || '—' }}</dd>
          </div>
          <div>
            <dt>身份证号</dt>
            <dd class="mono">{{ result.idCard || '—' }}</dd>
          </div>
          <div>
            <dt>性别</dt>
            <dd>{{ result.gender || '—' }}</dd>
          </div>
          <div>
            <dt>出生日期</dt>
            <dd>{{ result.birthDate?.slice(0, 10) || '—' }}</dd>
          </div>
          <div class="ocr-fields__wide">
            <dt>住址</dt>
            <dd>{{ result.address || '—' }}</dd>
          </div>
        </dl>

        <p v-else-if="!errorText" class="ocr-empty">尚未识别。选择图片后点击「开始识别」。</p>
      </div>
    </div>

    <template #footer>
      <el-button :disabled="recognizing" @click="visible = false">取消</el-button>
      <el-button type="primary" :disabled="!result" @click="applyResult">回填表单</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.ocr-subject {
  margin: 0 0 4px;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.ocr-tip {
  margin: 0 0 12px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.ocr-file {
  display: none;
}

.ocr-body {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 14px;
}

.ocr-upload {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  padding: 10px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.ocr-preview {
  width: 100%;
  height: 190px;
  background-color: #fff;
}

.ocr-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  height: 190px;
  border: 1px dashed var(--nc-line-strong);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.ocr-upload__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.ocr-file-name {
  overflow: hidden;
  max-width: 100%;
  color: var(--nc-muted);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ocr-result {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.ocr-result__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.08em;
}

.ocr-error {
  margin: 0;
}

.ocr-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 12px;
  margin: 0;
}

.ocr-fields > div {
  min-width: 0;
  padding: 7px 9px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
}

.ocr-fields__wide {
  grid-column: 1 / -1;
}

.ocr-fields dt {
  margin-bottom: 3px;
  color: var(--nc-muted);
  font-size: 11px;
}

.ocr-fields dd {
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.mono {
  font-family: var(--nc-font-mono);
}

.ocr-empty {
  margin: 0;
  padding: 18px 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

@media (max-width: 640px) {
  .ocr-body {
    grid-template-columns: minmax(0, 1fr);
  }

  .ocr-preview,
  .ocr-placeholder {
    height: 150px;
  }
}
</style>
