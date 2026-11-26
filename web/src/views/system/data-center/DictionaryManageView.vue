<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Plus, Refresh, Upload } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  createDictCategory,
  createDictItem,
  deleteDictCategory,
  deleteDictItem,
  listDictCategories,
  listDictItems,
  seedDictFromYaml,
  syncDictSchema,
  updateDictCategory,
  updateDictItem,
} from '@/api/dictionary'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { DictCategory, DictCategoryRequest, DictItem, DictItemRequest } from '@/types/dictionary'

const isNarrow = useMediaQuery('(max-width: 960px)')

const categories = ref<DictCategory[]>([])
const items = ref<DictItem[]>([])
const selectedCategoryId = ref<number | null>(null)

const loadingCategories = ref(false)
const loadingItems = ref(false)
const categoryError = ref('')
const itemError = ref('')
const syncing = ref(false)
const seeding = ref(false)

let itemTicket = 0

const selectedCategory = computed<DictCategory | null>(
  () => categories.value.find((item) => item.id === selectedCategoryId.value) ?? null,
)

/* ───────────────────────────── 加载 ───────────────────────────── */

async function loadCategories(preferId?: number): Promise<void> {
  loadingCategories.value = true
  categoryError.value = ''

  try {
    const list = await listDictCategories()
    categories.value = list

    const target = preferId ?? selectedCategoryId.value
    const matched = target === null ? null : list.find((item) => item.id === target)
    selectedCategoryId.value = matched?.id ?? list[0]?.id ?? null

    await loadItems()
  } catch (error) {
    categoryError.value = getErrorMessage(error, '字典分类加载失败')
  } finally {
    loadingCategories.value = false
  }
}

async function loadItems(): Promise<void> {
  const category = selectedCategory.value
  const ticket = ++itemTicket

  if (!category) {
    items.value = []
    itemError.value = ''
    loadingItems.value = false
    return
  }

  loadingItems.value = true
  itemError.value = ''
  try {
    const list = await listDictItems(category.category)
    if (ticket !== itemTicket) return
    items.value = list
  } catch (error) {
    if (ticket !== itemTicket) return
    items.value = []
    itemError.value = getErrorMessage(error, '字典项加载失败')
  } finally {
    if (ticket === itemTicket) loadingItems.value = false
  }
}

function selectCategory(id: number): void {
  if (selectedCategoryId.value === id) return
  selectedCategoryId.value = id
  void loadItems()
}

/* ───────────────────────────── 分类弹窗 ───────────────────────────── */

const categoryDialog = reactive({
  visible: false,
  editingId: 0,
  saving: false,
})

const categoryForm = reactive<DictCategoryRequest>({
  category: '',
  displayName: '',
  description: '',
  sortOrder: 0,
  isActive: true,
})

function openCategoryCreate(): void {
  categoryDialog.editingId = 0
  categoryForm.category = ''
  categoryForm.displayName = ''
  categoryForm.description = ''
  categoryForm.sortOrder = categories.value.length
  categoryForm.isActive = true
  categoryDialog.visible = true
}

function openCategoryEdit(category: DictCategory): void {
  categoryDialog.editingId = category.id
  categoryForm.category = category.category
  categoryForm.displayName = category.displayName
  categoryForm.description = category.description
  categoryForm.sortOrder = category.sortOrder
  categoryForm.isActive = category.isActive
  categoryDialog.visible = true
}

async function saveCategory(): Promise<void> {
  const categoryKey = categoryForm.category.trim()
  const displayName = categoryForm.displayName.trim()

  if (!categoryKey) {
    ElMessage.warning('请输入分类键')
    return
  }
  if (!displayName) {
    ElMessage.warning('请输入显示名称')
    return
  }
  if (categoryForm.sortOrder < 0) {
    ElMessage.warning('排序号不能为负数')
    return
  }

  const payload: DictCategoryRequest = {
    category: categoryKey,
    displayName,
    description: categoryForm.description.trim(),
    sortOrder: categoryForm.sortOrder,
    isActive: categoryForm.isActive,
  }

  categoryDialog.saving = true
  try {
    if (categoryDialog.editingId > 0) {
      const result = await updateDictCategory(categoryDialog.editingId, payload)
      ElMessage.success(result.message || '字典分类已更新')
      categoryDialog.visible = false
      await loadCategories(categoryDialog.editingId)
    } else {
      const result = await createDictCategory(payload)
      ElMessage.success(result.message || '字典分类已创建')
      categoryDialog.visible = false
      await loadCategories(result.id)
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存字典分类失败'))
  } finally {
    categoryDialog.saving = false
  }
}

async function handleDeleteCategory(category: DictCategory): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认删除字典分类「${category.displayName}」？删除后该分类及其字典项将不再出现在列表中。`,
      '删除字典分类',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const result = await deleteDictCategory(category.id)
    ElMessage.success(result.message || '字典分类已删除')
    if (selectedCategoryId.value === category.id) selectedCategoryId.value = null
    await loadCategories()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除字典分类失败'))
  }
}

/* ───────────────────────────── 字典项弹窗 ───────────────────────────── */

const itemDialog = reactive({
  visible: false,
  editingId: 0,
  saving: false,
})

const itemForm = reactive<DictItemRequest>({
  category: '',
  itemKey: '',
  itemValue: '',
  sortOrder: 0,
  isActive: true,
  description: '',
})

function openItemCreate(): void {
  const category = selectedCategory.value
  if (!category) return

  itemDialog.editingId = 0
  itemForm.category = category.category
  itemForm.itemKey = ''
  itemForm.itemValue = ''
  itemForm.sortOrder = items.value.length
  itemForm.isActive = true
  itemForm.description = ''
  itemDialog.visible = true
}

function openItemEdit(item: DictItem): void {
  itemDialog.editingId = item.id
  itemForm.category = item.category
  itemForm.itemKey = item.itemKey
  itemForm.itemValue = item.itemValue
  itemForm.sortOrder = item.sortOrder
  itemForm.isActive = item.isActive
  itemForm.description = item.description
  itemDialog.visible = true
}

async function saveItem(): Promise<void> {
  const category = selectedCategory.value
  if (!category) return

  const itemKey = itemForm.itemKey.trim()
  if (!itemKey) {
    ElMessage.warning('请输入字典项键')
    return
  }
  if (itemForm.sortOrder < 0) {
    ElMessage.warning('排序号不能为负数')
    return
  }

  const payload: DictItemRequest = {
    category: category.category,
    itemKey,
    itemValue: itemForm.itemValue.trim(),
    sortOrder: itemForm.sortOrder,
    isActive: itemForm.isActive,
    description: itemForm.description.trim(),
  }

  itemDialog.saving = true
  try {
    if (itemDialog.editingId > 0) {
      const result = await updateDictItem(itemDialog.editingId, payload)
      ElMessage.success(result.message || '字典项已更新')
    } else {
      const result = await createDictItem(payload)
      ElMessage.success(result.message || '字典项已创建')
    }
    itemDialog.visible = false
    await loadItems()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存字典项失败'))
  } finally {
    itemDialog.saving = false
  }
}

async function handleDeleteItem(item: DictItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认删除字典项「${item.itemKey}」？删除后该项将不再出现在列表中。`,
      '删除字典项',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const result = await deleteDictItem(item.id)
    ElMessage.success(result.message || '字典项已删除')
    await loadItems()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除字典项失败'))
  }
}

/* ───────────────────────────── 同步 / 初始化 ───────────────────────────── */

async function handleSyncSchema(): Promise<void> {
  syncing.value = true
  try {
    const result = await syncDictSchema()
    ElMessage.success(result.message || '字典表结构同步完成')
    await loadCategories()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '同步字典表结构失败'))
  } finally {
    syncing.value = false
  }
}

async function handleSeed(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将从 YAML 种子合并导入系统预置的字典分类与字典项，同名数据会被更新，人工新增的数据会保留。是否继续？',
      '从种子初始化',
      { confirmButtonText: '开始初始化', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  seeding.value = true
  try {
    const result = await seedDictFromYaml()
    ElMessage.success(result.message || '字典数据初始化完成')
    await loadCategories()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '字典数据初始化失败'))
  } finally {
    seeding.value = false
  }
}

onMounted(() => {
  void loadCategories()
})
</script>

<template>
  <div class="nc-page dictionary-page">
    <NcPageHeader
      title="数据字典管理"
      description="维护系统数据字典分类与字典项"
      kicker="系统 · 数据中心"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="syncing" @click="handleSyncSchema">
          同步表结构
        </el-button>
        <el-button type="primary" plain :icon="Upload" :loading="seeding" @click="handleSeed">
          从种子初始化
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="categoryError"
      class="page-error"
      type="error"
      :title="categoryError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadCategories()">重新加载</el-button>
    </el-alert>

    <div class="dictionary-layout">
      <!-- 分类 -->
      <NcCard title="字典分类" class="category-panel">
        <template #actions>
          <el-button size="small" type="primary" :icon="Plus" @click="openCategoryCreate">
            新增分类
          </el-button>
        </template>

        <el-skeleton v-if="loadingCategories && categories.length === 0" :rows="5" animated />

        <el-empty
          v-else-if="categories.length === 0"
          description="暂无字典分类"
          :image-size="80"
        >
          <el-button type="primary" plain :icon="Plus" @click="openCategoryCreate">
            新增分类
          </el-button>
        </el-empty>

        <div v-else class="category-list" role="listbox" aria-label="字典分类">
          <div
            v-for="category in categories"
            :key="category.id"
            class="category-row"
            :class="{ 'is-active': category.id === selectedCategoryId }"
            role="option"
            tabindex="0"
            :aria-selected="category.id === selectedCategoryId"
            @click="selectCategory(category.id)"
            @keydown.enter.prevent="selectCategory(category.id)"
            @keydown.space.prevent="selectCategory(category.id)"
          >
            <span class="category-main">
              <strong>{{ category.displayName }}</strong>
              <small class="mono">{{ category.category }}</small>
            </span>
            <span class="category-meta">
              <el-tag
                size="small"
                effect="plain"
                :type="category.isActive ? 'success' : 'info'"
              >
                {{ category.isActive ? '启用' : '停用' }}
              </el-tag>
              <span class="category-sort">排序 {{ category.sortOrder }}</span>
            </span>
            <span class="category-actions">
              <el-button
                link
                type="primary"
                :icon="Edit"
                title="编辑分类"
                aria-label="编辑分类"
                @click.stop="openCategoryEdit(category)"
              />
              <el-button
                link
                type="danger"
                :icon="Delete"
                title="删除分类"
                aria-label="删除分类"
                @click.stop="handleDeleteCategory(category)"
              />
            </span>
          </div>
        </div>
      </NcCard>

      <!-- 字典项 -->
      <NcCard
        :title="selectedCategory ? `字典项 - ${selectedCategory.displayName}` : '字典项'"
        class="items-panel"
      >
        <template #actions>
          <el-button
            size="small"
            type="primary"
            :icon="Plus"
            :disabled="!selectedCategory"
            @click="openItemCreate"
          >
            新增字典项
          </el-button>
        </template>

        <p v-if="itemError" class="error-note">
          {{ itemError }}
          <el-button link type="primary" @click="loadItems">重试</el-button>
        </p>

        <el-skeleton
          v-else-if="loadingItems && items.length === 0"
          :rows="5"
          animated
        />

        <el-empty
          v-else-if="!selectedCategory"
          description="请选择左侧字典分类"
          :image-size="80"
        />

        <el-empty
          v-else-if="items.length === 0"
          description="该分类暂无字典项"
          :image-size="80"
        >
          <el-button type="primary" plain :icon="Plus" @click="openItemCreate">
            新增字典项
          </el-button>
        </el-empty>

        <el-table
          v-else
          v-loading="loadingItems"
          :data="items"
          row-key="id"
          size="small"
          class="item-table"
        >
          <el-table-column label="键" min-width="150">
            <template #default="{ row }">
              <span class="item-key">{{ row.itemKey }}</span>
            </template>
          </el-table-column>
          <el-table-column label="值" min-width="180">
            <template #default="{ row }">
              <span class="item-value">{{ row.itemValue || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="88">
            <template #default="{ row }">
              <el-tag size="small" effect="plain" :type="row.isActive ? 'success' : 'info'">
                {{ row.isActive ? '启用' : '停用' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="排序" width="76" align="right">
            <template #default="{ row }">{{ row.sortOrder }}</template>
          </el-table-column>
          <el-table-column label="说明" min-width="180" show-overflow-tooltip>
            <template #default="{ row }">{{ row.description || '—' }}</template>
          </el-table-column>
          <el-table-column label="操作" width="132" align="right" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="openItemEdit(row as DictItem)">编辑</el-button>
              <el-button link type="danger" @click="handleDeleteItem(row as DictItem)">
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </NcCard>
    </div>

    <!-- 分类新增/编辑 -->
    <el-dialog
      v-model="categoryDialog.visible"
      :title="categoryDialog.editingId > 0 ? '编辑字典分类' : '新增字典分类'"
      :width="isNarrow ? '94%' : '520px'"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-position="top" @submit.prevent="saveCategory">
        <el-form-item label="分类键" required>
          <el-input
            v-model="categoryForm.category"
            maxlength="100"
            :disabled="categoryDialog.editingId > 0"
            placeholder="如 Gender"
          />
          <p v-if="categoryDialog.editingId > 0" class="form-hint">
            分类键是字典项的关联依据，创建后不可修改。
          </p>
        </el-form-item>
        <el-form-item label="显示名称" required>
          <el-input
            v-model="categoryForm.displayName"
            maxlength="100"
            placeholder="如 性别"
          />
        </el-form-item>
        <el-form-item label="说明">
          <el-input
            v-model="categoryForm.description"
            type="textarea"
            :rows="3"
            maxlength="2000"
            show-word-limit
            placeholder="选填"
          />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="排序">
            <el-input-number
              v-model="categoryForm.sortOrder"
              :min="0"
              :step="1"
              controls-position="right"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="categoryForm.isActive" />
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="categoryDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="categoryDialog.saving" @click="saveCategory">
          保存
        </el-button>
      </template>
    </el-dialog>

    <!-- 字典项新增/编辑 -->
    <el-dialog
      v-model="itemDialog.visible"
      :title="itemDialog.editingId > 0 ? '编辑字典项' : '新增字典项'"
      :width="isNarrow ? '94%' : '520px'"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-position="top" @submit.prevent="saveItem">
        <el-form-item label="所属分类">
          <el-input
            :model-value="
              selectedCategory
                ? `${selectedCategory.displayName}（${selectedCategory.category}）`
                : ''
            "
            disabled
          />
        </el-form-item>
        <el-form-item label="键" required>
          <el-input v-model="itemForm.itemKey" maxlength="100" placeholder="如 Male" />
        </el-form-item>
        <el-form-item label="值">
          <el-input v-model="itemForm.itemValue" maxlength="500" placeholder="如 男" />
        </el-form-item>
        <el-form-item label="说明">
          <el-input
            v-model="itemForm.description"
            type="textarea"
            :rows="2"
            maxlength="200"
            show-word-limit
            placeholder="选填"
          />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="排序">
            <el-input-number
              v-model="itemForm.sortOrder"
              :min="0"
              :step="1"
              controls-position="right"
              class="full-width"
            />
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="itemForm.isActive" />
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="itemDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="itemDialog.saving" @click="saveItem">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.dictionary-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.page-error {
  margin: 0;
}

.dictionary-layout {
  display: grid;
  grid-template-columns: 320px minmax(0, 1fr);
  gap: 15px;
  align-items: start;
}

.category-panel {
  min-width: 0;
}

.category-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 560px;
  overflow-y: auto;
}

.category-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  cursor: pointer;
  outline: none;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.category-row:hover {
  border-color: var(--nc-line-strong);
}

.category-row:focus-visible {
  border-color: var(--nc-seal);
  box-shadow: 0 0 0 2px var(--nc-seal-soft);
}

.category-row.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.category-main {
  display: flex;
  flex: 1;
  min-width: 0;
  flex-direction: column;
  gap: 2px;
}

.category-main strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.02em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.category-main small {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.category-meta {
  display: flex;
  flex: none;
  flex-direction: column;
  align-items: flex-end;
  gap: 3px;
}

.category-sort {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
}

.category-actions {
  display: flex;
  flex: none;
  flex-direction: column;
  gap: 0;
}

.category-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.items-panel {
  min-width: 0;
}

.item-table {
  width: 100%;
}

.item-key {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 700;
}

.item-value {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.error-note {
  margin: 0 0 10px;
  color: var(--nc-danger, #b42318);
  line-height: 1.7;
}

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

@media (max-width: 960px) {
  .dictionary-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .category-list {
    max-height: 320px;
  }
}

@media (max-width: 767px) {
  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
