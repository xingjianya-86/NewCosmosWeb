<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Camera, Document, Finished, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { completeTempReliefArchive, getTempReliefApplication } from '@/api/tempRelief'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { TempReliefDetail } from '@/types/tempRelief'
import TempReliefPhotoPanel from './TempReliefPhotoPanel.vue'
import TempReliefPrintDialog from './TempReliefPrintDialog.vue'

const route = useRoute()
const router = useRouter()

const detail = ref<TempReliefDetail | null>(null)
const loading = ref(false)
const loadError = ref('')
const printVisible = ref(false)
const photoVisible = ref(false)
const archiving = ref(false)

const applicationId = computed<number | null>(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
})

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text.split('-').join('/') : '—'
}

function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return `${value.toFixed(2)} 元`
}

function backToList(): void {
  void router.push('/social-assistance/temp-relief')
}

function openPrint(): void {
  if (applicationId.value === null) {
    ElMessage.warning('缺少档案编号，无法打印')
    return
  }
  printVisible.value = true
}

async function load(): Promise<void> {
  if (applicationId.value === null) {
    loadError.value = '缺少档案编号'
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    detail.value = await getTempReliefApplication(applicationId.value)
  } catch (error) {
    detail.value = null
    loadError.value = getErrorMessage(error, '临时救助档案加载失败')
  } finally {
    loading.value = false
  }
}

async function completeArchive(): Promise<void> {
  const id = applicationId.value
  if (id === null || archiving.value) return

  const no = detail.value?.applicationNo ? `[${detail.value.applicationNo}] ` : ''
  const name = detail.value?.applicantName ?? ''
  try {
    await ElMessageBox.confirm(
      `确定将 ${no}${name} 的临时救助档案标记为完成归档吗？完成后返回列表。`,
      '完成归档',
      {
        confirmButtonText: '完成归档',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  archiving.value = true
  try {
    await completeTempReliefArchive(id)
    ElMessage.success('已完成归档')
    await router.push('/social-assistance/temp-relief')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '完成归档失败'))
  } finally {
    archiving.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page temp-relief-output">
    <NcPageHeader
      title="临时救助档案输出"
      description="打印或补打审批表，核对证件材料照片；确认无误后完成归档并返回列表。"
      kicker="低收入人口救助帮扶 · 临时救助"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToList">返回列表</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load">重新加载</el-button>
    </el-alert>

    <NcCard v-if="loading" padding="lg">
      <el-skeleton :rows="5" animated />
    </NcCard>

    <template v-else-if="detail">
      <NcCard title="申请信息" subtitle="档案编号、申请人与救助类型以服务端返回为准。">
        <div class="summary-grid">
          <div class="summary-item">
            <span class="summary-label">申请编号</span>
            <strong class="summary-value mono">{{ detail.applicationNo || '—' }}</strong>
          </div>
          <div class="summary-item">
            <span class="summary-label">申请人</span>
            <strong class="summary-value">{{ detail.applicantName || '—' }}</strong>
          </div>
          <div class="summary-item">
            <span class="summary-label">身份证号</span>
            <strong class="summary-value mono">{{ detail.applicantIdCard || '—' }}</strong>
          </div>
          <div class="summary-item">
            <span class="summary-label">救助类型</span>
            <strong class="summary-value">{{ detail.categoryDisplayName || detail.reliefTypeText || '—' }}</strong>
          </div>
          <div class="summary-item">
            <span class="summary-label">当前状态</span>
            <span class="summary-value">
              <el-tag size="small" effect="plain">{{ detail.statusText || '—' }}</el-tag>
            </span>
          </div>
          <div class="summary-item">
            <span class="summary-label">申请日期</span>
            <strong class="summary-value">{{ formatDate(detail.applyDate) }}</strong>
          </div>
          <div class="summary-item">
            <span class="summary-label">确认金额</span>
            <strong class="summary-value">{{ formatMoney(detail.confirmAmount) }}</strong>
          </div>
          <div class="summary-item">
            <span class="summary-label">填报单位</span>
            <strong class="summary-value">{{ detail.reportUnit || '—' }}</strong>
          </div>
        </div>

        <div class="output-actions">
          <el-button type="primary" :icon="Document" @click="openPrint">
            打印 / 补打
          </el-button>
          <el-button :icon="Camera" @click="photoVisible = true">
            证件 / 材料照片
          </el-button>
          <el-button
            type="success"
            :icon="Finished"
            :loading="archiving"
            @click="completeArchive"
          >
            完成归档
          </el-button>
        </div>

        <p class="section-note">
          打印与补打共用同一份服务端打印数据；照片为只读查看。完成归档后档案返回列表管理。
        </p>
      </NcCard>
    </template>

    <TempReliefPrintDialog
      v-model="printVisible"
      :application-id="applicationId"
      :application-no="detail?.applicationNo ?? ''"
      :applicant-name="detail?.applicantName ?? ''"
      :relief-type="detail?.reliefType"
      mode="print"
    />

    <TempReliefPhotoPanel
      v-model="photoVisible"
      :application-id="applicationId"
      :applicant-name="detail?.applicantName ?? ''"
      :editable="false"
    />
  </div>
</template>

<style scoped>
.temp-relief-output {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.load-error {
  margin: 0;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px 14px;
}

.summary-item {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
}

.summary-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.summary-value {
  overflow-wrap: anywhere;
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 600;
}

.output-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 18px;
  padding-top: 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.section-note {
  margin: 12px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 1200px) {
  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 700px) {
  .summary-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .output-actions {
    flex-direction: column;
  }

  .output-actions :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }
}
</style>
