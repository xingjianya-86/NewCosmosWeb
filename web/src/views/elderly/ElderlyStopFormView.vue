<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Refresh } from '@element-plus/icons-vue'
import {
  getElderlyApplication,
  getElderlyImportedByIdCard,
  stopElderlyApplication,
  stopElderlyFromHistory,
} from '@/api/elderly'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { ElderlyStopRequest } from '@/types/elderly'
import { formatAge, formatMonth } from './display'

const route = useRoute()
const router = useRouter()

const stopReasonOptions = [
  { value: 'DECEASED', label: '去世' },
  { value: 'MOVED_OUT', label: '户籍迁出本县' },
  { value: 'QUALIFICATION_LOST', label: '身份资格取消' },
  { value: 'CERTIFICATION_FAIL', label: '不符合每月认证要求' },
  { value: 'OTHER', label: '其他' },
]

const today = () => {
  const date = new Date()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${m}-${d}`
}

const importedMode = computed(() => route.name === 'elderly-stop-history')
const applicationId = computed(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  return value ? String(value) : ''
})
const historyId = computed(() => {
  const raw = route.params.historyId
  const value = Array.isArray(raw) ? raw[0] : raw
  return value ? String(value) : ''
})

const loading = ref(false)
const saving = ref(false)
const loadError = ref('')
const candidate = reactive({
  name: '',
  idCard: '',
  gender: '',
  age: null as number | null,
  applicationNo: '',
  categoryText: '',
  issueStartMonth: '',
  issueAmount: 0,
  sourceText: '',
})

const form = reactive({
  dueStopDate: today(),
  stopReason: '',
  deathDate: '',
  otherReasonText: '',
  isRecover: false,
  recoverStartMonth: '',
  recoverEndMonth: '',
  recoverAmount: 0,
  remark: '',
})

const title = computed(() => (importedMode.value ? '高龄津贴建档并停发' : '高龄津贴停发办理'))
const isDeceased = computed(() => form.stopReason === 'DECEASED')
const isOther = computed(() => form.stopReason === 'OTHER')

async function load(): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    if (importedMode.value) {
      const idCard = readQuery('idCard')
      if (!idCard) {
        loadError.value = '缺少身份证号，无法加载导入名册人员'
        return
      }

      const item = await getElderlyImportedByIdCard(idCard)
      if (!item) {
        loadError.value = '导入名册中未找到该人员'
        return
      }

      candidate.name = item.name
      candidate.idCard = item.idCard
      candidate.gender = item.gender || ''
      candidate.age = item.age
      candidate.categoryText = item.originalType || item.personType || readQuery('category')
      candidate.sourceText = `历史导入 (${item.dataYear ?? 0}年)`
      return
    }

    if (!applicationId.value) {
      loadError.value = '缺少登记编号'
      return
    }

    const detail = await getElderlyApplication(applicationId.value)
    candidate.name = detail.name
    candidate.idCard = detail.idCard
    candidate.gender = detail.gender
    candidate.age = detail.age
    candidate.applicationNo = detail.applicationNo
    candidate.categoryText = detail.categoryText
    candidate.issueStartMonth = detail.issueStartMonth
    candidate.issueAmount = detail.issueAmount
    candidate.sourceText = '当前库'
    if (detail.status !== 'Confirmed') {
      loadError.value = '只有已确认（在享）人员才能停发'
    }
  } catch (error) {
    loadError.value = getErrorMessage(error, '停发人员信息加载失败')
  } finally {
    loading.value = false
  }
}

function readQuery(key: string): string {
  const raw = route.query[key]
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' ? value : ''
}

function validate(): string | null {
  if (!form.stopReason) return '请选择停发原因'
  if (isDeceased.value && !form.deathDate) return '请选择死亡日期'
  if (isOther.value && !form.otherReasonText.trim()) return '请填写“其他”停发原因说明'
  if (form.isRecover) {
    if (!form.recoverStartMonth) return '请选择追缴起算月'
    if (!form.recoverEndMonth) return '请选择追缴止算月'
    if (form.recoverStartMonth > form.recoverEndMonth) return '追缴止算月不能早于起算月'
    if (form.recoverAmount < 0) return '追缴金额不能为负数'
  }
  return null
}

function buildRequest(): ElderlyStopRequest {
  return {
    dueStopDate: form.dueStopDate || null,
    stopReason: form.stopReason,
    deathDate: isDeceased.value ? form.deathDate || null : null,
    otherReasonText: isOther.value ? form.otherReasonText.trim() : '',
    isRecover: form.isRecover,
    recoverStartMonth: form.isRecover ? form.recoverStartMonth : '',
    recoverEndMonth: form.isRecover ? form.recoverEndMonth : '',
    recoverAmount: form.isRecover ? form.recoverAmount : 0,
    remark: form.remark.trim(),
  }
}

async function submit(): Promise<void> {
  if (loadError.value) return
  const validation = validate()
  if (validation) {
    ElMessage.warning(validation)
    return
  }

  saving.value = true
  try {
    if (importedMode.value) {
      await stopElderlyFromHistory(historyId.value, buildRequest())
    } else {
      await stopElderlyApplication(applicationId.value, buildRequest())
    }

    ElMessage.success('停发成功')
    await router.push({ name: 'elderly-stops' })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '停发失败'))
  } finally {
    saving.value = false
  }
}

function cancel(): void {
  void router.push({ name: 'elderly-stops' })
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page elderly-stop-form-page">
    <NcPageHeader :title="title" description="停发信息与追缴信息">
      <template #actions>
        <el-button :icon="ArrowLeft" @click="cancel">返回列表</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    />

    <NcCard v-if="loading" padding="lg">
      <el-skeleton :rows="6" animated />
    </NcCard>

    <template v-else>
      <NcCard title="人员信息">
        <div class="info-grid">
          <div><span>姓名</span><strong>{{ candidate.name || '—' }}</strong></div>
          <div><span>性别</span><strong>{{ candidate.gender || '—' }}</strong></div>
          <div><span>年龄</span><strong>{{ formatAge(candidate.age) }}</strong></div>
          <div><span>申请编号</span><strong class="mono">{{ candidate.applicationNo || '--' }}</strong></div>
          <div><span>身份证号码</span><strong class="mono">{{ candidate.idCard || '—' }}</strong></div>
          <div><span>享受类别</span><strong>{{ candidate.categoryText || '—' }}</strong></div>
          <div><span>计发年月</span><strong>{{ formatMonth(candidate.issueStartMonth) }}</strong></div>
          <div><span>计发金额</span><strong>{{ candidate.issueAmount.toFixed(2) }} 元/月</strong></div>
        </div>
        <p class="source-note">来源：{{ candidate.sourceText }}</p>
      </NcCard>

      <NcCard title="停发信息">
        <div class="form-grid">
          <label class="field">
            <span class="field-label">应停发时间 *</span>
            <el-date-picker
              v-model="form.dueStopDate"
              type="date"
              value-format="YYYY-MM-DD"
              style="width: 100%"
            />
          </label>

          <label class="field">
            <span class="field-label">停发原因 *</span>
            <el-select v-model="form.stopReason" placeholder="请选择停发原因">
              <el-option
                v-for="option in stopReasonOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
          </label>

          <label v-if="isDeceased" class="field">
            <span class="field-label">死亡日期 *</span>
            <el-date-picker
              v-model="form.deathDate"
              type="date"
              value-format="YYYY-MM-DD"
              style="width: 100%"
            />
          </label>

          <label v-if="isOther" class="field field--wide">
            <span class="field-label">其他原因说明 *</span>
            <el-input v-model="form.otherReasonText" type="textarea" :rows="2" />
          </label>
        </div>
      </NcCard>

      <NcCard title="追缴信息">
        <p class="section-note">需要追缴（多发的情况才需填写）</p>
        <el-checkbox v-model="form.isRecover">需要追缴</el-checkbox>

        <div v-if="form.isRecover" class="form-grid recover-grid">
          <label class="field">
            <span class="field-label">追缴起算月 *</span>
            <el-date-picker
              v-model="form.recoverStartMonth"
              type="month"
              value-format="YYYY-MM"
              style="width: 100%"
            />
          </label>
          <label class="field">
            <span class="field-label">追缴止算月 *</span>
            <el-date-picker
              v-model="form.recoverEndMonth"
              type="month"
              value-format="YYYY-MM"
              style="width: 100%"
            />
          </label>
          <label class="field">
            <span class="field-label">追缴金额（元）</span>
            <el-input-number
              v-model="form.recoverAmount"
              :min="0"
              :precision="2"
              :step="10"
              controls-position="right"
              style="width: 100%"
            />
          </label>
        </div>

        <label class="field remark-field">
          <span class="field-label">备注</span>
          <el-input v-model="form.remark" type="textarea" :rows="3" placeholder="备注说明..." />
        </label>
      </NcCard>

      <div class="bottom-bar">
        <p class="bottom-note">停发后实际停发时间取系统当前时间，自动生成。取消返回列表。</p>
        <div class="bottom-actions">
          <el-button type="danger" :loading="saving" @click="submit">确认停发</el-button>
          <el-button @click="cancel">取消</el-button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.elderly-stop-form-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.load-error {
  margin: 0;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px 16px;
}

.info-grid > div {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.info-grid span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.info-grid strong {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-base);
  font-weight: 600;
  overflow-wrap: anywhere;
}

.source-note,
.section-note {
  margin: 12px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 16px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field--wide {
  grid-column: span 2;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.recover-grid {
  margin-top: 12px;
}

.remark-field {
  margin-top: 14px;
}

.bottom-bar {
  position: sticky;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  box-shadow: 0 -2px 8px rgba(0, 0, 0, 0.06);
}

.bottom-note {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.bottom-actions {
  display: flex;
  gap: 10px;
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 960px) {
  .info-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 700px) {
  .info-grid,
  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: auto;
  }

  .bottom-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .bottom-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
