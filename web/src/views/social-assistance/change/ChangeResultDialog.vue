<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { ChangeOperationResult } from '@/types/changeManagement'
import { formatAmount } from './format'

const props = defineProps<{
  modelValue: boolean
  result: ChangeOperationResult | null
  /** 单人户整户停止等场景的标题覆盖。 */
  title?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  done: []
}>()

const router = useRouter()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const hasNewApplication = computed(() => (props.result?.newApplicationId ?? 0) > 0)
const classificationChanged = computed(
  () =>
    Boolean(props.result) &&
    props.result?.oldClassification !== props.result?.newClassification,
)
const navigatingAway = ref(false)

const dialogTitle = computed(() => props.title || '办理结果')

function goNewApplication(): void {
  if (!props.result || !hasNewApplication.value) return
  const newId = props.result.newApplicationId
  navigatingAway.value = true
  visible.value = false
  void router.push(`/applications/${newId}/edit`)
}

function handleClosed(): void {
  if (navigatingAway.value) {
    navigatingAway.value = false
    return
  }
  emit('done')
}

function close(): void {
  visible.value = false
}
</script>

<template>
  <el-dialog v-model="visible" :title="dialogTitle" width="min(560px, 94vw)" append-to-body @closed="handleClosed">
    <div v-if="result" class="result">
      <div class="result__hero">
        <span class="result__badge">办理成功</span>
        <p class="result__no">变更记录 #{{ result.changeId || '—' }}</p>
      </div>

      <div class="result__grid">
        <div class="result__item">
          <span>变更前分类</span>
          <strong>{{ result.oldClassificationName || '—' }}</strong>
        </div>
        <div class="result__item">
          <span>变更后分类</span>
          <strong :class="{ 'is-accent': classificationChanged }">
            {{ result.newClassificationName || '—' }}
          </strong>
        </div>
        <div class="result__item">
          <span>变更前保障金（户月）</span>
          <strong>{{ formatAmount(result.oldGuaranteeAmount) }}</strong>
        </div>
        <div class="result__item">
          <span>变更后保障金（户月）</span>
          <strong>{{ formatAmount(result.newGuaranteeAmount) }}</strong>
        </div>
      </div>

      <div class="result__tags">
        <span v-if="result.rebuilt" class="tag tag--info">已停旧建新</span>
        <span v-if="result.triggeredGracePeriod" class="tag tag--amber">触发渐退期</span>
        <span v-if="result.triggeredStop" class="tag tag--seal">触发停保</span>
        <span v-if="!result.rebuilt" class="tag">未生成新档案</span>
      </div>

      <el-alert
        v-if="result.triggeredStop && !hasNewApplication"
        type="warning"
        :closable="false"
        show-icon
        title="该户保障已停止"
        description="档案已转为已停保，本次未建立新档案。"
      />

      <el-alert
        v-else-if="hasNewApplication"
        type="info"
        :closable="false"
        show-icon
        title="已生成新档案（草稿）"
        :description="`新档案 ID：${result.newApplicationId}。请前往完善经济信息并完成 Step5 分类判定与归档。`"
      />

      <p class="result__note">
        页面展示的分类与保障金均为 Core 判定结果，前端未参与重算。
      </p>
    </div>

    <template #footer>
      <el-button @click="close">关闭</el-button>
      <el-button v-if="hasNewApplication" type="primary" @click="goNewApplication">
        去完善新档案
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.result {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.result__hero {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
}

.result__badge {
  display: inline-flex;
  padding: 2px 9px;
  border: 1px solid rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
}

.result__no {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.result__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.result__item {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.result__item span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.result__item strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.result__item strong.is-accent {
  color: var(--nc-seal);
}

.result__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.tag {
  display: inline-flex;
  padding: 1px 8px;
  border: 1px solid var(--nc-line);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
}

.tag--info {
  border-color: rgba(33, 89, 138, 0.3);
  background-color: rgba(33, 89, 138, 0.08);
  color: rgb(33, 89, 138);
}

.tag--amber {
  border-color: rgba(166, 106, 0, 0.32);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber);
}

.tag--seal {
  border-color: rgba(171, 50, 39, 0.35);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.result__note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

@media (max-width: 600px) {
  .result__grid {
    grid-template-columns: 1fr;
  }
}
</style>
