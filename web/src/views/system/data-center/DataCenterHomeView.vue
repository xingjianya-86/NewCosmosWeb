<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Coin,
  DataAnalysis,
  Download,
  OfficeBuilding,
  Refresh,
  TrendCharts,
  Upload,
  Warning,
} from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import {
  cleanupLogs,
  cleanupSoftDeleted,
  detectSoftDelete,
  downloadBackup,
  fixSchema,
  getDataCenterOverview,
  getSchemaStatus,
  initializeSchema,
  listBackups,
  listClientVersions,
  restoreBackup,
  startBackup,
  uploadBackup,
} from '@/api/dataCenter'
import { getErrorMessage } from '@/api/http'
import { saveBlob } from '@/views/photos/photoMeta'
import type {
  BackupFile,
  ClientVersionList,
  DataCenterJob,
  DataCenterOverview,
  SchemaStatus,
  SoftDeleteCensus,
} from '@/types/dataCenter'
import JobProgressDialog from './components/JobProgressDialog.vue'

const router = useRouter()

const overview = ref<DataCenterOverview | null>(null)
const schema = ref<SchemaStatus | null>(null)
const backups = ref<BackupFile[]>([])
const clients = ref<ClientVersionList | null>(null)

const loading = reactive({
  overview: false,
  schema: false,
  backups: false,
  clients: false,
})
const errors = reactive({
  overview: '',
  schema: '',
  backups: '',
  clients: '',
})

const showAllTables = ref(false)
const visibleTables = computed(() =>
  showAllTables.value ? overview.value?.tables ?? [] : (overview.value?.tables ?? []).slice(0, 10),
)

const bus = reactive({
  jobVisible: false,
  jobId: '',
  jobTitle: '',
  afterFinish: null as null | ((job: DataCenterJob) => void),
})
const uploadInput = ref<HTMLInputElement | null>(null)
const uploading = ref(false)

const restore = reactive({
  visible: false,
  file: null as BackupFile | null,
  confirmText: '',
  submitting: false,
})
const softDelete = reactive({
  visible: false,
  loading: false,
  data: null as SoftDeleteCensus | null,
  cleaning: false,
})

const modules = [
  {
    title: '数据导入',
    subtitle: '导入 Excel 数据文件',
    description: '家庭/人员双文件自动匹配，预览后导入，支持模板与错误日志。',
    icon: Upload,
    tone: 'amber',
    to: '/system/data-center/import',
  },
  {
    title: '银行流水核对',
    subtitle: '查看核对结果与标记人员',
    description: '按批次、姓名、身份证检索，超过 30 月标准与证件存疑单独标记。',
    icon: Coin,
    tone: 'blue',
    to: '/system/data-center/bank-flow',
  },
  {
    title: '数据字典管理',
    subtitle: '管理系统数据字典',
    description: '分类与字典项增删改，支持同步表结构与从种子初始化。',
    icon: DataAnalysis,
    tone: 'green',
    to: '/system/data-center/dictionary',
  },
  {
    title: '标准配置管理',
    subtitle: '管理政策标准配置',
    description: '低保、特困、刚性支出等标准值按户口类型与供养方式维护。',
    icon: TrendCharts,
    tone: 'purple',
    to: '/system/data-center/standards',
  },
  {
    title: '行政区划管理',
    subtitle: '管理省市区镇村数据',
    description: '省市县镇村五级级联维护，支持新增、编辑、删除与种子初始化。',
    icon: OfficeBuilding,
    tone: 'teal',
    to: '/system/data-center/regions',
  },
]

async function loadOverview(): Promise<void> {
  loading.overview = true
  errors.overview = ''
  try {
    overview.value = await getDataCenterOverview()
  } catch (error) {
    errors.overview = getErrorMessage(error, '数据库状态加载失败')
  } finally {
    loading.overview = false
  }
}

async function loadSchema(): Promise<void> {
  loading.schema = true
  errors.schema = ''
  try {
    schema.value = await getSchemaStatus()
  } catch (error) {
    errors.schema = getErrorMessage(error, 'Schema 状态加载失败')
  } finally {
    loading.schema = false
  }
}

async function loadBackups(): Promise<void> {
  loading.backups = true
  errors.backups = ''
  try {
    backups.value = await listBackups()
  } catch (error) {
    errors.backups = getErrorMessage(error, '备份列表加载失败')
  } finally {
    loading.backups = false
  }
}

async function loadClients(): Promise<void> {
  loading.clients = true
  errors.clients = ''
  try {
    clients.value = await listClientVersions()
  } catch (error) {
    errors.clients = getErrorMessage(error, '登录端版本加载失败')
  } finally {
    loading.clients = false
  }
}

function refreshAll(): void {
  void loadOverview()
  void loadSchema()
  void loadBackups()
  void loadClients()
}

function openJob(
  jobId: string,
  title: string,
  afterFinish?: (job: DataCenterJob) => void,
): void {
  bus.jobId = jobId
  bus.jobTitle = title
  bus.afterFinish = afterFinish ?? null
  bus.jobVisible = true
}

function handleJobFinished(job: DataCenterJob): void {
  if (bus.afterFinish) bus.afterFinish(job)
  if (job.state === 'succeeded') {
    ElMessage.success(job.message)
  } else if (job.state === 'failed') {
    ElMessage.error(job.error || job.message || '任务失败')
  }
  if (job.kind === 'schema-initialize' || job.kind === 'schema-fix') void loadSchema()
  if (job.kind === 'backup' || job.kind === 'restore') void loadBackups()
}

async function startSchemaInitialize(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将按 YAML 定义创建所有缺失的数据表。此操作只新增，不删除任何已有数据。',
      '初始化缺失表',
      { confirmButtonText: '开始初始化', cancelButtonText: '取消', type: 'warning' },
    )
    const result = await initializeSchema()
    openJob(result.jobId, '初始化缺失表')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    ElMessage.error(getErrorMessage(error, '启动初始化失败'))
  }
}

async function startSchemaFix(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将校验全部 YAML 表定义并补齐缺失列。只新增缺失列，不修改或删除已有列。',
      '修复缺失列',
      { confirmButtonText: '开始修复', cancelButtonText: '取消', type: 'warning' },
    )
    const result = await fixSchema()
    openJob(result.jobId, '修复缺失列')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    ElMessage.error(getErrorMessage(error, '启动修复失败'))
  }
}

async function handleBackup(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将执行 pg_dump 生成完整数据库备份，文件保存在服务器备份目录。',
      '执行数据库备份',
      { confirmButtonText: '开始备份', cancelButtonText: '取消', type: 'info' },
    )
    const result = await startBackup()
    openJob(result.jobId, '数据库备份')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    ElMessage.error(getErrorMessage(error, '启动备份失败'))
  }
}

async function handleDownload(file: BackupFile): Promise<void> {
  try {
    const blob = await downloadBackup(file.fileName)
    saveBlob(blob, file.fileName)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '下载备份失败'))
  }
}

function openRestore(file: BackupFile): void {
  restore.file = file
  restore.confirmText = ''
  restore.visible = true
}

async function submitRestore(): Promise<void> {
  if (!restore.file) return
  if (restore.confirmText.trim() !== restore.file.fileName) {
    ElMessage.warning('请输入完整备份文件名以确认')
    return
  }

  restore.submitting = true
  try {
    const result = await restoreBackup(restore.file.fileName, restore.confirmText.trim())
    restore.visible = false
    openJob(result.jobId, '数据库还原')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '启动还原失败'))
  } finally {
    restore.submitting = false
  }
}

function openUpload(): void {
  uploadInput.value?.click()
}

async function handleUploadPicked(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!file.name.toLowerCase().endsWith('.sql')) {
    ElMessage.warning('仅支持 .sql 格式的 pg_dump 备份')
    return
  }

  uploading.value = true
  try {
    await uploadBackup(file)
    ElMessage.success('备份已上传')
    await loadBackups()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '上传备份失败'))
  } finally {
    uploading.value = false
  }
}

async function handleCleanupLogs(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将按 app 30 天、biz 180 天、sec 365 天、err 90 天、perf 30 天清理过期日志，当前正在写入的文件不会删除。',
      '清理过期日志',
      { confirmButtonText: '开始清理', cancelButtonText: '取消', type: 'warning' },
    )
    const result = await cleanupLogs()
    openJob(result.jobId, '清理过期日志')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    ElMessage.error(getErrorMessage(error, '启动日志清理失败'))
  }
}

async function handleDetectSoftDelete(): Promise<void> {
  softDelete.loading = true
  softDelete.visible = true
  softDelete.data = null
  try {
    softDelete.data = await detectSoftDelete()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '检测软删除失败'))
    softDelete.visible = false
  } finally {
    softDelete.loading = false
  }
}

async function handleCleanupSoftDelete(): Promise<void> {
  const data = softDelete.data
  if (!data || data.totalToDelete === 0) return
  try {
    await ElMessageBox.confirm(
      `将先自动备份数据库，再物理删除 ${data.totalToDelete} 行软删除数据。` +
        '停保档案及其成员会保留。此操作不可撤销。',
      '清理软删除数据',
      { confirmButtonText: '确认清理', cancelButtonText: '取消', type: 'error' },
    )
  } catch {
    return
  }

  softDelete.cleaning = true
  try {
    const result = await cleanupSoftDeleted()
    softDelete.visible = false
    openJob(result.jobId, '清理软删除数据')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '启动软删除清理失败'))
  } finally {
    softDelete.cleaning = false
  }
}

const missingColumnsText = computed(() => {
  const map = schema.value?.missingColumns ?? {}
  return Object.entries(map).map(([table, columns]) => ({
    table,
    columns: columns.join('、'),
  }))
})

onMounted(refreshAll)
</script>

<template>
  <div class="nc-page data-center">
    <NcPageHeader
      title="数据中心"
      description="数据库状态监控、Schema 管理、备份还原、日志与软删除维护，以及数据导入、字典、标准与区划配置。"
      kicker="系统 · 数据中心"
    >
      <template #actions>
        <el-button :icon="Refresh" @click="refreshAll">刷新</el-button>
      </template>
    </NcPageHeader>

    <!-- 子模块 -->
    <div class="module-grid">
      <button
        v-for="item in modules"
        :key="item.title"
        type="button"
        class="module-card"
        @click="router.push(item.to)"
      >
        <span class="module-icon" :class="`module-icon--${item.tone}`">
          <el-icon :size="22"><component :is="item.icon" /></el-icon>
        </span>
        <span class="module-text">
          <strong>{{ item.title }}</strong>
          <small>{{ item.subtitle }}</small>
          <span class="module-desc">{{ item.description }}</span>
        </span>
        <span class="module-arrow">进入 →</span>
      </button>
    </div>

    <!-- Schema -->
    <NcCard title="Schema 状态">
      <template #actions>
        <el-button
          v-if="schema?.hasMissingTables"
          type="warning"
          plain
          @click="startSchemaInitialize"
        >
          初始化缺失表
        </el-button>
        <el-button
          v-if="schema?.hasMissingColumns"
          type="danger"
          plain
          @click="startSchemaFix"
        >
          修复缺失列
        </el-button>
      </template>

      <p v-if="errors.schema" class="error-note">{{ errors.schema }}</p>
      <el-skeleton v-else-if="loading.schema" :rows="2" animated />

      <template v-else-if="schema">
        <div class="schema-head">
          <span class="schema-icon" :style="{ color: schema.statusColor }">
            {{ schema.statusIcon }}
          </span>
          <div class="schema-text">
            <strong :style="{ color: schema.statusColor }">{{ schema.statusText }}</strong>
            <span>{{ schema.statusSummary }}</span>
          </div>
          <div class="schema-meta">
            <span>初始化：{{ schema.initializedAt || '—' }}</span>
            <span>初始化人：{{ schema.initializedBy || '—' }}</span>
          </div>
        </div>

        <div v-if="schema.hasMissingTables" class="schema-alert">
          <strong>缺失数据表（{{ schema.missingTables.length }}）</strong>
          <p>{{ schema.missingTables.join('、') }}</p>
        </div>

        <div v-if="schema.hasMissingColumns" class="schema-alert schema-alert--warn">
          <strong>缺失列（{{ schema.totalMissingColumns }}）</strong>
          <p v-for="item in missingColumnsText" :key="item.table">
            {{ item.table }}：{{ item.columns }}
          </p>
        </div>
      </template>
    </NcCard>

    <!-- 数据库状态 -->
    <NcCard title="数据库状态">
      <p v-if="errors.overview" class="error-note">{{ errors.overview }}</p>
      <el-skeleton v-else-if="loading.overview" :rows="2" animated />

      <div v-else-if="overview" class="stat-grid">
        <article class="stat-tile stat-tile--green">
          <span>连接状态</span>
          <strong>{{ overview.database.connected ? '已连接' : '未连接' }}</strong>
          <small>{{ overview.database.version }}</small>
        </article>
        <article class="stat-tile stat-tile--blue">
          <span>数据库大小</span>
          <strong>{{ overview.database.sizeText }}</strong>
          <small>{{ overview.database.totalTables }} 张表</small>
        </article>
        <article class="stat-tile stat-tile--amber">
          <span>总记录数</span>
          <strong>{{ overview.database.totalRecords.toLocaleString() }}</strong>
          <small>按表估算行数</small>
        </article>
        <article class="stat-tile stat-tile--purple">
          <span>活跃用户</span>
          <strong>{{ overview.database.activeUsers }}</strong>
          <small>当前启用账户</small>
        </article>
      </div>
    </NcCard>

    <!-- 维护操作 -->
    <NcCard title="维护操作" subtitle="备份、还原、日志与软删除清理均先备份或二次确认。">
      <div class="maintain-grid">
        <div class="maintain-item">
          <strong>数据库备份</strong>
          <span>执行 pg_dump，保留最近 30 天。</span>
          <div class="maintain-actions">
            <el-button type="primary" :icon="Download" @click="handleBackup">执行备份</el-button>
            <el-button :icon="Upload" :loading="uploading" @click="openUpload">
              上传备份
            </el-button>
            <input
              ref="uploadInput"
              type="file"
              accept=".sql"
              class="hidden-input"
              @change="handleUploadPicked"
            />
          </div>
        </div>

        <div class="maintain-item">
          <strong>清理过期日志</strong>
          <span>app 30 / biz 180 / sec 365 / err 90 / perf 30 天。</span>
          <div class="maintain-actions">
            <el-button type="warning" plain @click="handleCleanupLogs">清理日志</el-button>
          </div>
        </div>

        <div class="maintain-item">
          <strong>清理软删除数据</strong>
          <span>先检测再清理，清理前自动备份，停保档案保留。</span>
          <div class="maintain-actions">
            <el-button type="danger" plain :icon="Warning" @click="handleDetectSoftDelete">
              检测软删除
            </el-button>
          </div>
        </div>
      </div>

      <div class="backup-list">
        <div class="backup-head">
          <strong>备份文件（{{ backups.length }}）</strong>
          <el-button link :icon="Refresh" @click="loadBackups">刷新</el-button>
        </div>
        <p v-if="errors.backups" class="error-note">{{ errors.backups }}</p>
        <el-empty v-else-if="!loading.backups && backups.length === 0" description="暂无备份文件" :image-size="72" />
        <el-table v-else :data="backups" size="small" max-height="280">
          <el-table-column prop="fileName" label="文件名" min-width="240" show-overflow-tooltip />
          <el-table-column prop="createdAt" label="备份时间" width="160" />
          <el-table-column prop="sizeText" label="大小" width="100" align="right" />
          <el-table-column label="操作" width="170" align="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="handleDownload(row as BackupFile)">下载</el-button>
              <el-button link type="danger" @click="openRestore(row as BackupFile)">还原</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </NcCard>

    <!-- 表大小 -->
    <NcCard title="表大小统计">
      <template #actions>
        <el-button
          v-if="(overview?.tables?.length ?? 0) > 10"
          link
          type="primary"
          @click="showAllTables = !showAllTables"
        >
          {{ showAllTables ? '收起' : `显示全部 ${overview?.tables?.length ?? 0} 张` }}
        </el-button>
      </template>
      <p v-if="errors.overview" class="error-note">{{ errors.overview }}</p>
      <el-skeleton v-else-if="loading.overview" :rows="3" animated />
      <el-table v-else :data="visibleTables" size="small" max-height="420">
        <el-table-column prop="tableName" label="表名" min-width="220" show-overflow-tooltip />
        <el-table-column prop="tableComment" label="说明" min-width="200" show-overflow-tooltip />
        <el-table-column prop="sizeText" label="大小" width="110" align="right" />
        <el-table-column prop="rowCount" label="行数" width="120" align="right">
          <template #default="{ row }">
            {{ Number(row.rowCount ?? 0).toLocaleString() }}
          </template>
        </el-table-column>
      </el-table>
    </NcCard>

    <!-- 登录端版本 -->
    <NcCard title="客户端版本">
      <template #actions>
        <span v-if="clients" class="muted">{{ clients.summary }}</span>
        <el-button link :icon="Refresh" @click="loadClients">刷新</el-button>
      </template>
      <p v-if="errors.clients" class="error-note">{{ errors.clients }}</p>
      <el-empty
        v-else-if="!loading.clients && (clients?.items?.length ?? 0) === 0"
        description="暂无登录记录"
        :image-size="72"
      />
      <el-table v-else :data="clients?.items ?? []" size="small" max-height="320">
        <el-table-column prop="deviceName" label="设备" min-width="180" show-overflow-tooltip />
        <el-table-column label="用户" width="150">
          <template #default="{ row }">{{ row.fullName || row.userName }}</template>
        </el-table-column>
        <el-table-column prop="deviceKind" label="类型" width="100" />
        <el-table-column prop="appVersion" label="版本" width="120" />
        <el-table-column prop="lastSeenAt" label="最后在线" width="170" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.online ? 'success' : 'info'" size="small" effect="plain">
              {{ row.online ? '在线' : '离线' }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>
    </NcCard>

    <JobProgressDialog
      v-model="bus.jobVisible"
      :job-id="bus.jobId || null"
      :title="bus.jobTitle"
      @finished="handleJobFinished"
    />

    <!-- 还原确认 -->
    <el-dialog v-model="restore.visible" title="还原数据库" width="min(560px, 94vw)" append-to-body>
      <div class="restore-body">
        <el-alert
          type="error"
          :closable="false"
          show-icon
          title="还原会覆盖当前数据库"
          description="系统将先自动备份当前库，然后进入维护模式并整体回放备份；失败会回滚。还原期间所有用户暂时不可用。"
        />
        <p>备份文件：<strong>{{ restore.file?.fileName }}</strong></p>
        <p>请输入完整文件名以确认：</p>
        <el-input v-model="restore.confirmText" :placeholder="restore.file?.fileName" />
      </div>
      <template #footer>
        <el-button @click="restore.visible = false">取消</el-button>
        <el-button
          type="danger"
          :loading="restore.submitting"
          :disabled="restore.confirmText.trim() !== restore.file?.fileName"
          @click="submitRestore"
        >
          确认还原
        </el-button>
      </template>
    </el-dialog>

    <!-- 软删除检测 -->
    <el-dialog v-model="softDelete.visible" title="软删除检测" width="min(860px, 96vw)" append-to-body>
      <el-skeleton v-if="softDelete.loading" :rows="4" animated />
      <template v-else-if="softDelete.data">
        <div class="soft-summary">
          <span>软删除 {{ softDelete.data.totalSoftDeleted }} 行</span>
          <span>待清理 {{ softDelete.data.totalToDelete }} 行</span>
          <span>保留 {{ softDelete.data.totalKept }} 行</span>
        </div>

        <el-table :data="softDelete.data.tables" size="small" max-height="300">
          <el-table-column prop="tableName" label="表名" min-width="220" show-overflow-tooltip />
          <el-table-column prop="softDeleted" label="软删除" width="100" align="right" />
          <el-table-column prop="toDelete" label="待清理" width="100" align="right" />
          <el-table-column prop="kept" label="保留" width="100" align="right" />
        </el-table>

        <div v-if="softDelete.data.chainIssues.length > 0" class="chain-alert">
          <strong>档案链一致性异常（{{ softDelete.data.chainIssues.length }}）</strong>
          <ul>
            <li v-for="item in softDelete.data.chainIssues.slice(0, 20)" :key="item.oldApplicationId">
              旧档案 #{{ item.oldApplicationId }} {{ item.oldName }}（{{ item.oldStatus }}）→
              新档案 #{{ item.newApplicationId }}（{{ item.chainType }}）
            </li>
          </ul>
        </div>
      </template>
      <template #footer>
        <el-button @click="softDelete.visible = false">关闭</el-button>
        <el-button
          type="danger"
          :loading="softDelete.cleaning"
          :disabled="(softDelete.data?.totalToDelete ?? 0) === 0"
          @click="handleCleanupSoftDelete"
        >
          清理 {{ softDelete.data?.totalToDelete ?? 0 }} 行
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.data-center {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.module-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.module-card {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  width: 100%;
  padding: 16px 18px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-2);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
}

.module-card:hover {
  border-color: var(--nc-seal);
  box-shadow: var(--nc-shadow-xs);
  transform: translateY(-1px);
}

.module-icon {
  display: inline-flex;
  flex: none;
  width: 44px;
  height: 44px;
  align-items: center;
  justify-content: center;
  border-radius: var(--nc-radius-xs);
}

.module-icon--amber { background: #fff3e0; color: #b45309; }
.module-icon--blue { background: #e8f0fe; color: #1d4ed8; }
.module-icon--green { background: #e7f6ed; color: #15803d; }
.module-icon--purple { background: #f3e8ff; color: #7e22ce; }
.module-icon--teal { background: #e6fffa; color: #0f766e; }

.module-text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 3px;
}

.module-text strong {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  color: var(--nc-ink);
}

.module-text small {
  color: var(--nc-muted);
}

.module-desc {
  margin-top: 3px;
  font-size: var(--nc-text-xs);
  line-height: 1.65;
  color: var(--nc-muted-2);
}

.module-arrow {
  margin-left: auto;
  align-self: center;
  font-size: var(--nc-text-xs);
  color: var(--nc-seal-dark);
  white-space: nowrap;
}

.schema-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
}

.schema-icon {
  font-size: 26px;
  line-height: 1;
}

.schema-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.schema-text strong {
  font-size: var(--nc-text-md);
}

.schema-text span {
  color: var(--nc-muted);
}

.schema-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  margin-left: auto;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.schema-alert {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: var(--nc-radius-xs);
  background: rgba(180, 35, 24, 0.06);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.schema-alert--warn {
  background: rgba(180, 83, 9, 0.08);
}

.schema-alert p {
  margin: 6px 0 0;
  line-height: 1.7;
  word-break: break-all;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.stat-tile {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 14px 16px;
  border-radius: var(--nc-radius-sm);
  color: #fff;
}

.stat-tile span { font-size: var(--nc-text-xs); opacity: 0.9; }
.stat-tile strong { font-size: 22px; line-height: 1.3; }
.stat-tile small { font-size: 11px; opacity: 0.8; }

.stat-tile--green { background: linear-gradient(135deg, #2f7d5b, #1f5c42); }
.stat-tile--blue { background: linear-gradient(135deg, #2563eb, #1e40af); }
.stat-tile--amber { background: linear-gradient(135deg, #d97706, #b45309); }
.stat-tile--purple { background: linear-gradient(135deg, #7c3aed, #5b21b6); }

.maintain-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}

.maintain-item {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
}

.maintain-item strong { color: var(--nc-ink); }
.maintain-item span { color: var(--nc-muted); font-size: var(--nc-text-sm); line-height: 1.6; }
.maintain-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 6px; }

.hidden-input {
  display: none;
}

.backup-list {
  margin-top: 16px;
  border-top: 1px solid var(--nc-line-faint);
  padding-top: 14px;
}

.backup-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.restore-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.soft-summary {
  display: flex;
  gap: 18px;
  margin-bottom: 10px;
  color: var(--nc-ink);
}

.chain-alert {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: var(--nc-radius-xs);
  background: rgba(180, 83, 9, 0.08);
  font-size: var(--nc-text-sm);
}

.chain-alert ul {
  margin: 6px 0 0;
  padding-left: 18px;
  line-height: 1.7;
  color: var(--nc-muted-2);
}

.muted { color: var(--nc-muted); font-size: var(--nc-text-xs); }

.error-note {
  margin: 0;
  color: var(--nc-danger, #b42318);
  line-height: 1.7;
}

@media (max-width: 1100px) {
  .stat-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .maintain-grid { grid-template-columns: 1fr; }
}

@media (max-width: 767px) {
  .module-grid { grid-template-columns: 1fr; }
  .module-arrow { display: none; }
  .schema-meta { margin-left: 0; }
  .stat-grid { grid-template-columns: 1fr 1fr; }
}
</style>
