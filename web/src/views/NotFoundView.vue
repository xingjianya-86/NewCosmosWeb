<script setup lang="ts">
import { useRouter } from 'vue-router'
import { Back, House } from '@element-plus/icons-vue'

const router = useRouter()

function goBack(): void {
  if (window.history.length > 1) router.back()
  else void router.replace('/')
}
</script>

<template>
  <div class="notfound">
    <div class="notfound-card">
      <p class="code">404</p>
      <h1>页面不存在</h1>
      <p class="desc">您访问的地址可能已被移动或删除，请检查链接是否正确。</p>
      <div class="actions">
        <el-button :icon="Back" @click="goBack">返回上一页</el-button>
        <el-button type="primary" :icon="House" @click="router.replace('/')">回到首页</el-button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.notfound {
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: 24px;
  background:
    radial-gradient(600px 320px at 50% 0%, rgba(28, 95, 158, 0.08), transparent 70%),
    var(--nc-bg);
}

.notfound-card {
  width: 100%;
  max-width: 440px;
  padding: 40px 32px;
  border: 1px solid var(--nc-border);
  border-radius: 14px;
  background: #fff;
  box-shadow: var(--nc-shadow);
  text-align: center;
}

.code {
  color: var(--nc-primary);
  font-size: 56px;
  font-weight: 700;
  line-height: 1;
  letter-spacing: 4px;
  opacity: 0.9;
}

.notfound-card h1 {
  margin-top: 16px;
  font-size: 20px;
  font-weight: 600;
}

.desc {
  margin-top: 10px;
  color: var(--nc-text-secondary);
  font-size: 13px;
  line-height: 1.7;
}

.actions {
  display: flex;
  justify-content: center;
  gap: 10px;
  margin-top: 26px;
}

@media (max-width: 480px) {
  .notfound-card {
    padding: 32px 20px;
  }

  .actions {
    flex-direction: column-reverse;
  }

  .actions .el-button {
    width: 100%;
    margin-left: 0;
  }
}
</style>
