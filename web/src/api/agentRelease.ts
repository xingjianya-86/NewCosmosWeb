/**
 * Agent 版本清单（静态站下发，仅用于 Web 观测最新版本，不做任何升级操作）。
 * 清单可能尚未上传：404 / 断网一律返回 null，页面按「拿不到清单」降级展示。
 */
export interface AgentRelease {
  version: string
  url: string
  sha256?: string
  notes?: string
  publishedAt?: string
}

const AGENT_LATEST_URL =
  'https://cosmos-static.sua17.cn:20000/downloads/windows/agent-latest.json'

export async function getAgentRelease(): Promise<AgentRelease | null> {
  try {
    const response = await fetch(`${AGENT_LATEST_URL}?t=${Date.now()}`, { cache: 'no-store' })
    if (!response.ok) return null
    return (await response.json()) as AgentRelease
  } catch {
    return null
  }
}

/**
 * 按 '.' 分段数值比较版本号（格式如 2.0.20261128）。
 * 返回 1（a 更新）/ 0（相同或任一侧为空、无法比较）/ -1（a 较旧）。
 */
export function compareAgentVersions(
  a: string | null | undefined,
  b: string | null | undefined,
): number {
  if (!a || !b) return 0
  const pa = a.split('.')
  const pb = b.split('.')
  const len = Math.max(pa.length, pb.length)
  for (let i = 0; i < len; i++) {
    const na = i < pa.length ? Number(pa[i]) : 0
    const nb = i < pb.length ? Number(pb[i]) : 0
    if (Number.isNaN(na) || Number.isNaN(nb)) return 0
    if (na > nb) return 1
    if (na < nb) return -1
  }
  return 0
}
